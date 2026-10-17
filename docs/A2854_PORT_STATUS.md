# Siri Remote A2854 (third generation) — development branch

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

## Next hardware experiment

Use an independent WinUSB Bluetooth controller, run this console, and share
only the redacted status and input output. If the controller is not detected,
stop; don't change built-in Bluetooth drivers. If pairing or GATT discovery
fails, diagnose those separately before claiming 3rd-gen support or wiring
the new decoder into the main GUI.

The fork and original project are AGPL-3.0-or-later; preserve original
copyright and licensing notices. This branch has not been submitted to the
upstream maintainer.
