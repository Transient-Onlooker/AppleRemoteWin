# Siri Remote A2854 (third generation) — development branch

> **Full handoff (Korean, 2026-10-18):** [HANDOFF_2026-10-18_A2854.md](HANDOFF_2026-10-18_A2854.md). Read that document first when resuming after a conversation reset; the notes below are the chronological engineering log.


This feature branch explores A2854 support without altering the original
first-generation Siri Remote path. **No native Windows A2854 hardware success
has been claimed.** As of 2026-10-18, the owner's physical A2854 was decoded
on Linux, while stock Windows WinRT HID-GATT enumeration returned
`ACCESS_DENIED` at service `0x1812`.

## Changes

- `src/AppleRemote.Core/A2854Decoder.cs`: independent 3rd-gen input parser.
- `tests/AppleRemote.Tests/A2854DecoderTests.cs`: hardware-observed
  `0xFB` button / `0xFC` clickpad samples, malformed packets, link-drop releases,
  and first-gen regression.
- `SiriRemoteBle`: optional allowlist for input Report Reference IDs.
  Without it, the original 1st-gen behavior is unchanged.
- `tools/A2854.Probe`: minimal *native Windows* console connecting through the
  existing **direct USB Bluetooth HCI over WinUSB** transport. It subscribes
  only to `0xFB` and `0xFC`, never to `0xFA` microphone reports.
  The existing GATT code discovers duplicate `0x2A4D` characteristic
  identities via the `0x2908` descriptor, writes CCCDs only for selected
  input reports, and writes `0xAF` to the discovered `0xF0` feature report.

## Compile and run (Windows PowerShell)

Prerequisites: .NET 10 SDK and **a separate, USB Bluetooth adapter already
assigned WinUSB**, configured using a documented restore path. This project
does *not* install or change device drivers. **Do not replace the laptop's
built-in Intel AX200 Windows Bluetooth driver.**

```powershell
cd "C:\Users\무조림.000\Desktop\코딩\01_working"
git clone https://github.com/Transient-Onlooker/AppleRemoteWin.git
cd AppleRemoteWin
git switch feat/a2854-support

dotnet test tests/AppleRemote.Tests -c Release
dotnet build tools/A2854.Probe/A2854.Probe.csproj -c Release
dotnet run --project tools/A2854.Probe/A2854.Probe.csproj -- --seconds 30
```

**Important:** If that folder already exists, `git clone` fails; use a new
directory or `git fetch origin` and `git switch feat/a2854-support`.

For first pairing with the dedicated adapter, put the remote in pairing mode
(Back + Volume Up for ~5 s); the Windows OS pairing for AX200 does not
transfer to a different controller. Saved bond keys for this *experimental*
program are kept separately in
`%APPDATA%\AppleRemote\a2854-probe-bonds.json`, not the gen-1 app's
`bonds.json`. Treat this key file as private.

The console intentionally does **not** inject mouse or keyboard events,
change system Bluetooth settings, install drivers, record microphone data,
or expose BLE MAC addresses in log output. Only the buttons and first 80
touch frames are printed, with a final count.

### Tested versus untested

- Hardware-trace samples: A2854 button masks are two little-endian bytes on
  input Report ID `0xFB`; touch Report ID `0xFC` carries the `0x32` marker
  with 11- or 18-byte payloads (excluding report ID). These were observed
  with Linux BlueZ on the owner's physical device.
- Existing Windows built-in AX200 / WinRT: HID `0x1812` found, report
  characteristic enumeration `ACCESS_DENIED`; this new program **does not**
  use WinRT GATT.
- New direct-HCI path: **not yet hardware-verified with A2854**. The
  first-generation app's `SmpPairing` assumes legacy \"Just Works\" pairing,
  and its advertisement fingerprint requires both HID `0x1812` and Apple
  manufacturer data `0x004C` in one advertisement. A2854 might require
  different discovery, pairing, MTU, unlock, and connection handling.
- The WPF application continues using `Decoder` (first generation). The
  A2854 parser is used by this diagnostic tool only until real connectivity
  and firmware identity can be proven.

## A2854 event reference (owner data)

- `FB 00 04` → Right pressed; `FB 00 00` → Right released
- `FB 01 00` → TV; `FB 40 00` → Back
- `FC 32 14 FC 01 C9 9E FB 51 44 01 66` →
  x=-311, y=-71, hover=true, pressure=1 (not necessarily contact)
- `FC 32 36 FC 00 C9 9E FB 00 00 00 67` → release
- `FC 32 92 D6 01 66 DC D5 60 1C 09 AC` →
  x=-922, y=-675, hover=false

Measurements and provenance:
[hardware sample fixtures](https://github.com/Transient-Onlooker/siri-remote-for-windows/blob/main/docs/windows-port/OBSERVED_REPORT_FIXTURES.md),
[protocol findings](https://github.com/Transient-Onlooker/siri-remote-for-windows/blob/main/docs/windows-port/PROTOCOL_FINDINGS.md).

## 2026-10-18 Windows 11 owner-run build and test validation

Owner ran these commands in Windows PowerShell on a physical laptop after
installing Microsoft .NET SDK 10.0.402:

```text
dotnet --list-sdks
10.0.402 [C:\\Program Files\\dotnet\\sdk]
dotnet test tests/AppleRemote.Tests -c Release
Total: 89, Failed: 0, Passed: 89, Skipped: 0
Build succeeded.
dotnet build tools/A2854.Probe/A2854.Probe.csproj -c Release
AppleRemote.Core succeeded.
AppleRemote.Windows succeeded.
A2854.Probe succeeded.
Build succeeded.
```

These are **real Windows compile and unit-test results** provided by the
owner, not simulated CI. The direct BLE adapter route and A2854 connection
have **not yet been run on hardware**. The core decoder's historical hardware
fixtures passed; no microphone or kernel-driver experiment was conducted.

## 2026-10-18 Windows direct-controller probe result

Owner executed from PowerShell:

```text
dotnet run --no-build -c Release --project tools/A2854.Probe/A2854.Probe.csproj -- --seconds 30
WinUSB controller unavailable: InvalidOperationException.
An additional USB Bluetooth dongle must be under WinUSB.
Do NOT change the laptop's built-in Bluetooth adapter driver.
```

**Interpretation:** The diagnostic executable starts successfully, but
`UsbHci.Open()` found no eligible Bluetooth USB interface bound to WinUSB.
No connection or GATT request was attempted; this is **not** an A2854
transport failure, HID denial, or decoder failure. The built-in Intel AX200
remains under Windows' normal Bluetooth driver. Repeating this command
without supplying an independent WinUSB HCI controller cannot test A2854.

Next experiments depend on the user's requirement: use a separate supported
USB Bluetooth dongle under WinUSB for fully native Windows HCI, or use the
already demonstrated Linux/BlueZ input receiver inside WSL2 and forward
events to a Windows application (Windows frontend, Linux Bluetooth backend).
Never silently swap the built-in AX200's driver.

## 2026-10-18 Intel AX200 Windows USB driver confirmation

Owner provided these Windows PowerShell diagnostics after the WinUSB
diagnostic exited without detecting a controller:

```text
Get-PnpDevice -PresentOnly (VID_8087&PID_0029):
FriendlyName : Intel(R) Wireless Bluetooth(R)
Status       : OK
Class        : Bluetooth
InstanceId   : USB\\VID_8087&PID_0029\\[host-specific instance omitted]

Get-PnpDeviceProperty:
DEVPKEY_Device_Service       BTHUSB
DEVPKEY_Device_DriverInfPath oem116.inf

usbipd list:
BUSID 1-3, VID:PID 8087:0029, Intel Wireless Bluetooth, Not shared
```

**Conclusion:** Windows detects the Intel AX200 and binds it to **BTHUSB**
(not **WinUSB**). Consequently the WinUSB-only `UsbHci.Open()` does not
open this device. The `usbipd` status is unrelated to WinUSB eligibility:
`Not shared` only indicates the device is not currently shared for
USB/IP/WSL. This is a controller-access precondition failure, not evidence
of an A2854 BLE/GATT connection failure.

Do not replace the laptop's internal Bluetooth USB driver merely to
test this probe. A separate USB adapter under WinUSB is the safe direct-HCI
experiment; a distinct native BTHUSB integration would be a new backend.

## Final attempt: built-in Windows HID child only (2026-10-18)

A guarded PowerShell script has been added at
`tools/A2854.Probe/LastBuiltinHidAttempt.ps1`. It runs on the **native
Windows** host; it does not require an independent WinUSB Bluetooth
adapter. It is a targeted attempt to recover the existing **failed Windows
HIDClass device**, not a way to bypass protected GATT access.

1. Run `git pull --ff-only` on `feat/a2854-support`.
2. In normal PowerShell run the script **without arguments** for a
   read-only preflight.
3. If and only if it reports exactly one matching `DL1`/Siri Remote
   descendant (`HIDClass` + `mshidumdf` + Code 10 + NTSTATUS
   `0xC00000B9`), explicitly launch elevated PowerShell and run
   the script with `-Restart` once.
4. It restarts **only** that HID child via the documented
   `pnputil /restart-device <instance id>`; it never restarts the Intel
   AX200 controller itself, changes `BTHUSB`, reinstalls drivers,
   changes pairing, or modifies registry entries.
5. It reports whether the child reached `OK`; it **does not** prove raw
   `0xFB` or `0xFC` delivery. If `OK`, check a newly usable Windows
   HID interface in a separate follow-up; otherwise stop.

No GitHub Actions or real PowerShell parser were available in the authoring
environment for this script. The owner must first confirm the preflight
output on physical Windows and should not treat this as hardware-validated.

Microsoft documentation:
https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-command-syntax

## Next hardware experiment

Use an independent WinUSB Bluetooth controller, run this console, and share
only the redacted status and input output. If the controller is not detected,
stop; don't change built-in Bluetooth drivers. If pairing or GATT discovery
fails, diagnose those separately before claiming 3rd-gen support or wiring
the new decoder into the main GUI.

The fork and original project are AGPL-3.0-or-later; preserve original
copyright and licensing notices. This branch has not been submitted to the
upstream maintainer.
