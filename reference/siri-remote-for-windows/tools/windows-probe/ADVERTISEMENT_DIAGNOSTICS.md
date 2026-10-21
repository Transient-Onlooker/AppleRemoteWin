# BLE advertisement visibility: A2854 Windows connection diagnostic

**2026-10-17 observed, native Windows:** `DL1…` is in the paired WinRT LE list; `BluetoothLEDevice.from_id_async` returned an object, but its initial status was `DISCONNECTED`. An uncached `get_gatt_services_with_cache_mode_async` request returned `UNREACHABLE` and the status remained `DISCONNECTED`. **This is a failed link/service discovery attempt, NOT a test of HID 0x1812 access.**

Microsoft's [GATT client guidance](https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/gatt-client) says an uncached discovery can initiate a connection; it will fail with unreachable if the peripheral cannot be reached. Causes may include sleeping/not advertising, a stale bond, radio conflicts, or device-specific reconnect requirements. None is proven yet.

## Read-only advertisement scan

In PowerShell, from the repository root:

```powershell
git pull --ff-only
py -3 tools\windows-probe\adv_probe.py --seconds 20
```

- Position the remote very close to the Intel AX200 PC.
- During the scan, try tapping Back/TV or touching the clickpad to wake the remote.
- If nothing is observed, retry once while holding **Back + Volume Up for ~5 seconds** to trigger the remote's pairing/advertising mode. This is not the same as deleting or replacing the existing Windows pairing. Do not select/remove a Windows device as part of this test.
- The scanner is separate from the GATT probe. **Run it sequentially**, not simultaneously.

The program listens using Bleak's Windows WinRT backend. It prints only coarse markers (remote-like local name, HID service UUID 0x1812, Apple company identifier). It **does not** output MAC addresses, unique device IDs, manufacturer bytes, raw audio, or even the remote's complete name. `Apple manufacturer` alone is insufficient to identify this remote; many nearby Apple devices advertise.

A matching advertisement suggests a candidate BLE broadcaster is visible; it **does not** establish that the paired bond address is still valid or that Windows can connect to it. No matching advertisement does **not** prove the remote is not transmitting (Windows scan filtering, sleep, timing, rotating address, or other RF conditions can mask it).

## Recorded result (2026-10-17)

A 20-second Windows scan reported **204 matching advertisement events** from **7 nearby identities**, at least one with Apple manufacturer data and HID service UUID 0x1812. Because the scan intentionally redacts addresses and identifiers, these sightings are **not uniquely proven** to come from the A2854. Immediately afterward, a WinRT GATT operation against the paired A2854 succeeded and exposed HID service 0x1812, though its characteristics were denied.

## After scanning

Do not delete the pairing yet. With the remote awake and nearby:

```powershell
py -3 tools\windows-probe\gatt_probe.py --index 0
```

Use index `0` **only if** the paired WinRT device list still shows the remote at index 0. Otherwise first enumerate without `--index` and choose the correct index.

If still `UNREACHABLE`, preserve the exact status plus whether the scan saw a **remote-like name**, **HID UUID**, or **Apple-only** candidate. Then investigate advertisement versus bond identity (without posting identifiers) and rule out other devices currently connected to the remote. Avoid guessing a HID permission failure before any GATT connection succeeds.

The script depends on Bleak (`py -3 -m pip install "bleak>=2,<4"` if missing). Unit tests and CI validate code/interface behavior, **not** radio reception on the actual Siri Remote.
