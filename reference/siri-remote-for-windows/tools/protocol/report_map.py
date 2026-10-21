#!/usr/bin/env python3
"""Read-only Linux BlueZ extraction and offline inspection of HID Report Map 0x2A4B.

No BLE writes, pairing changes, driver changes, or privilege escalation.
By default live mode requires an already connected/resolved remote.
--connect makes a bounded connection attempt against one existing bonded
remote and reads its HID Report Map in the same D-Bus session.
Offline mode (--inspect) works on Windows and Linux using stdlib only.
"""

from __future__ import annotations

import argparse
import asyncio
from collections import Counter
import hashlib
from pathlib import Path
import sys

DEVICE = "org.bluez.Device1"
SERVICE = "org.bluez.GattService1"
CHARACTERISTIC = "org.bluez.GattCharacteristic1"
HID_SERVICE = "00001812-0000-1000-8000-00805f9b34fb"
REPORT_MAP = "00002a4b-0000-1000-8000-00805f9b34fb"


def value(obj):
    """Unwrap dbus-next Variants, compatible with ordinary fixture objects."""
    return getattr(obj, "value", obj)


def find_map_paths(objects: dict, name_prefix: str = "") -> list[str]:
    """Find map characteristics below CONNECTED + resolved HID devices only.

    Return private BlueZ object paths internally; caller must never print them.
    """
    matches = []
    prefix = name_prefix.casefold()
    devices = []
    for path, ifaces in objects.items():
        props = ifaces.get(DEVICE)
        if not props:
            continue
        name = str(value(props.get("Name", "")))
        if prefix and not name.casefold().startswith(prefix):
            continue
        if value(props.get("Connected", False)) is not True:
            continue
        if value(props.get("ServicesResolved", False)) is not True:
            continue
        devices.append(str(path))
    for dev in devices:
        for service_path, ifaces in objects.items():
            if not str(service_path).startswith(dev + "/"):
                continue
            svc = ifaces.get(SERVICE)
            if not svc or str(value(svc.get("UUID", ""))).lower() != HID_SERVICE:
                continue
            for char_path, child in objects.items():
                if not str(char_path).startswith(str(service_path) + "/"):
                    continue
                char = child.get(CHARACTERISTIC)
                if char and str(value(char.get("UUID", ""))).lower() == REPORT_MAP:
                    matches.append(str(char_path))
    return sorted(set(matches))


def find_bonded_device_paths(objects: dict, name_prefix: str) -> list[str]:
    """Select paired BlueZ devices by private friendly-name prefix."""
    if not name_prefix:
        return []
    paths = []
    for path, ifaces in objects.items():
        props = ifaces.get(DEVICE)
        if not props:
            continue
        if not str(value(props.get("Name", ""))).casefold().startswith(
                name_prefix.casefold()):
            continue
        if value(props.get("Paired", False)) is True:
            paths.append(str(path))
    return sorted(paths)


def remote_gatt_state(objects: dict, path: str) -> dict:
    """Summarize only public protocol states of one PRIVATE BlueZ device path.

    Never return/print the device path, address, serial, service object paths or
    characteristic object paths. Works even when services have not resolved.
    """
    props = objects.get(path, {}).get(DEVICE, {})
    hid = [
        str(service_path)
        for service_path, ifaces in objects.items()
        if str(service_path).startswith(path + "/")
        and str(value(ifaces.get(SERVICE, {}).get("UUID", ""))).lower()
        == HID_SERVICE
    ]
    report_chars = 0
    map_chars = 0
    for char_path, ifaces in objects.items():
        if not any(str(char_path).startswith(service + "/") for service in hid):
            continue
        char = ifaces.get(CHARACTERISTIC)
        if not char:
            continue
        uuid = str(value(char.get("UUID", ""))).lower()
        if uuid == REPORT_MAP:
            map_chars += 1
        elif uuid == "00002a4d-0000-1000-8000-00805f9b34fb":
            report_chars += 1
    return {
        "connected": value(props.get("Connected", False)) is True,
        "resolved": value(props.get("ServicesResolved", False)) is True,
        "hid_services": len(hid),
        "map_chars": map_chars,
        "report_chars": report_chars,
    }


def safe_bluez_error_name(reply) -> str:
    """Emit recognized generic D-Bus errors only (never error messages)."""
    name = str(getattr(reply, "error_name", "") or "")
    if name.startswith("org.bluez.Error."):
        suffix = name[len("org.bluez.Error."):]
        if suffix in {
            "Failed", "NotReady", "InProgress", "AlreadyConnected",
            "NotConnected", "NotAvailable", "NotSupported", "NotAuthorized",
            "NotPermitted", "AuthenticationFailed", "AuthenticationCanceled",
            "AuthenticationRejected", "AuthenticationTimeout",
            "ConnectionAttemptFailed", "DoesNotExist",
        }:
            return suffix
    if name == "org.freedesktop.DBus.Error.NoReply":
        return "NoReply"
    return "other-error"


def inspect_map(raw: bytes) -> dict:
    """Cheap HID item boundary/collection checks, NOT a full Windows HID parser."""
    i = 0
    collections = 0
    maximum_depth = 0
    count = Counter()
    report_ids = set()
    errors = []
    while i < len(raw):
        offset = i
        prefix = raw[i]
        i += 1
        if prefix == 0xFE:
            if i + 2 > len(raw):
                errors.append(f"Truncated long-item header at offset {offset}")
                break
            data_len = raw[i]
            i += 2  # item size and long-item tag
            if i + data_len > len(raw):
                errors.append(f"Truncated long-item payload at offset {offset}")
                break
            i += data_len
            count["long"] += 1
            continue
        size = (0, 1, 2, 4)[prefix & 0x03]
        if i + size > len(raw):
            errors.append(f"Truncated item payload at offset {offset}")
            break
        data = raw[i:i + size]
        i += size
        item_type = (prefix >> 2) & 0x03
        tag = (prefix >> 4) & 0x0F
        count[(item_type, tag)] += 1
        if item_type == 0 and tag == 10:  # Collection
            collections += 1
            maximum_depth = max(maximum_depth, collections)
        elif item_type == 0 and tag == 12:  # End Collection
            collections -= 1
            if collections < 0:
                errors.append(f"Unexpected End Collection at offset {offset}")
                collections = 0
        elif item_type == 1 and tag == 8:  # Report ID
            report_id = int.from_bytes(data, "little") if data else 0
            if report_id == 0:
                errors.append(f"Invalid Report ID 0 at offset {offset}")
            elif report_id > 255:
                errors.append(f"Out-of-range Report ID at offset {offset}")
            else:
                report_ids.add(report_id)
    if collections != 0:
        errors.append(f"Unclosed Collections: {collections}")
    if not raw:
        errors.append("Empty Report Map")
    return {
        "bytes": len(raw),
        "sha256": hashlib.sha256(raw).hexdigest(),
        "report_ids": sorted(report_ids),
        "maximum_collection_depth": maximum_depth,
        "items": sum(count.values()),
        "warnings": errors,
    }


def print_analysis(raw: bytes) -> None:
    summary = inspect_map(raw)
    print(f"Report Map length: {summary['bytes']} bytes")
    print(f"SHA-256: {summary['sha256']}")
    print("Report IDs declared: " +
          (", ".join(f"0x{v:02X}" for v in summary["report_ids"]) or "(none)"))
    print(f"Maximum collection depth: {summary['maximum_collection_depth']}")
    print(f"HID item count: {summary['items']}")
    if summary["warnings"]:
        for msg in summary["warnings"]:
            print("Structural warning: " + msg)
    else:
        print("No BASIC item-boundary/collection problems found.")
    print("This inspector does not implement HidP_GetCollectionDescription; "
          "clean output does NOT prove Windows HID compatibility.")


async def read_connected_map(
    name_prefix: str, connect: bool = False, scan_before: float = 0.0,
    observe_seconds: float = 19.0, attempts: int = 3
) -> bytes:
    if sys.platform != "linux":
        raise RuntimeError("Live BlueZ read works only on Linux. Use --inspect on Windows.")
    try:
        from dbus_next.aio import MessageBus
        from dbus_next import BusType, Message, MessageType
    except ImportError as exc:
        raise RuntimeError("Missing dependency: python3 -m pip install dbus-next") from exc
    bus = await MessageBus(bus_type=BusType.SYSTEM).connect()

    async def snapshot():
        reply = await bus.call(Message(
            destination="org.bluez", path="/",
            interface="org.freedesktop.DBus.ObjectManager",
            member="GetManagedObjects"))
        if reply.message_type != MessageType.METHOD_RETURN or not reply.body:
            raise RuntimeError("BlueZ ObjectManager query failed")
        return reply.body[0]

    async def try_read(objects, diagnostics=None):
        paths = find_map_paths(objects, name_prefix)
        if len(paths) != 1:
            return None
        if diagnostics is not None:
            diagnostics["read_attempts"] += 1
        try:
            reply = await asyncio.wait_for(bus.call(Message(
                destination="org.bluez", path=paths[0],
                interface=CHARACTERISTIC, member="ReadValue",
                signature="a{sv}", body=[{}])), timeout=8)
        except asyncio.TimeoutError:
            if diagnostics is not None:
                diagnostics["read_status"] = "timeout"
            return None
        if reply.message_type != MessageType.METHOD_RETURN:
            if diagnostics is not None:
                diagnostics["read_status"] = safe_bluez_error_name(reply)
            return None
        if not reply.body or not bytes(reply.body[0]):
            if diagnostics is not None:
                diagnostics["read_status"] = "empty"
            return None
        return bytes(reply.body[0])

    async def scan_for_current_advertisement(target, seconds):
        """Temporarily use LE scan; never print nearby BLE identifiers.

        A cached RSSI value is not proof of a *new* advertisement, so we
        observe target-specific RSSI PropertiesChanged signals instead.
        """
        if seconds <= 0:
            return
        adapter = target.rsplit("/dev_", 1)[0]
        match_rule = (
            "type='signal',interface='org.freedesktop.DBus.Properties',"
            "member='PropertiesChanged',path='" + target + "'"
        )
        sightings = 0

        def handler(message):
            nonlocal sightings
            if (message.message_type == MessageType.SIGNAL
                    and message.path == target
                    and message.interface == "org.freedesktop.DBus.Properties"
                    and message.member == "PropertiesChanged"
                    and len(message.body) >= 2
                    and message.body[0] == DEVICE
                    and "RSSI" in message.body[1]):
                sightings += 1
            return False

        bus.add_message_handler(handler)
        subscribed = False
        scanning = False
        try:
            add_reply = await bus.call(Message(
                destination="org.freedesktop.DBus",
                path="/org/freedesktop/DBus",
                interface="org.freedesktop.DBus",
                member="AddMatch", signature="s", body=[match_rule]))
            if add_reply.message_type == MessageType.METHOD_RETURN:
                subscribed = True
            scan_reply = await bus.call(Message(
                destination="org.bluez", path=adapter,
                interface="org.bluez.Adapter1", member="StartDiscovery"))
            if scan_reply.message_type == MessageType.METHOD_RETURN:
                scanning = True
                print(f"LE discovery session: started for {seconds:g}s.")
                await asyncio.sleep(seconds)
            else:
                print(f"Discovery start: {safe_bluez_error_name(scan_reply)}")
        finally:
            if scanning:
                try:
                    await bus.call(Message(
                        destination="org.bluez", path=adapter,
                        interface="org.bluez.Adapter1",
                        member="StopDiscovery"))
                except Exception:
                    pass
            if subscribed:
                try:
                    await bus.call(Message(
                        destination="org.freedesktop.DBus",
                        path="/org/freedesktop/DBus",
                        interface="org.freedesktop.DBus",
                        member="RemoveMatch", signature="s", body=[match_rule]))
                except Exception:
                    pass
            bus.remove_message_handler(handler)
            print(
                "Fresh RSSI updates for the *previously paired remote* "
                f"during scan: {sightings}"
            )
            print(
                "Zero updates is inconclusive: BlueZ can suppress duplicate "
                "advertisements or use a different device identity."
            )

    try:
        objects = await snapshot()
        result = await try_read(objects)
        if result is not None:
            return result
        if not connect:
            raise RuntimeError(
                "Remote not connected/resolved or Report Map read unavailable; "
                "try --connect --name-prefix DL1 on the existing bonded remote.")
        candidates = find_bonded_device_paths(objects, name_prefix)
        print(f"Paired remote candidates matching name prefix: {len(candidates)}")
        if len(candidates) != 1:
            raise RuntimeError("A unique previously paired remote is required")
        target = candidates[0]
        # Explicit opt-in: existing bond only, no pairing or driver changes.
        # BlueZ LE connections generally need a recent active advertisement.
        if scan_before > 0:
            await scan_for_current_advertisement(target, scan_before)
        for attempt in range(1, attempts + 1):
            print(f"BLE connection/read attempt {attempt}/{attempts}.")
            diag = {"read_attempts": 0, "read_status": "not-attempted"}
            ever = {
                "connected": False, "resolved": False,
                "hid_services": 0, "map_chars": 0, "report_chars": 0
            }
            samples = {"total": 0, "connected": 0, "resolved": 0}
            last_state = {"connected": False, "resolved": False}
            # Do not WAIT for Connect() to complete before watching GATT:
            # previously, a transient connection could appear and disappear
            # during the 15s wait and be missed entirely by the snapshots.
            loop = asyncio.get_running_loop()
            started = loop.time()
            connect_task = asyncio.create_task(bus.call(Message(
                destination="org.bluez", path=target,
                interface=DEVICE, member="Connect")))
            connect_status = "pending"
            while loop.time() - started < observe_seconds:
                objects = await snapshot()
                current = remote_gatt_state(objects, target)
                samples["total"] += 1
                samples["connected"] += int(current["connected"])
                samples["resolved"] += int(current["resolved"])
                last_state = current
                ever["connected"] |= current["connected"]
                ever["resolved"] |= current["resolved"]
                for name in ("hid_services", "map_chars", "report_chars"):
                    ever[name] = max(ever[name], current[name])
                result = await try_read(objects, diagnostics=diag)
                if result is not None:
                    print(f"Attempt {attempt}: Report Map read success.")
                    return result
                if connect_task.done():
                    try:
                        response = connect_task.result()
                        connect_status = (
                            "success" if response.message_type
                            == MessageType.METHOD_RETURN
                            else safe_bluez_error_name(response)
                        )
                    except Exception:
                        connect_status = "request-error"
                    if connect_status not in ("success", "AlreadyConnected"):
                        # Give services a small post-failure observation window.
                        if loop.time() - started >= 4.0:
                            break
                await asyncio.sleep(0.20)
            pending = not connect_task.done()
            if pending:
                connect_task.cancel()
                # A cancelled D-Bus await does not necessarily cancel the
                # underlying BlueZ connection attempt. Avoid stacking retries
                # on top of an ongoing operation.
                connect_status = "pending-after-observation"
            print(
                f"Attempt {attempt} diagnostic: Connect={connect_status}; "
                f"seen Connected={ever['connected']}, "
                f"ServicesResolved={ever['resolved']}; "
                f"connected samples={samples['connected']}/{samples['total']}, "
                f"final Connected={last_state['connected']}; "
                f"HID service count={ever['hid_services']}, "
                f"Report Map 0x2A4B count={ever['map_chars']}, "
                f"Report characteristic 0x2A4D count={ever['report_chars']}; "
                f"map reads={diag['read_attempts']}, "
                f"last read status={diag['read_status']}"
            )
            if pending:
                print("Connect still pending; not issuing overlapping retries.")
                break
            if attempt < attempts:
                await asyncio.sleep(1)
        raise RuntimeError("Report Map was not readable in bounded attempts")
    finally:
        bus.disconnect()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    selection = parser.add_mutually_exclusive_group(required=True)
    selection.add_argument("--inspect", type=Path,
                           help="Offline inspection of an existing raw .bin Report Map")
    selection.add_argument("--output", type=Path,
                           help="Read an already-connected Linux BlueZ remote and save binary")
    parser.add_argument("--name-prefix", default="",
                        help="Optional device name prefix; never printed")
    parser.add_argument("--connect", action="store_true",
                        help="Linux only: connect one already paired remote (max 3 tries)")
    parser.add_argument("--scan-before", type=float, default=0.0,
                        metavar="SECONDS",
                        help="Linux only: BLE discovery before connection (1 to 20s)")
    parser.add_argument("--observe-seconds", type=float, default=19.0,
                        metavar="SECONDS",
                        help="Linux: observe connection/service resolution (8 to 90s)")
    parser.add_argument("--attempts", type=int, default=3,
                        help="Linux: bounded connection attempts (1 to 3)")
    args = parser.parse_args()
    if args.connect and (args.output is None or not args.name_prefix):
        parser.error("--connect requires --output and a unique --name-prefix")
    if args.scan_before and (not args.connect or not 1 <= args.scan_before <= 20):
        parser.error("--scan-before requires --connect and duration of 1-20 seconds")
    if not (8 <= args.observe_seconds <= 90) or not (1 <= args.attempts <= 3):
        parser.error("--observe-seconds must be 8-90 and --attempts must be 1-3")
    if not args.connect and (args.observe_seconds != 19 or args.attempts != 3):
        parser.error("--observe-seconds/--attempts require --connect")
    try:
        if args.inspect is not None:
            raw = args.inspect.read_bytes()
        else:
            raw = asyncio.run(read_connected_map(
                args.name_prefix, connect=args.connect,
                scan_before=args.scan_before,
                observe_seconds=args.observe_seconds,
                attempts=args.attempts))
            # Exclusive create prevents accidentally overwriting other captures.
            with args.output.open("xb") as out:
                out.write(raw)
            print("Saved Report Map to specified local file (no device identifiers printed).")
        print_analysis(raw)
        return 0
    except (OSError, RuntimeError, ValueError, asyncio.TimeoutError) as exc:
        # Avoid printing exception payloads from BlueZ that might contain addresses.
        # Our own fixed RuntimeError strings are safe, but arbitrary D-Bus
        # exception messages may contain private hardware identifiers.
        if isinstance(exc, RuntimeError) and str(exc) in {
            "Report Map was not readable in bounded attempts",
            "A unique previously paired remote is required",
            "BlueZ ObjectManager query failed",
        }:
            print("Cannot capture/inspect Report Map: " + str(exc), file=sys.stderr)
        else:
            print("Cannot capture/inspect Report Map: " + type(exc).__name__, file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
