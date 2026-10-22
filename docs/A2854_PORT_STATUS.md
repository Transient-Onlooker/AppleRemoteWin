# Siri Remote A2854 (third generation) — development branch

> **Latest checkpoint (2026-10-21):** [current consolidated Korean handoff](HANDOFF_2026-10-21_A2854.md). The one-time HID child restart ran on the actual Windows PC but Code 10 / 0xC00000B9 persisted. The recent random-address BLE changes remain **unverified by the next Windows test run**. A ThinkPad T490s with AC 9560 was reported but has **not** been tested with A2854; external dongle chipsets remain candidates, not proven support.

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

## 2026-10-19: One-time built-in HID restart failed; prepare direct-HCI privacy-address support

Owner-provided native Windows result on the physical A2854:

```text
Remote anchor candidates: 1
Unique failed HID candidates: 1
Target checks passed: one remote descendant, HIDClass, mshidumdf, Code 10, NTSTATUS 0xC00000B9.
PnP restart exit code: 0
After restart: device status=Error; problem code=10; NTSTATUS=0xC00000B9
HID not recovered (or not proven recovered).
```

Preflight matched exactly one failed Siri Remote HID descendant, and an administrator
restarted **that HID child exactly once**. The restart command returned 0, but
the node remained Code 10 / 0xC00000B9. The built-in Intel AX200 remained
healthy with BTHUSB and driver 23.160.0.9. No raw Windows 0xFB or 0xFC
input was received; do not repeat this same restart or install the gen-1
kernel filter in an attempt to recover A2854.

The next hardware path is a *separate*, USB Bluetooth controller under
WinUSB. The original upstream has tested the TP-Link UB500 Realtek
RTL8761B/RTL8761BU controller family for gen 1, but that does not
constitute an A2854 compatibility claim. Verify the adapter's actual USB
VID/PID and provide a driver restoration plan before changing the **external**
controller's driver; preserve the built-in AX200.

Ahead of this experiment, the feature branch now passes discovered BLE
peer address type (public or random) to HCI Create Connection and
legacy SMP confirm generation when the A2854 probe opts in.
The rotating/random address is freshly scanned rather than blindly
reconnecting to a stale stored address. This is a proposed
code path, **not yet build-verified or hardware-verified** at the time
of writing; its single-remote LTK reuse assumes only one A2854 in range.
A simulated peer regression test was added. The gen-1 path continues
using public-address scanning by default.

A2854 pairing security mode, advertisement fragmentation, possible
resolvable-private-address identity handling and actual 0xFB/0xFC
receipt over the new controller remain open.

## 2026-10-21 purchased BR8652A12 external dongle (next test hardware)

Owner reports buying the available **Daiso Axxen Bluetooth 5.3 USB adapter**, after the older Realtek 5.0 adapter was unavailable, and identifies the chipset as **Barrot / BRLink BR8652A12** (not the previously speculated BR8651). USB VID/PID, descriptor class, Windows driver status and A2854 native receive have **not** been obtained yet. This changes the next test target, not the success status.

See [BR8652A12-specific evidence, known Barrot HCI quirks and safe Windows preflight](a2854/BARROT_BR8652A12.md). Do not force Realtek firmware onto this controller or replace the built-in Intel AX200 driver. The external dongle's original driver binding should be recorded before considering a reversible WinUSB-only experiment.

## 2026-10-21 Axxen Barrot 33FA:0001 native Windows PnP result

Local agent physically enumerated the purchased Daiso Axxen dongle: **USB 33FA:0001 rev 8891**, class compatible ID **E0/01/01**, manufacturer Barrot Technology Co.,Ltd., product BARROT Bluetooth Adapter. Windows binds **BTHUSB** via **oem80.inf / brbtusb.inf** v**21.46.25.278** but the node shows **Code 31 / CM_PROB_FAILED_ADD / NTSTATUS 0xC0000001**. The USB path traverses a **2109:2815 hub**; endpoint descriptors and WinUSB native operation are still unmeasured. Internal Intel AX200 **8087:0029** remains BTHUSB, driver 23.160.0.9, OK.

After BLE random-address changes, the local agent ran **90/90 .NET Release unit tests PASS** and **A2854.Probe Release build clean** at **6093954**. Subsequent **Barrot EP0 class/interface request 0x21** and **explicit `--usb 33FA:0001` diagnostic selection** were added to source, but have *not* yet been validated with fresh Windows build/tests or physical HCI input. Upstream Linux Barrot reverse-engineering supports H:2 for this VID/PID; it does not prove this dongle receives A2854 on Windows.

Next steps: backup `oem80.inf`, inspect redacted Windows setup failure, consider direct USB port instead of hub, only then a reversible **external-dongle-only** WinUSB binding with established restoration route; never alter internal Intel Bluetooth. See [BR8652A12/33FA:0001 report](a2854/BARROT_BR8652A12.md).

## 2026-10-21 Barrot 33FA:0001 second preflight (96/96, endpoints, custom-driver decision)

Local agent verified **96/96 Release tests PASS** and clean `A2854.Probe` build at `7c6d0cb`. SetupAPI/Kernel-PnP confirm `bth.inf` **already failed Code 31/0xC0000001 before** the Barrot `oem80.inf` package was installed; the vendor driver also fails the same way. Device configuration event 400 succeeded but start event 411 failed. Thus the Barrot lower filter is not proven to be the only cause. Full-speed USB (via `2109:2815` hub) has HCI interface 0 class `E0/01/01`, EP `0x81` interrupt IN (16 B/1 ms), `0x02` bulk OUT (64 B), `0x82` bulk IN (64 B); interface 1 handles isochronous transfers. The `oem80.inf` vendor driver was exported locally and INF/CAT/SYS checked. **No WinUSB binding or active BLE/HCI operation yet.**

Follow-up software fix: `UsbHci` now enumerates its actual **WinUSB function interface GUID** `{6B3E8C52-9D41-4F6A-8A1E-5C0D7B2F4E19}` first; previously it only enumerated USB device interfaces, potentially missing the driver interface even after binding. Await fresh local build/verification.

Next: direct-laptop USB-A Code 31 comparison; try a **Microsoft-signed inbox WinUsb Device** selection only if Windows offers it for the exact dongle and device-scoped interface registration is possible, preserving the Intel AX200. Avoid the custom driver installer that adds a machine-wide self-signed trusted root without separate approval. [Complete decision and rollback notes](a2854/BARROT_BR8652A12.md).

## 2026-10-22 external Barrot 33FA:0001: first native Windows HCI success

The user/local agent reports the externally purchased dongle now **WinUSB `oem86.inf`, PnP Started**. An existing previously compiled Release `A2854.Probe --no-build --usb 33FA:0001 --hci-only` successfully received HCI Reset and Read Local Version responses, with HCI USB EP0 request **0x21**, HCI/LMP version **0x09** (reported Bluetooth **5.0**), HCI revision **0x0202**, company identifier **0x08E7** (Barrot), LMP subversion **0x0002**. This verifies Windows-native USB HCI *transport*, **not** BLE scanning, A2854 pairing or input. The Intel AX200 remains Started. Experimental certificate in LocalMachine My/Root/TrustedPublisher has matching thumbprint, expiry **2026-11-21**; physical rollback has not been attempted.

Important independent blocker: the attempt to build with plain `dotnet run -c Release` failed, while `dotnet test -c Release` hung and was manually interrupted. The previous **96/96** unit test pass is not a fresh run at HEAD `8256a5a`. The HCI success came from the previous compiled binary. **Exact error output is needed before diagnosing cause.** A new privacy-preserving `--scan-only` mode was committed afterward; do not run it with `--no-build` until fresh source compilation has succeeded. [Device data and next steps](a2854/BARROT_BR8652A12.md).

## 2026-10-22 Windows-native BLE advertising: 770 events, 155 Apple-HID co-advertisements

The local agent updated to `4820fc17942b2d53dee168917db52a2c7659e383` and diagnosed the earlier Release failure as **sandbox filesystem write denial to `obj` (`MSB3491`)**, not NuGet restore or source compilation. After running in a writable path, **Release build succeeded with 0 errors/0 warnings**. Old MSBuild server processes were stopped; fresh single-node tests with 60-second hang monitor **passed 96/96**. A previous Release diagnostic binary was backed up locally.

On the **actual Windows WinUSB Barrot `33FA:0001`** controller, `--scan-only --seconds 30` initialized LE successfully (Extended Advertising supported) and collected:

```text
packets=770
apple_ad_packets=178
hid1812_ad_packets=155
both_in_one_packet=155
```

The first scan had 444 events / 11 Apple company-ID / no HID, suggesting pairing-advertisement timing mattered. The second scan's **155 co-advertisements** are evidence of an **Apple HID-over-GATT advertiser**, not yet unequivocal identification of the specific A2854. No Windows BLE connection, pairing/bonding, GATT or input report was attempted. The Barrot remained `oem86.inf` Started and Intel AX200 remained Started; experimental signing cert expiry 2026-11-21, physical restoration still untested.

The owner asked that the detector identify the known model more specifically. The repository's historical paired-device name starts **`DL1…`**; reference manufacturer header `07 0D 02 15 03 02`, but the actual `FakeRemote.PairingAdvertisement` HCI-trace fixture starts `8A 07 6D 02 02`. Both share Apple `0x004C` + HID `0x1812` and must be supported: do **not** use only the reference fingerprint or a BLE name (often omitted). New `src/AppleRemote.Core/A2854Advertisement.cs`, tests, and `--scan-only` aggregate `strong_candidates` counters were committed after `4820fc1` and remain **uncompiled/unverified** by the local agent. [Detailed observations](a2854/BARROT_BR8652A12.md).

Next: build/test new matcher → scan while waking physical A2854 after the scan starts → confirm known fingerprints without logging identity/address → propose explicit connection-only diagnostic; **do not silently perform SMP pairing or save bond keys**.

## 2026-10-22 source audit at cc129a3 (current verification)

Update: the earlier verification boundary below is superseded by this local Windows audit. In a writable temporary checkout, the full Release solution build passed with 0 errors, A2854.Probe Release build passed with 0 errors, and 109/109 tests passed. NU1900 indicates NuGet vulnerability-feed access was unavailable; the full solution build emitted non-fatal app post-build external artifact download warnings. No hardware command was run in this audit.

`LeCentral.ConnectAsync` now allows one pending create attempt, awaits the cancel command, drains late completion and disconnects any late-created handle before returning cancellation. Both cancel-race statuses 0x02 and 0x0C are handled. The next physical gate remains link-only: `dotnet run -c Release --project tools/A2854.Probe/A2854.Probe.csproj -- --usb 33FA:0001 --connect-only --seconds 30`. Do not infer link success from prior HCI or advertising results. No SMP, GATT or bond operation is part of this diagnostic.

## 2026-10-22 desk research / no-remote improvements before link test

Local agent report at HEAD \`1375d4d\`: full Release tests **103/103 passed**, standalone Probe Release build succeeded. The owner's physical A2854 was unavailable; \`--connect-only\` timed out **at advertising scan**, so **no connection attempt or cleanup Reset took place**. Prior real Barrot scan had already measured **95 strong A2854-format advertisements** with Apple \`0x004C\`, HID \`0x1812\` and manufacturer prefix A. Physical LE link, security, GATT and native input are untested.

ChatGPT compared Apple model/pairing guidance, Bluetooth SIG USB transport and LE HCI event mask, Linux HCI initialization and SIG Security Manager test vectors. Changes after the 103/103 baseline:

1. \`src/AppleRemote.Core/Ble.cs\`: extended-controller LE event mask changed from **\`0x101F\` to \`0x121F\`**, enabling bit 9 (**LE Enhanced Connection Complete**) alongside bit 12 (Extended Advertising). The existing Bluetooth 5 fake-controller unit test now uses **enhanced** completion. This is a preventative protocol fix, **not a proven cause of a prior link failure**, because no real link command has yet been issued.
2. \`src/AppleRemote.Core/SiriRemoteBle.cs\`: A2854-only pairing-capable path now uses \`A2854Advertisement.Classify(...).StrongCandidate\`; previously it used only Apple+HID and could pick an unrelated Apple HID device. Original first-generation matcher remains unchanged; a new regression test covers weaker Apple+HID data.
3. Existing \`tests/AppleRemote.Tests/SmpTests.cs\` already has **independent official Bluetooth SIG c1/s1 specification vectors**. These were separately calculated against the specification's fixed numbers and matched; no redundant test was needed.

See [the linked standards-backed research report](a2854/RESEARCH_2026-10-22.md) for references, cancellations/privacy/security concerns, and the staged plan.

**Verification boundary:** these *newest* source edits have been pushed but **not yet compiled or tested on the owner's Windows system**; 103/103 belongs to \`1375d4d\`. The available assistant container has no .NET SDK; static code inspection alone is not a replacement for local build/tests. Do not make device-driver, certificate, pairing or bond changes until the owner can participate.

## 2026-10-22 first live Windows-native A2854 LE link and 0x3E disconnect follow-up

The owner's Windows local agent reported the first successful **A2854 LE Connection Complete** through the external WinUSB Barrot `33FA:0001` Bluetooth dongle (`oem86.inf`, EP0 `0x21`). The physical A2854 entered advertising mode when the user pressed Back + Volume Up. The high-confidence Apple company `0x004C` / HID `0x1812` / known A2854 manufacturer header was found, the peer type was public, and the controller's extended LE path produced `LE Connection Complete OK` with a connection handle. This is the first *actual link-layer connection-completion result*, not just an advertisement finding. No SMP, encryption, GATT, HID or bond operation was attempted.

The probe then printed `Disconnection Complete OK (reason=0x3E)`. **This does not prove a clean local HCI Disconnect.** The Bluetooth SIG assigns `0x3E` to Connection Failed to be Established / Synchronization Timeout. A typical locally initiated disconnect reports reason **`0x16`** in the local Disconnection Complete event. The old CLI treated any completed disconnection event as an unconditional success. The actual order of HCI Disconnect Command Status and early link termination was not recorded; do not infer which component caused it. The link-completion event succeeded, but stability/normal termination has yet to be tested.

The local agent had already verified **109/109** tests plus a successful Release solution and Probe build **before these newest GitHub changes**. An attempted local Markdown/Program patch could not be applied because of an approval-layer limit; its uncommitted diff remains only in a user-local backup and was **not** inspected or automatically applied by ChatGPT. Preserve it when syncing.

GitHub code follow-up adds a bounded **1500 ms idle observation after connection**, explicit host-Disconnect Command Status logging, matching handle/Disconnection Complete status validation, reason name and elapsed timing, and an anomalous-result exit for reason `0x3E`. A reason classification helper and parameterized tests distinguish `0x16` clean local disconnect versus `0x3E` and other reasons. The source changes were **not** built or run on the owner's hardware during this assistant turn.

**Next validation gate:** safely reconcile any local diff with the GitHub branch, build/test fresh, then repeat **only** `dotnet run -c Release --project tools/A2854.Probe/A2854.Probe.csproj -- --usb 33FA:0001 --connect-only --seconds 30`. Start scan before activating remote pairing advertisements; classify link state **before host Disconnect** separately from events **after** the command. Do not perform SMP/GATT/bond operations until link stability and termination behavior are understood.

Details and Bluetooth SIG citations: [First LE link hardware report](a2854/LINK_PROBE_2026-10-22.md).

## 2026-10-22 live LE link observation and clean 0x16 local disconnect verified

On real hardware with owner present, the controlled repeat of `--connect-only --usb 33FA:0001 --seconds 30` succeeded completely:
1. The physical A2854 was woken with Back + Volume Up, and strong candidate advertising was acquired.
2. `LE Connection Complete OK` was achieved via WinUSB Barrot `33FA:0001`.
3. The link was observed idle for 1500 ms without SMP/GATT traffic; the link remained intact with zero spontaneous drops.
4. The host issued `HCI Disconnect`, receiving `Command Status OK`.
5. `Disconnection Complete` arrived at 1544 ms with reason `0x16` (`Connection Terminated by Local Host`) and `after_host_request=True`.
6. This resolves the previous `0x3E` anomaly and confirms link stability and clean local termination. SMP pairing and GATT report access can now be approached with verified link foundations.

## 2026-10-22 SMP pairing negotiation diagnostic (--smp-negotiate-only)

Implemented an isolated, safe SMP pairing negotiation diagnostic mode (`--smp-negotiate-only`) in `A2854.Probe`:
1. Establishes the physical LE link on WinUSB Barrot `33FA:0001` and checks link stability for 500 ms.
2. Sends a standard SMP Pairing Request on L2CAP CID `0x0006` (`NoInputNoOutput`, `OOB=0`, `Bonding=0x01`, `MaxKeySize=16`, `RespKeyDist=EncKey`).
3. Receives and analyzes the peer's `Pairing Response` (Opcode `0x02`) or `Pairing Failed` (Opcode `0x05`) without proceeding to Confirm/Random key generation, DHKey checks, encryption start, or bond storage.
4. Analyzes IO Capability, OOB data, AuthReq flags (`Bonding`, `MITM`, `SC`, `Keypress`, `CT2`), encryption key size, and key distribution masks to classify whether A2854 accepts Legacy Just Works or strictly requires LE Secure Connections.
5. Initiates graceful local `HCI Disconnect` and verifies clean `Disconnection Complete` (`0x16`).
6. Comprehensive unit tests (`SmpNegotiationTests.cs`) cover PDU parsing, validation, reason code mapping, and mock L2CAP exchanges (125/125 Release tests passing).

## 2026-10-22 physical A2854 SMP negotiation success: Legacy Just Works verified

Live hardware execution of `--smp-negotiate-only --usb 33FA:0001` produced a landmark protocol confirmation:
1. **Pairing Response Received (545ms)**: Remote responded with `AuthReq = 0x01` (`Bonding = True`, `MITM = False`, `SC = False`, `Keypress = False`, `CT2 = False`).
2. **IO Capability**: `NoInputNoOutput (0x03)`, `OOB = 0x00`.
3. **Key Size & Distribution**: `MaxKeySize = 16 bytes`, `RespKeyDist = EncKey(LTK) (0x01)`.
4. **Architectural Confirmation**: Physical A2854 does **not** enforce LE Secure Connections (SC=0). It natively accepts standard **Legacy Just Works pairing** (`TK = 0`).
5. **Crypto Compatibility**: The existing codebase's AES-128 `SmpCrypto.C1` and `SmpCrypto.S1` implementations are algorithmically complete and directly compatible with physical A2854 hardware without needing P-256 ECDH.
6. **Clean Disconnect**: Graceful host-initiated `HCI Disconnect` concluded at 575 ms with verified reason `0x16` (`after_host_request = True`).
7. Detailed report: [A2854 SMP Negotiation Report](a2854/SMP_NEGOTIATION_2026-10-22.md).

## 2026-10-22 full SMP pairing source gate — implemented, pending Windows Release build and hardware approval

The owner reported a **physical SMP Pairing Response** from A2854 while using external Barrot `33FA:0001` WinUSB on the USB 3.0 hub: `IO=0x03`, `OOB=0`, `AuthReq=0x01` (Bonding requested, SC/MITM off), `KeySize=16`, `InitiatorKeyDist=0`, `ResponderKeyDist=EncKey`. The probe disconnected with local HCI `0x16`. Last independently reported local source-test baseline: **125/125 passed** for `1653c23`. Full Confirm/Random exchange, STK encryption, LTK distribution and bond reconnection **have NOT yet been physically attempted or verified**. A legacy-only request returning SC=0 does not establish whether SC would be supported under different offers.

After the physical response, a separately gated, **not yet locally built**, `--smp-pair-only --confirm-pairing --usb 33FA:0001` diagnostic was added. It reuses the legacy `SmpPairing.PairAsync` implementation with whole-session cancellation and per-stage progress, strict observed A2854 security-profile checks, timing limits, constant-time confirmation, STK encryption, in-memory responder LTK/EDIV/Rand acquisition and best-effort volatile secret clearing. It does **not** create a GATT client, access HID, save bond data or modify USB/driver/certificate state. Both success and failure take the existing controller-specific link cleanup path. The shared legacy parser retains backward compatibility for older remotes by enforcing the strict A2854 profile **only in the explicit diagnostic**.

New mock tests cover physical-profile success, unacceptable SC/MITM/OOB/IO/bond/key-distribution flags, invalid key sizes, SMP Pairing Failed, incorrect remote Confirm, encryption rejection, cancellation while awaiting Pairing Response and missing remote bond-key distribution. Full solution and probe compilation **must be done on the owner's Windows .NET 10 machine** before live use.

The previous implicit full-driver default was disabled. The historical driver requires explicit `--full-legacy-driver --confirm-plaintext-bond` because that old route writes raw LTKs to a plaintext JSON file. Do **not** use it for the A2854 physical pairing experiment. Protected persistence (Windows user-scoped DPAPI or equivalent), approval and subsequent encrypted reconnection are separate future gates.

**Operator warning:** a live full pairing attempt, even with keys discarded in memory on this PC, can disrupt the A2854's existing Apple TV bond. Ask the owner for separate informed approval **after successful local build and tests**. Do not auto-run repeated attempts or automatically change pairing modes.

See [SMP pairing implementation details](a2854/SMP_PAIR_ONLY_IMPLEMENTATION_2026-10-22.md), [staged safety plan](a2854/SMP_PAIRING_PLAN_2026-10-22.md) and [physical SMP negotiation evidence](a2854/SMP_NEGOTIATION_2026-10-22.md).

## 2026-10-22 — real full Legacy SMP succeeded; guarded Windows bond/reconnect/GATT code prepared

**Physical verification, reported by owner/local agent:** on Windows 11, with the external Barrot `33FA:0001` bound to WinUSB on a USB 3.0 hub, A2854 successfully completed **SMP Just Works Pairing Confirm/Random, peer Confirm verification, STK Link Encryption, LTK + EDIV + RAND distribution, and local HCI Disconnect (reason 0x16)**. The reported pairing timestamp was 932ms and normal link release at 963ms. The local agent previously reported **148/148** Release tests passing at `6cd4bbb` and a clean Probe Release build. The successful physical diagnostic **erased the bond keys on exit**, so **no encrypted reconnection / protected key persistence / GATT HID has been verified**. It is incorrect to report future reconnection from that key material.

**New source changes after that verified checkpoint (not yet compiled or physically executed):**
- A Windows **CurrentUser DPAPI** protected local bond store (`src/AppleRemote.Core/ProtectedBondStore.cs`) that serializes only one exact Legacy bond into encrypted bytes under LocalApplicationData; does not use or output plaintext JSON. An existing protected bond is protected against implicit overwrite even under concurrent runs. Best-effort transient secret scrubbing.
- `--smp-pair-only --confirm-pairing --save-bond --usb 33FA:0001` optionally retains new bond material only after complete legacy pairing; without `--save-bond` the existing ephemeral-only behavior remains. Both new full pairing and saving require operator approval because existing Apple TV pairing might be disrupted. No automatic retries or driver operations.
- `--reconnect-only --usb 33FA:0001`: **uses a previously DPAPI-stored public BLE address/LTK**, establishes a new LE link, starts encrypted session and checks Encryption Change; never SMP-re-pairs or changes keys. Explicitly refuses random stored peer address due to absent IRK resolution.
- `--gatt-inspect-only --usb 33FA:0001`: after encrypted reconnect, performs **ATT read-only** primary service and Report Reference `0x2908` discovery for HID service `0x1812`, reports local handles of `0xFB`, `0xFC`, `0xF0` if present. Does not subscribe or unlock 0xF0, capture HID data or microphone, or write any attribute.
- Hardened GATT discovery to reject zero-size, malformed and nonmonotonic entries, and added DPAPI Windows tests plus fake encrypted-GATT integration/negative parsing tests. Latest new-source test count **unknown** until Windows builds run.

**Next gate:** Local Codex agent must first fetch, **build and run fresh full Release tests**, especially DPAPI Win32 P/Invoke on actual Windows, GATT parser and all old tests, then fix/test/commit any failures. Do not interpret 148/148 as belonging to this new source. **Stop before new physical pairing** and obtain fresh user authorization to issue another pairing with `--save-bond`; the earlier keys were erased. Only after safe bonded storage may `--reconnect-only` and then read-only `--gatt-inspect-only` be tested. Never silently recover by deleting existing keys, re-pairing or overwriting them; preserve Intel BTHUSB and Barrot WinUSB driver bindings.

Full plan and official protocol references: [Protected bond/reconnect/read-only GATT gates](a2854/PROTECTED_BOND_AND_GATT_2026-10-22.md).

