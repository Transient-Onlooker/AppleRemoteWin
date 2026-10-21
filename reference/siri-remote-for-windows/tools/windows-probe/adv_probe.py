#!/usr/bin/env python3
"""Read-only BLE advertisement visibility check for a paired Siri Remote.

Uses Windows' native BLE scanner through Bleak. Does not connect, pair, write,
subscribe, record audio, or log BLE addresses/device identifiers.
"""

from __future__ import annotations

import argparse
import asyncio
import sys

HID_UUID = "00001812-0000-1000-8000-00805f9b34fb"
APPLE_COMPANY_ID = 0x004C


def classify_advertisement(adv: object) -> tuple[str, ...]:
    """Return coarse markers only; never return names, IDs, payload, or address."""
    name = (getattr(adv, "local_name", None) or "").lower()
    services = {str(u).lower() for u in (getattr(adv, "service_uuids", ()) or ())}
    manufacturers = getattr(adv, "manufacturer_data", {}) or {}
    result = []
    if name.startswith("dl1") or "siri remote" in name:
        result.append("remote-like name")
    if HID_UUID in services or "1812" in services:
        result.append("HID service 0x1812 advertised")
    if APPLE_COMPANY_ID in manufacturers:
        result.append("Apple manufacturer (not necessarily remote)")
    return tuple(result)


async def scan(seconds: int) -> int:
    if sys.platform != "win32":
        print("Run on native Windows, not WSL/Linux.", file=sys.stderr)
        return 2
    try:
        from bleak import BleakScanner
    except ImportError:
        print('Install scanner dependency: py -3 -m pip install "bleak>=2,<4"',
              file=sys.stderr)
        return 2

    # Address is only an in-memory deduplication key. Never emit it.
    observed: dict[str, set[str]] = {}
    matching_events = 0

    def on_advertisement(device, advertisement):
        nonlocal matching_events
        markers = set(classify_advertisement(advertisement))
        if not markers:
            return
        matching_events += 1
        previous = observed.setdefault(device.address, set())
        newly_seen = markers - previous
        previous.update(markers)
        if newly_seen:
            print("Observed: " + "; ".join(sorted(newly_seen)), flush=True)

    print(f"Scanning BLE advertisements for {seconds} seconds...")
    print("Bring the remote close; tap Back/TV or touch the pad during scanning.")
    print("No addresses, manufacturer payloads, or microphone packets are printed.")
    try:
        async with BleakScanner(detection_callback=on_advertisement):
            await asyncio.sleep(seconds)
    except Exception as exc:
        print(f"Scanner error: {type(exc).__name__}: {exc}", file=sys.stderr)
        return 2

    print(f"Matching advertisement events: {matching_events}")
    print(f"Matched nearby BLE identities (unverified): {len(observed)}")
    if not observed:
        print("No matching advertisements observed. The remote may be sleeping, "
              "not advertising, filtered by Windows, or out of range.")
        print("This alone does NOT prove BLE/GATT incompatibility.")
        return 1
    if not any("remote-like name" in tags or
               "HID service 0x1812 advertised" in tags for tags in observed.values()):
        print("Only Apple manufacturer advertisements were seen; these may belong "
              "to unrelated nearby Apple devices.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--seconds", type=int, default=20,
                        help="Scan time in seconds (5..120, default 20)")
    args = parser.parse_args()
    if not 5 <= args.seconds <= 120:
        parser.error("--seconds must be in 5..120")
    return asyncio.run(scan(args.seconds))


if __name__ == "__main__":
    raise SystemExit(main())
