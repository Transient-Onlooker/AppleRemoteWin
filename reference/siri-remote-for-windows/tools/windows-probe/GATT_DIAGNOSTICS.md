# Paired Siri Remote: read-only Windows GATT investigation

The Windows Bluetooth Settings page can show a device as paired/disconnected even when a client has not requested an active BLE connection. During the 2026-10-17 trial, the A2854 showed a temporary connection and a battery percentage, then disconnected; the earlier HID interface probe remained unchanged at 20 interfaces. The device's Windows display name was reported as beginning with "DL1" (exact suffix not recorded). These findings do **not** yet confirm working Windows HID input.

This program uses **WinRT directly** to list paired BLE devices, request **uncached** GATT discovery on one selected device, and report whether HID 0x1812 is visible and whether its Report 0x2A4D characteristics are accessible. It never pairs/unpairs, installs drivers, writes HID feature/output reports, enables notifications, captures microphone data, or synthesizes input.

## Run in PowerShell (repository root)

```powershell
git pull --ff-only
py -3 -m pip install "bleak>=2,<4"
py -3 tools\windows-probe\gatt_probe.py
```

Look for a device named `DL1…` or another likely A2854 name among the **paired BLE devices**. Use its numbered index, for example:

```powershell
py -3 tools\windows-probe\gatt_probe.py --index 0
```

The `0` here is **only an example**. Choose the printed index for the remote, not another person's device. The script suppresses BLE addresses and unique device IDs by design. Keep the remote nearby and try tapping Back or another button while it runs. A normal BLE connection may close again after the script finishes.

If the paired WinRT enumeration does not show the remote, also list disconnected Windows Bluetooth PnP entries:

```powershell
Get-PnpDevice -Class Bluetooth |
    Where-Object { $_.FriendlyName -match 'DL1|Siri|Apple|Remote|Bluetooth LE' } |
    Select-Object Status, Class, FriendlyName
```

Avoid posting raw Windows `InstanceId` fields publicly because some encode unique Bluetooth addresses.

## Confirmed 2026-10-17 device result

The paired A2854 was listed by WinRT, but uncached GATT discovery returned `UNREACHABLE` and `DISCONNECTED`. This means no usable GATT session was established for the attempted discovery, **not** that HID 0x1812 access is denied. Before changing pairing or driver settings, run the [read-only advertisement scan](ADVERTISEMENT_DIAGNOSTICS.md) with wake-button timing.

The first iteration of this script printed `device.name` which may contain the Bluetooth address. The corrected version no longer prints that WinRT field. Avoid sharing previous unredacted logs externally.

## Follow-up experiment: connected GATT with denied HID characteristics

A subsequent Windows test detected an HID UUID 0x1812 advertisement among nearby devices and successfully opened the paired A2854 through WinRT. The returned service list had **7 services** (GAP, GATT, Device Information, one vendor service, Bond Management, Battery, HID). The GATT service discovery status was `SUCCESS`, connection status became `CONNECTED`, HID service `0x1812` was exposed, and characteristic discovery returned `ACCESS_DENIED`.

**Interpretation:** This strongly suggests an OS/user-mode API restriction on the HID service, but does not by itself establish that Windows HIDClass generated a usable interface for A2854. Windows includes a built-in HID-over-GATT transport, and HID input collections can be opened exclusively by system components. Do not attempt driver-protection or security-policy bypasses.

### Connecting reliably before interpreting HID counts

The earlier UNREACHABLE result was followed by a successful GATT session after an advertisement scan. This sequence is an observation, **not proof** that scanning caused success. A [2025 Microsoft Q&A report](https://learn.microsoft.com/en-us/answers/questions/2280559/retry-required-for-getgattservicesasync%28%29-when-con) describes similar failures with BLE private addresses, but the A2854 cause remains unverified.

The optional pre-scan finishes before GATT discovery, and --attempts allows at most three discovery tries, five seconds apart. The tool retries **only UNREACHABLE**, not successful or denied HID access, and reopens the Windows device object on each attempt.

To test whether HID interfaces become visible during an actual connection, from repository root:

```powershell
git pull --ff-only
py -3 tools\windows-probe\gatt_probe.py --index 0 --hid-snapshot --scan-before 10 --attempts 3
```

If all three attempts show UNREACHABLE, stop and keep the logs. Check the paired device list before assuming the remote is still index 0. The new option prints only counts and coarse VID/PID/usage identifiers, not device paths or Bluetooth addresses. **No new HID interface signature** means none became visible during that capture, not proof that raw BLE is inaccessible by every conceivable API. An additional interface cannot be conclusively attributed to A2854 without correlation.

Microsoft reference: [HID Transport Overview](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/hid-transports), [HID Architecture](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/hid-architecture).

## 2026-10-17 follow-up: connected, but still no newly visible HID interface

With `--hid-snapshot --scan-before 10 --attempts 3`, the first GATT attempt was `UNREACHABLE`, then the second connected and returned seven services, including HID 0x1812; HID characteristic discovery returned `ACCESS_DENIED`. The count of Windows HID interfaces remained **20 before and 20 after** on both attempts. This is stronger evidence that ordinary user-mode HID/GATT access is not immediately available, but cannot conclusively show whether Windows bound a system HID driver, whether an interface already existed, or whether a hidden device node has an error state.

Next run the [read-only PnP driver inventory](PNP_DIAGNOSTICS.md); avoid assuming that GATT denial necessarily means a functioning HIDClass input interface exists.

## Interpret carefully

- **A paired record present**: Windows has pairing metadata, not proof of active GATT connectivity.
- **Uncached GATT discovery success**: Windows obtained a service list in this attempt, indicating a working GATT operation.
- **HID 0x1812 absent**: Windows may own/hide it or service discovery is incomplete. Does not prove the device lacks HID; Linux already observed it.
- **HID 0x1812 present, characteristics AccessDenied**: Windows has denied these GATT characteristic operations to this process; a normal Windows GATT client may not be able to receive HID reports.
- **HID Report 0x2A4D visible**: This is promising, but **actual** 0xFB/0xFC notifications and required 0xAF enable writes still need a separate, explicitly approved test.
- **GATT errors/timeouts**: Record the exact status/error and Windows version; do not automatically delete pairing or change drivers.

Microsoft documents that uncached GATT discovery can initiate a BLE connection, and that a BluetoothLEDevice object alone may not. Bleak's WinRT backend may explicitly skip HID characteristics after AccessDenied, so this tool uses lower-level WinRT to preserve the denied status rather than silently omitting it.

References:
- [Microsoft: Bluetooth GATT client](https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/gatt-client)
- [Microsoft: BluetoothLEDevice API](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothledevice)
- [Bleak WinRT implementation](https://bleak.readthedocs.io/en/latest/_modules/bleak/backends/winrt/client.html)

## Validation boundaries

The program requires native Windows, Python 3.9+, and the `bleak` dependency (which brings PyWinRT modules). Successful Windows CI imports or unit tests do not prove that this specific A2854 can be read. If WinRT fails while Windows Bluetooth briefly shows a battery reading, preserve both observations; they are not necessarily contradictory.
