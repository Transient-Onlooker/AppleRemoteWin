# AppleRemoteWin WPF GUI — A2854 integration (2026-10-23)

**Code committed; this new GUI integration has not yet been compiled or run on native Windows.** The **218/218** test baseline and physical 8-second HID trial at `1fd45a4` predate this code. Never claim mouse/keyboard injection is hardware-verified.

## Physical baseline

WinUSB Barrot `33FA:0001` on USB 3.0 hub has completed real A2854 SMP, CurrentUser DPAPI bond persistence, encrypted LTK reconnect and HID discovery. Physical Report Reference handles: button 0xFB=`0x0039`, touch 0xFC=`0x003D`, feature 0xF0=`0x004D`. Latest eight-second live test: 33 button frames, 360 touch frames, zero malformed; two CCCD disables acknowledged; host HCI normal `0x16`; exit code 0. Native GUI input synthesis remains unverified.

## Main-code implementation

- `A2854RemoteBle` in `AppleRemote.Windows`: a **single encrypted connection**, using only the existing user-protected bond; no SMP fallback, no key writes, no microphone. Reuses `LeCentral`, `GattClient` and `A2854HidCapture` to validate the actual physical 0xFB/0xFC/0xF0 handles; dynamically resolves CCCDs, enables only those two subscriptions and the known F0 AF feature, and forwards validated 0xFB/0xFC data. Best-effort CCCD disable and HCI local disconnect on exit, scrubs LTK/RAND arrays, sends a final button-release sentinel. No automatic reconnect yet: after remote sleep, user Stop/Start manually.
- Existing `RemoteSession`: explicit `A2854Mode` (off by default) opens **only Barrot VID:PID 33FA:0001** before historical ESP32/first-gen driver auto selection; old 1st-gen remains untouched when mode is off. Uses new `A2854Decoder` events in existing UI and KeyBinder, not the incompatible first-gen raw decoder; guards late input after Stop and against overlapping USB opens. Initial TV/Siri/Power actions disabled.
- `A2854PointerMapper` in Core is a pure testable event-to-relative-mouse adapter: one contact, fresh anchor, hover/multitouch/release reset, reject coordinate jumps, limit movement to 60 pixels/frame with no backlog, click edge tracking and forced release. Existing `NativeMethods.SendInput` is reused via new `WindowsPointerSink`.
- Existing WPF Settings UI adds separate **A2854 device opt-in**, **A2854 Windows mouse opt-in (off by default)** and sensitivity slider; mouse activation excludes duplicate click-to-Enter/macro. Existing Keyboard toggle controls independent media-key mapping. No pairing keys are stored in settings.
- `A2854PointerMapperTests`: fake-sink movement, hover, jump/no-backlog, click release, media-ignore and sensitivity edge cases. Existing gen-1 tests remain unchanged.

## Required Windows Codex verifier workflow

The main code is already authored upstream. Local Codex is **verification only**, not asked to write an independent integration.

1. Preserve local diffs, safely fetch/fast-forward `feat/a2854-support`.
2. Run `dotnet test tests/AppleRemote.Tests -c Release` and `dotnet build tools/A2854.Probe/A2854.Probe.csproj -c Release`.
3. Compile the **WPF app** with no external driver/firmware release downloads:
   `dotnet build src/AppleRemote.App/AppleRemote.App.csproj -c Release -p:AppleRemoteDriver=false -p:AppleRemoteDongle=false`.
4. Verify all new pointer tests plus first-generation, DPAPI, GATT, SMP, HCI regressions and WPF XAML bindings. Report exact errors. Analyze Stop/driver-callback races and mouse button release, with fake sinks rather than real `SendInput`.
5. **Stop before any real A2854 GUI connection, GATT feature write, mouse or keyboard synthesis.** Physical GUI test needs separate explicit user approval; retain Barrot WinUSB, Intel AX200 BTHUSB, DPAPI bond and existing certificates. Do not launch any auto-pairing or delete/rewrite keys.
6. After approval, first inspect the GUI with mouse synthesis OFF. Only later enable the mouse toggle for a short supervised test at low sensitivity. Auto reconnection and long-term durability are future work.

The CLI diagnostic is retained for isolated physical-layer tests, but the real user workflow is now the existing AppleRemoteWin application.
