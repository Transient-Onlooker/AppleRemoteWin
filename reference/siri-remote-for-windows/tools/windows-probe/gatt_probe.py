#!/usr/bin/env python3
"""Windows-native, read-only WinRT BLE/GATT diagnostic for the paired Siri Remote.

No pairing, unpairing, characteristic writes, HID enable writes, or audio capture.
Requires native Windows and PyWinRT modules supplied by "bleak".
"""

from __future__ import annotations

import argparse
import asyncio
from collections import Counter
from dataclasses import dataclass
import sys

HID_SERVICE = "00001812-0000-1000-8000-00805f9b34fb"
HID_REPORT = "00002a4d-0000-1000-8000-00805f9b34fb"
BATTERY_SERVICE = "0000180f-0000-1000-8000-00805f9b34fb"


def status_text(value: object) -> str:
    """PyWinRT enum values support .name; preserve numeric value when possible."""
    return getattr(value, "name", str(value))


@dataclass
class Candidate:
    name: str
    device_id: str


@dataclass
class AttemptResult:
    """Per-attempt connection evidence; never contains device identifiers."""
    discovery_status: str | None = None


def runtime_modules():
    """Import only after CLI validation so --help and static checks work anywhere."""
    if sys.platform != "win32":
        raise RuntimeError("Native Windows required (not WSL/Linux).")
    try:
        from winrt.windows.devices.bluetooth import BluetoothLEDevice, BluetoothCacheMode
        from winrt.windows.devices.enumeration import DeviceInformation
    except ImportError as exc:
        raise RuntimeError(
            "Missing Windows WinRT support. Run: py -3 -m pip install \"bleak>=2,<4\""
        ) from exc
    return BluetoothLEDevice, BluetoothCacheMode, DeviceInformation


async def list_paired(BluetoothLEDevice, DeviceInformation) -> list[Candidate]:
    selector = BluetoothLEDevice.get_device_selector_from_pairing_state(True)
    devices = await DeviceInformation.find_all_async_aqs_filter(selector)
    candidates = [Candidate(name=d.name or "(unnamed BLE)", device_id=d.id) for d in devices]
    candidates.sort(key=lambda d: d.name.lower())
    return candidates


def hid_snapshot() -> Counter:
    """Summarize Windows HID interfaces without retaining device paths/IDs."""
    from hid_probe import enumerate_hid
    return Counter(
        (d.vendor, d.product, d.usage_page, d.usage, d.input_length)
        for d in enumerate_hid()
    )


def print_hid_comparison(before: Counter, after: Counter) -> None:
    """Print counts and coarse IDs only; a difference is not proof of A2854 ownership."""
    print(f"HID interfaces before={sum(before.values())} after={sum(after.values())}")
    additions = after - before
    removed = before - after
    for (vid, pid, page, usage, length), count in sorted(
        additions.items(), key=lambda item: str(item[0])
    ):
        fmt = lambda v: "unknown" if v is None else f"{v:04X}"
        print(f"  Added {count}: VID={fmt(vid)} PID={fmt(pid)} "
              f"usage={fmt(page)}:{fmt(usage)} input_length={length}")
    if removed:
        print(f"  Removed interface signatures: {sum(removed.values())}")
    if not additions:
        print("No new HID interface signatures appeared during this GATT attempt.")
    print("Interface changes do not prove which device owns them; no HID data was read.")


async def probe_one(selected: Candidate, BluetoothLEDevice, BluetoothCacheMode,
                    compare_hid: bool = False,
                    attempt_result: AttemptResult | None = None) -> int:
    # Do not print the device ID: Windows IDs can embed unique BLE addresses.
    before = None
    if compare_hid:
        try:
            before = hid_snapshot()
        except OSError as exc:
            print(f"HID snapshot unavailable: {exc}")
    print(f"Opening paired BLE device: {selected.name!r}")
    device = await BluetoothLEDevice.from_id_async(selected.device_id)
    if device is None:
        print("FromIdAsync returned null: pairing record exists, but device is not available.")
        return 2
    try:
        # WinRT device.name can be a Bluetooth MAC address rather than a friendly name.
        # The paired device name above is enough for these diagnostics.
        print(f"Initial connection status: {status_text(device.connection_status)}")
        print("Requesting uncached GATT services (this may initiate a BLE connection)...")
        result = await device.get_gatt_services_with_cache_mode_async(
            BluetoothCacheMode.UNCACHED
        )
        discovery_status = status_text(result.status)
        if attempt_result is not None:
            attempt_result.discovery_status = discovery_status.upper()
        print(f"Service discovery status: {discovery_status}")
        if result.protocol_error is not None:
            print(f"ATT protocol error: 0x{result.protocol_error:02X}")
        print(f"Connection status after discovery: {status_text(device.connection_status)}")
        if status_text(result.status).lower() != "success":
            print("Service discovery failed. No HID compatibility conclusion yet.")
            return 2
        services = list(result.services)
        print(f"GATT services exposed to this Windows process: {len(services)}")
        try:
            for service in services:
                print(f"  service={str(service.uuid).lower()}")
            hid_services = [s for s in services if str(s.uuid).lower() == HID_SERVICE]
            if not hid_services:
                print("HID 0x1812 is NOT exposed in the WinRT service list.")
                print("Windows may claim/hide HID, or service discovery may be incomplete.")
                return 1
            denied = False
            for service in hid_services:
                print("HID 0x1812 present. Requesting its characteristics (read-only)...")
                try:
                    chars = await service.get_characteristics_with_cache_mode_async(
                        BluetoothCacheMode.UNCACHED
                    )
                except (OSError, PermissionError) as exc:
                    denied = True
                    print(f"HID characteristics access exception: {type(exc).__name__}: {exc}")
                    continue
                print(f"HID characteristic discovery: {status_text(chars.status)}")
                if chars.protocol_error is not None:
                    print(f"ATT protocol error: 0x{chars.protocol_error:02X}")
                if status_text(chars.status).lower() != "success":
                    denied = True
                    continue
                reports = [c for c in chars.characteristics if str(c.uuid).lower() == HID_REPORT]
                print(f"Report 0x2A4D instances exposed: {len(reports)}")
                for index, characteristic in enumerate(reports):
                    print(f"  Report[{index}] handle={characteristic.attribute_handle} "
                          f"properties={characteristic.characteristic_properties}")
                print("NO notifications or writes attempted. Reaching this point "
                      "does NOT prove 0xFB/0xFC delivery.")
            return 1 if denied else 0
        finally:
            for service in services:
                service.close()
    finally:
        # Capture while the WinRT BluetoothLEDevice is still open.
        if before is not None:
            try:
                print_hid_comparison(before, hid_snapshot())
            except OSError as exc:
                print(f"Second HID snapshot unavailable: {exc}")
        device.close()


async def run(index: int | None, compare_hid: bool = False,
              attempts: int = 1, scan_before: int = 0) -> int:
    BluetoothLEDevice, BluetoothCacheMode, DeviceInformation = runtime_modules()
    devices = await list_paired(BluetoothLEDevice, DeviceInformation)
    print(f"Paired BLE devices visible to WinRT: {len(devices)}")
    for i, candidate in enumerate(devices):
        print(f"  [{i}] {candidate.name}")
    if not devices:
        print("No paired BLE devices reported. Check Windows Bluetooth Settings.")
        return 1
    if index is None:
        print("Select a device with: py -3 tools\\windows-probe\\gatt_probe.py --index N")
        return 0
    if index < 0 or index >= len(devices):
        print("Invalid index; run without --index to list paired devices.", file=sys.stderr)
        return 2
    if scan_before:
        # Scan completes before GATT discovery; do not leave a competing
        # scanner running while requesting a connection to the paired device.
        from adv_probe import scan

        print("Pre-connection advertisement scan (not device identity proof):")
        scan_result = await scan(scan_before)
        if scan_result == 2:
            print("Advertisement scan failed; continuing independent GATT diagnosis.")

    for attempt in range(1, attempts + 1):
        evidence = AttemptResult()
        print(f"GATT connection attempt {attempt}/{attempts}")
        result = await probe_one(devices[index], BluetoothLEDevice, BluetoothCacheMode,
                                 compare_hid=compare_hid, attempt_result=evidence)
        # Do not retry ACCESS_DENIED or other statuses as if they were
        # connectivity errors. Each attempt uses a new WinRT device object.
        if evidence.discovery_status != "UNREACHABLE" or attempt == attempts:
            return result
        print("Remote unreachable; next attempt in 5 seconds. "
              "Keep it close and tap a button to wake it.")
        await asyncio.sleep(5)
    return 2


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--index", type=int, default=None,
                        help="Index of a paired BLE device shown by the list command")
    parser.add_argument("--hid-snapshot", action="store_true",
                        help="Compare read-only HID interface metadata during GATT attempt")
    parser.add_argument("--attempts", type=int, default=1,
                        help="Up to 3 GATT attempts; retry only UNREACHABLE (default 1)")
    parser.add_argument("--scan-before", type=int, default=0,
                        help="Optional pre-connection BLE scan seconds (5..30)")
    args = parser.parse_args()
    if not 1 <= args.attempts <= 3:
        parser.error("--attempts must be in 1..3")
    if args.scan_before != 0 and not 5 <= args.scan_before <= 30:
        parser.error("--scan-before must be 5..30 (or omitted)")
    try:
        return asyncio.run(run(args.index, compare_hid=args.hid_snapshot,
                               attempts=args.attempts, scan_before=args.scan_before))
    except (OSError, RuntimeError, TimeoutError) as exc:
        print(f"WinRT GATT diagnostic failed: {type(exc).__name__}: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
