#!/usr/bin/env python3
"""Windows-native A2854 raw BLE controller-input probe (no mouse/VM/WDK).

Uses paired-device WinRT APIs directly (not Bleak's service filtering).
- One existing *paired* DL1 device (no pairing, no adapter/driver changes).
- GATT HID 0x1812 / 0x2A4D access diagnostic with explicit ACCESS_DENIED.
- Distinguishes duplicate Report characteristics via Report Reference 0x2908.
- Subscribes ONLY to 0xFB buttons / 0xFC touch, never microphone (0xFA).
- Subscription writes the standard CCCD notify value; optionally --enable-input
  sends the ALREADY OBSERVED 0xAF enable byte to exactly report ID 0xF0.
- Prints payload bytes from 0xFB / 0xFC only, no BLE addresses/device IDs.
- No driver reinstallation, device restart, WSL, raw HCI, or PnP changes.

Known limitation: Windows may reserve HID-over-GATT 0x1812 for the OS
and return ACCESS_DENIED before enumeration. This tool cannot override it.

Requires native Windows 11 and PyWinRT dependencies from Bleak:
    py -3 -m pip install "bleak>=2,<4"
"""
from __future__ import annotations

import argparse
import asyncio
from dataclasses import dataclass
import sys

HID_SERVICE = "00001812-0000-1000-8000-00805f9b34fb"
HID_REPORT = "00002a4d-0000-1000-8000-00805f9b34fb"
REPORT_REFERENCE = "00002908-0000-1000-8000-00805f9b34fb"
TARGET_INPUTS = frozenset((0xFB, 0xFC))
ENABLE_FEATURE_ID = 0xF0

# No microphone 0xFA, report 0x60/0xFD/0xFE, or arbitrary vendor data captured.


@dataclass(frozen=True)
class ReportRef:
    report_id: int
    report_type: int


def parse_report_reference(raw: bytes) -> ReportRef | None:
    """HOGP 0x2908 = 2 bytes: report_id; report_type (1/2/3)."""
    if len(raw) != 2 or raw[1] not in (1, 2, 3):
        return None
    return ReportRef(raw[0], raw[1])


def status(value: object) -> str:
    return str(getattr(value, "name", str(value))).upper()


def classify_hid_status(gatt_status: object) -> str:
    text = status(gatt_status)
    if text in ("ACCESS_DENIED", "ACCESSDENIED"):
        return "WINDOWS_PROTECTED_HID_SERVICE"
    if text == "SUCCESS":
        return "ACCESS_GRANTED"
    return "GATT_" + text


def safe_exception(exc: BaseException) -> str:
    """Never print WinRT exceptions, which can contain private PnP IDs."""
    number = getattr(exc, "winerror", None)
    if number is None:
        number = getattr(exc, "hresult", None)
    return type(exc).__name__ + (
        f" (HRESULT=0x{number & 0xFFFFFFFF:08X})" if isinstance(number, int) else ""
    )


async def bounded(awaitable, seconds: float = 18.0):
    return await asyncio.wait_for(awaitable, timeout=seconds)


async def get_reports(service, BluetoothCacheMode):
    """Return (classified 2A4D reports, result status, discovered count).

    No subscription/write is attempted on ambiguous or inaccessible reports.
    """
    result = await bounded(service.get_characteristics_with_cache_mode_async(
        BluetoothCacheMode.UNCACHED
    ))
    state = classify_hid_status(result.status)
    if state != "ACCESS_GRANTED":
        return [], state, 0
    reports = []
    all_reports = [
        c for c in result.characteristics
        if str(c.uuid).lower() == HID_REPORT
    ]
    for char in all_reports:
        try:
            found = await bounded(char.get_descriptors_with_cache_mode_async(
                BluetoothCacheMode.UNCACHED
            ))
            if status(found.status) != "SUCCESS":
                print(f"  Report Reference query: {status(found.status)}")
                continue
            matching = [
                d for d in found.descriptors
                if str(d.uuid).lower() == REPORT_REFERENCE
            ]
            if len(matching) != 1:
                print("  Report Reference unavailable or ambiguous; skipping characteristic.")
                continue
            value = await bounded(matching[0].read_value_async())
            if status(value.status) != "SUCCESS":
                print(f"  Report Reference read: {status(value.status)}")
                continue
            ref = parse_report_reference(bytes(value.value))
            if ref is not None:
                reports.append((char, ref))
                print(f"  HID report ref: ID=0x{ref.report_id:02X}, type={ref.report_type}")
        except (OSError, RuntimeError, asyncio.TimeoutError) as exc:
            print(f"  Report Reference read failed: {safe_exception(exc)}")
    return reports, state, len(all_reports)


async def receive(device, cache, cccd, seconds: int, enable: bool) -> int:
    print("Phase 1: uncached GATT service discovery")
    services_result = await bounded(
        device.get_gatt_services_with_cache_mode_async(cache.UNCACHED),
        28,
    )
    print(f"  Services status: {status(services_result.status)}")
    if status(services_result.status) != "SUCCESS":
        print("No BLE notifications tested: remote unreachable or service discovery failed.")
        # Only a transport-level UNREACHABLE permits a bounded retry.
        return 7 if status(services_result.status) == "UNREACHABLE" else 2
    services = list(services_result.services)
    targets = [s for s in services if str(s.uuid).lower() == HID_SERVICE]
    print(f"  Exposed services={len(services)}, HID 0x1812={len(targets)}")
    try:
        if len(targets) != 1:
            print("No unique HID service. No subscription attempted.")
            return 3

        print("Phase 2: enumerate 0x2A4D HID Report characteristics")
        reports, state, count = await get_reports(targets[0], cache)
        print(f"  HID access: {state}; Report characteristics={count}")
        if state == "WINDOWS_PROTECTED_HID_SERVICE":
            print("BLOCKED: Windows reserves HID 0x1812 for the OS.")
            print("Linux-style GATT input is not available through this WinRT path.")
            print("No VM or driver alteration is needed to establish this API boundary.")
            return 3
        if state != "ACCESS_GRANTED":
            print("No notification attempt: characteristic discovery not permitted.")
            return 3
        inputs = [(c, r) for c, r in reports if r.report_type == 1 and r.report_id in TARGET_INPUTS]
        if not inputs:
            print("No verified 0xFB/0xFC Input Report Reference; refusing blind subscription.")
            return 4

        print(f"Phase 3: subscribe to verified input reports ({len(inputs)} targets)")
        # WinRT ValueChanged callbacks might occur on a non-asyncio thread.
        loop = asyncio.get_running_loop()
        queue: asyncio.Queue[tuple[int, bytes]] = asyncio.Queue(maxsize=500)
        active: list[tuple[object, object]] = []
        totals = {0xFB: 0, 0xFC: 0}
        def handler_for(rid: int):
            def on_value(sender, args):
                try:
                    raw = bytes(args.characteristic_value)
                    loop.call_soon_threadsafe(receive_value, rid, raw)
                except (OSError, RuntimeError, ValueError):
                    pass
            return on_value
        def receive_value(rid: int, raw: bytes):
            if not queue.full():
                queue.put_nowait((rid, raw))

        try:
            for char, ref in inputs:
                token = char.add_value_changed(handler_for(ref.report_id))
                # Register cleanup *before* CCCD write. Failure can leave an
                # event handler installed; cleanup always removes that token.
                entry = (char, token)
                try:
                    notify_state = await bounded(
                        char.write_client_characteristic_configuration_descriptor_async(
                            cccd.NOTIFY
                        )
                    )
                except (OSError, RuntimeError, asyncio.TimeoutError) as exc:
                    char.remove_value_changed(token)
                    print(f"  0x{ref.report_id:02X} CCCD subscribe exception: {safe_exception(exc)}")
                    continue
                if status(notify_state) != "SUCCESS":
                    char.remove_value_changed(token)
                    print(f"  0x{ref.report_id:02X} CCCD subscribe: {status(notify_state)}")
                    continue
                active.append(entry)
                print(f"  0x{ref.report_id:02X} CCCD subscribe: SUCCESS")
            if not active:
                print("No notifications active. Windows or remote denied CCCD subscription.")
                return 4

            if enable:
                # Previously observed A2854 feature ID 0xF0 accepts 0xAF.
                features = [(c, r) for c, r in reports
                            if r.report_id == ENABLE_FEATURE_ID and r.report_type in (2, 3)]
                if len(features) != 1:
                    print("Enable not sent: expected a unique writable Report 0xF0.")
                else:
                    char, ref = features[0]
                    try:
                        result = await bounded(char.write_value_async(bytes([0xAF])))
                        print(f"  Report 0xF0 input-enable 0xAF write: {status(result)}")
                    except (OSError, RuntimeError, asyncio.TimeoutError) as exc:
                        print(f"  Report 0xF0 input-enable failed: {safe_exception(exc)}")
            else:
                print("Input-enable 0xAF NOT sent (opt in with --enable-input).")

            print(f"Now press remote buttons and touch the pad for {seconds} seconds.")
            end_time = loop.time() + seconds
            while loop.time() < end_time:
                try:
                    rid, raw = await asyncio.wait_for(
                        queue.get(), timeout=min(1.0, max(0.001, end_time - loop.time()))
                    )
                except asyncio.TimeoutError:
                    continue
                totals[rid] += 1
                if totals[rid] <= 60:
                    visible = " ".join(f"{b:02X}" for b in raw[:40])
                    print(f"  input 0x{rid:02X}: len={len(raw)} raw={visible}")
            print(f"Received: buttons(0xFB)={totals[0xFB]}, touch(0xFC)={totals[0xFC]}")
            return 0 if any(totals.values()) else 5
        finally:
            for char, token in active:
                try:
                    char.remove_value_changed(token)
                except (OSError, RuntimeError):
                    pass
                try:
                    await bounded(
                        char.write_client_characteristic_configuration_descriptor_async(
                            cccd.NONE
                        ), 5
                    )
                except (OSError, RuntimeError, asyncio.TimeoutError):
                    pass
            print("GATT subscriptions cleaned up.")
    finally:
        for service in services:
            try:
                service.close()
            except (OSError, RuntimeError):
                pass


async def run(name_prefix: str, index: int | None, seconds: int,
              enable: bool, scan_before: int = 0, attempts: int = 1) -> int:
    if sys.platform != "win32":
        raise RuntimeError("Windows native Python required, NOT WSL.")
    # Reuse the already-tested paired-device enumerator, bypassing Bleak's
    # characteristic filtering to expose exact Windows GATT status.
    from gatt_probe import list_paired, runtime_modules
    from winrt.windows.devices.bluetooth.genericattributeprofile import (
        GattClientCharacteristicConfigurationDescriptorValue as Cccd
    )

    dev_class, cache, enum_class = runtime_modules()
    paired = await bounded(list_paired(dev_class, enum_class))
    matches = [c for c in paired if c.name.casefold().startswith(name_prefix.casefold())]
    print(f"Paired BLE devices={len(paired)}, name prefix matches={len(matches)}")
    if index is None:
        if len(matches) != 1:
            print("Not exactly one previously paired DL1 device; no connection attempted.")
            print("Specify --index N only after checking prior gatt_probe enumeration.")
            return 2
        selected = matches[0]
    else:
        if index < 0 or index >= len(paired):
            print("Index outside paired-device list.")
            return 2
        if not paired[index].name.casefold().startswith(name_prefix.casefold()):
            print("Index is NOT a DL1 device; aborting to avoid unrelated access.")
            return 2
        selected = paired[index]
    if scan_before:
        # The existing scanner handles advertisement signals ONLY, not
        # pairing. End the scan before attempting a GATT connection.
        from adv_probe import scan
        print("Passive advertisement scan BEFORE connecting:")
        scan_result = await scan(scan_before)
        if scan_result == 2:
            print("Advertisement scan failed; proceeding with independent GATT test.")

    for attempt in range(1, attempts + 1):
        print(f"Opening paired DL1 remote, attempt {attempt}/{attempts} (no re-pairing)")
        device = await bounded(dev_class.from_id_async(selected.device_id), 25)
        if device is None:
            print("Windows returned no BLE device for this pairing record.")
            return 2
        try:
            result = await receive(device, cache, Cccd, seconds, enable)
        finally:
            device.close()
        if result != 7 or attempt == attempts:
            return result
        print("GATT UNREACHABLE: waking remote and retrying after 5 seconds.")
        print("Tap the remote Back button once; no PnP restart or pairing reset.")
        await asyncio.sleep(5)
    return 7


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--name-prefix", default="DL1",
                   help="Require paired device name prefix (default DL1)")
    p.add_argument("--index", type=int,
                   help="Optional paired-device index (only if also DL1)")
    p.add_argument("--seconds", type=int, default=25)
    p.add_argument("--scan-before", type=int, default=0,
                   help="Optional passive BLE advertisement scan, seconds (5..30)")
    p.add_argument("--attempts", type=int, default=1,
                   help="GATT attempts (1..3), retries only UNREACHABLE")
    p.add_argument("--enable-input", action="store_true",
                   help="Explicitly write previously observed 0xAF to Report ID 0xF0")
    a = p.parse_args()
    if not 5 <= a.seconds <= 90:
        p.error("--seconds must be between 5 and 90")
    if not a.name_prefix or len(a.name_prefix) < 2:
        p.error("name prefix must be at least 2 characters")
    if a.scan_before and not 5 <= a.scan_before <= 30:
        p.error("--scan-before must be between 5 and 30 (or omitted)")
    if not 1 <= a.attempts <= 3:
        p.error("--attempts must be between 1 and 3")
    try:
        return asyncio.run(run(
            a.name_prefix, a.index, a.seconds, a.enable_input,
            scan_before=a.scan_before, attempts=a.attempts
        ))
    except (OSError, RuntimeError, asyncio.TimeoutError, ImportError) as exc:
        print(f"Probe stopped: {safe_exception(exc)}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
