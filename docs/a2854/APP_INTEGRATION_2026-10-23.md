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

## 2026-10-23 — post-build defect corrections (main author)

The independent Windows verifier fast-forwarded to `bd1e288` and reported **229/229 Release tests**, **11/11 pointer tests**, and full solution, WPF app and Probe builds with **0 warnings / 0 errors**. WPF runtime bindings and live Windows input were **not** executed. The verifier identified four *static* issues; the main author implemented the following corrections:

1. **USB read failure without Disconnection Complete:** a dedicated `A2854SessionFaultGate` cancels the connection task's linked cancellation token while synchronously revoking A2854 input under the existing `RemoteSession` event lock. It runs once even if both WinUSB readers fail. Cancellation is guaranteed via `finally` even if an event handler fails. An error reported after a disposed prior session cannot revoke a new session. `A2854SessionFaultGateTests` simulate no HCI terminal event, simultaneous USB errors, aborted release and late failures.
2. **Late notification re-press after peer 0x13:** `A2854InputGate` serializes `0xFB/0xFC` notification forwarding and the terminal empty release marker. Both natural HCI disconnection and general `finally` close the gate before ATT cleanup; late callbacks are dropped. Terminal HCI completion is still signalled even if release callbacks throw. `A2854InputGateTests` cover held callbacks, concurrent close, late notification and idempotence.
3. **Windows SendInput mouse-up failure:** `IRelativePointerSink.LeftButton` now returns actual backend success. `WindowsPointerSink` checks `SendInput` return count; `A2854PointerMapper` does not change held-button state until the OS accepts a transition. `Reset` tries mouse-up up to three times and returns failure if still held; subsequent releases remain possible. UI status explicitly warns about failure. Fake backend tests simulate rejected down/up, repeated failures and eventual recovery. OS-secure-desktop restrictions may still prevent an actual mouse-up; this is **best-effort**, not an absolute release guarantee.
4. **Sensitivity range mismatch:** WPF slider now covers the Core model's actual **0.02..3.0** range. Model tests include saved 0.8 sensitivity and both endpoints.
5. **Additional lifecycle hardening:** one lock serializes `RemoteSession.Start/Stop` to avoid two concurrent local USB opens. Existing protection while a previously stopped controller task is still cleaning up is preserved. This does **not** provide cross-process exclusive access versus a separately running Probe or another app.

**Validation boundary:** all above code/test changes are committed source. The 229/229 test result is the **prior** `bd1e288` baseline; newer release tests, WPF runtime bindings, physical GUI/HID, actual SendInput and USB failure injection on Windows have **not yet been run**. Do not claim those milestones prematurely.

### Local verifier (verification only, no new design)

- Safely fetch/fast-forward latest `feat/a2854-support` after preserving local work.
- Run full `dotnet test tests/AppleRemote.Tests -c Release`; run `dotnet build src/AppleRemote.App/AppleRemote.App.csproj -c Release -p:AppleRemoteDriver=false -p:AppleRemoteDongle=false`, full solution Release and A2854.Probe Release builds.
- Run `A2854InputGateTests`, `A2854SessionFaultGateTests` and `A2854PointerMapperTests` specifically; check exact failures/warnings and first-gen regressions. Verify WPF bindings, especially sensitivity 0.02..3.0.
- Review the two-lock order (`_lifecycleGate`, `_gate`) and Ctrl+C/Stop handling, early remote end, late GATT callback, fault cancellation, and SendInput failure. Use fake sinks only; no radio/USB changes needed.
- Report findings and counterexamples, but leave implementation changes to the main author. Do not run GUI, live GATT, keyboard/mouse injection, SMP, re-pair, change drivers/certs, or delete/rewrite the DPAPI bond without new explicit approval.

## 2026-10-23 — second independent review, main-author follow-up (246-test baseline)

The local Windows verifier reported **246/246 Release tests passing** (InputGate 4/4, SessionFaultGate 6/6, PointerMapper 18/18); WPF, full solution and Probe Release builds all finished with **0 warnings / 0 errors** at `d988a1b`. Physical GUI, mouse injection and Bluetooth hardware were not run. Four remaining static findings were reported and addressed in new source:

1. **Old USB fault callback after Dispose/new Start.** `A2854SessionFaultGate.Fail` and `Dispose` now share the **same critical section**; a pending failure revocation finishes before Dispose returns. Late failures after Dispose are dropped. A barrier-controlled test pauses the old callback while another thread calls Dispose, proving Dispose cannot complete prematurely. This complements the existing cleanup-task barrier between successive GUI sessions.
2. **GUI event exception during terminal release.** `RemoteSession.FeedA2854([])` releases keyboard and mouse in an unconditional `finally`, even if decoding, binding or a WPF event subscriber throws. Both release calls are isolated so one failure cannot skip the other. An optional, independent `A2854InputGate` final-release callback re-attempts cleanup when the primary terminal receiver raises, and refuses late notifications. New tests inject a throwing terminal receiver and assert fallback release still runs once.
3. **WinUSB reader error before registration.** `UsbHci` now records the first reader failure synchronously with a lightweight `A2854UsbFailureRelay` and exposes `WatchFailure` that replays it if the reader already failed while the adapter constructor was starting its threads. `RemoteSession` subscribes through that relay. The first-gen `Failed` event remains available, preserving its prior API. Pure tests check early and late errors, deduplication and concurrent subscription/error ordering.
4. **A2854 pointer toggle affecting 1st-gen keys.** The toggle now calls `KeyBinder.ReleaseAll` **only in A2854 mode**. Disabling the pointer still retries a previously held A2854 click if one exists, without touching 1st-gen keyboard bindings. This addresses the cross-model interaction without changing legacy decoding or key routing.

**Validation boundary:** the previous **246/246** result applies to `d988a1b`, **not** this follow-up. New source and test files were committed without running .NET on the owner's Windows machine. The user/agent must run fresh full Release tests and WPF/Probe builds before any physical GUI session. Never auto-pair, delete or rewrite the DPAPI bond, change the Barrot WinUSB or Intel AX200 drivers, or synthesize real mouse/keyboard inputs during software verification. Cross-process USB mutual exclusion is still not implemented.

## 2026-10-23 — third Windows code review: 254-test baseline, fault-safe HID dispatch

Windows Codex fast-forwarded to `ba96249` and reported: **first full Release run 253 passed / 1 failed**, rerun **254/254 passed**; `A2854SessionFaultGateTests` 8/8, `A2854InputGateTests` 6/6, `A2854UsbFailureRelayTests` 4/4, all Release builds eventually **0 warnings / 0 errors**. A first test compilation emitted **xUnit1031** for a blocking `Task.Wait`, and the first full run failed the gen-1 fake random-address pairing test after ten seconds. The isolated test and subsequent full rerun passed; its root cause remains unknown. No real GUI / USB HCI / SendInput test was executed.

**Main-author changes following that review (source committed; new full Windows test run pending):**

1. **Unhandled ordinary event/subscriber exception (high):** `RemoteSession.FeedA2854` now traps failures in ordinary 0xFB/0xFC event dispatch, revokes incoming A2854 input, always runs independent keyboard + mouse-up cleanup, then triggers the session-specific `A2854SessionFaultGate` **outside the input-state lock** to cancel the BLE session. `A2854InputGate.TryForward` now also closes the stream and invokes its terminal fallback if any receiver outside RemoteSession throws. At the WinUSB layer, `UsbHci.StartReader` catches `receive(...)` callback exceptions and delivers an error through the already buffered A2854 USB failure relay (and the original first-gen Failed event) rather than letting an exception kill the reader thread. New fake tests simulate both ordinary and terminal receiver exceptions and verify release once and rejection of late reports.
2. **Terminal callback interrupting `Stop()` (medium):** `RemoteSession.Stop` now uses a `try/finally` around cancellation, forced HID release and bounded driver wait. Even if GUI handlers, cancellation callbacks or terminal events throw, it clears `_cts`, `_driverTask`, `_tickTask`, adapter state and runs keyboard and pointer cleanup independently. Old A2854 USB task cleanup continues to block premature reopening.
3. **First-gen random-address mock timeout (unresolved intermittent):** strengthened the specific test to use a bounded 30-second deadline and report the pairing count, ATT-write count and status history upon a future timeout. Preserves the exact pairing/CCCD/F0 assertions and cancels/awaits the driver in `finally`. A longer deadline makes scheduling false positives less likely but **does not prove or fix the underlying intermittent fault**; repeated Windows test runs and diagnostic evidence are required.
4. **xUnit1031:** replaced blocking `disposing.Wait(TimeSpan...) ` in the async Dispose/fault race test with a fully asynchronous `await Task.WhenAny(disposing, Task.Delay(100))`. No longer blocks xUnit worker threads.

**Validation gate:** Previous 254/254 is the `ba96249` baseline only. All these changes need a fresh Release build and full regression run, ideally multiple runs to detect the intermittently failing gen-1 test. Codex remains a verifier, **not a second main coder**. No SMP rebonding, protected bond deletion/rewrite, Barrot/Intel driver changes, live GATT writes, Windows input synthesis or physical remote interaction during this verification.

## 2026-10-23 — A2854 Settings mapping panel (separate development after GUI fix)

**Implementation pending independent Windows build and runtime QA:** Settings now switches between the original first-generation key-mapping diagram (preserved byte-for-byte inside its own collapsed-by-mode container) and an A2854-specific mapping panel. The new panel shows the physical USB-C A2854 drawing with 10 editable input bindings: directional ring Up/Down/Left/Right, center click, Back, Play/Pause, Volume Up/Down, and Mute. The existing key-picker infrastructure, macros, application launchers, combo mapping, import/export and saved keymap remain shared. Mapping the same input name intentionally edits the same binding in either model; there is **no separate per-model profile** yet.

The portable `KeyMap.Inputs` now includes `mute` with default `VolumeMute`. New tests cover mute press/release, JSON round trip/disable and legacy JSON compatibility. The already established `KeyboardEnabled` gate still controls actual Windows keyboard injection; merely showing or editing a binding is not permission to send keys. A2854 TV, Siri and Power remain **display-only / blocked from keyboard & application actions** by `RemoteSession.FeedA2854`, and are intentionally not offered as editable pickers. The pointer click remains independently gated when pointer mode is enabled. No BLE, USB, native-pointer, bond, driver or hardware code is changed in this feature commit.

The preceding A2854 live UI-visualization revision `05c60b6` may be undergoing **independent QA concurrently**. Its test results, when received, must be attributed to that exact SHA, never to this Settings follow-up. After this commit, local QA should fast-forward and run one full Release suite, WPF build, and inspect Settings mode switching + mute KeyPicker. This source has not been built locally by the main ChatGPT agent.
