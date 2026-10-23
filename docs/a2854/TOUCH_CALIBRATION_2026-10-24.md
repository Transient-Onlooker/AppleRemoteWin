# A2854 physical clickpad calibration — user-guided measurements

**Status: feature committed as source, but no independent Windows build or physical measurements have been made on this revision.**

## Why the original display does not line up

The current fallback `A2854TouchProjection` maps the full signed-12-bit protocol domain (`-2048..2047`) to the 360×360 canvas and clips the visible touch marker to a round pad (radius 165; marker center radius 150). This domain is **not the measured clickpad's physical contact range**. A finger at the true physical center may therefore appear offset, and a physical edge may be far from the rendered edge. Fallback stays available and is unchanged.

## Exactly how to measure

1. Start the WPF app with **A2854 mode**, `KeyboardEnabled=false` and `A2854PointerEnabled=false`; use the already protected DPAPI bond, never re-pair.
2. Go to **Settings → A2854 touchpad calibration**. The live `0xFC X/Y` value shows the measured point; no BLE address or keys are logged.
3. Physically identify a *repeatable fingertip-center point*, not the metallic outer ring/button housing. Recommended points: **center**, **leftmost usable center**, **rightmost usable center**, **uppermost usable center**, **lowermost usable center**. Avoid pressing the click switch hard enough to cause the device to behave differently.
4. Select a position in the dropdown. Hold **one finger steadily** there and click **Capture point** using the PC while the finger is still down. At least 5 valid reports from the latest 1.2s are required. The saved point in the summary confirms success; failed saves report an error rather than implying persistence.
5. Repeat for the other four points, **in any order or across multiple separate connections**. Values persist to `%APPDATA%\\AppleRemote\\A2854-touch-calibration.json`. This file is distinct from `%LOCALAPPDATA%\\AppleRemoteWin\\A2854\\bond.v1.dpapi` and contains only integer touch coordinates. Before QA, back up that file too if it exists.
6. A **valid** set of five points must have substantial, nonzero left/right and up/down spans around the physical center. Firmware axis inversion is determined from **which physical point was captured**, not guessed from sign.
7. After recording all five, use **Toggle preview** to display the *calibrated contact positions*. Calibrated visualization is **automatically activated** after a restart or model switch *only when the full stored five-point profile is valid*. Use **Toggle preview** to compare against the original uncalibrated signed-12-bit display. The actual Windows cursor mapper is unaffected. No calibration is inferred from invalid/incomplete profiles.
8. Verify visually: center is at the center; cardinal points reach the inner, marker-safe circle; try diagonal movements and determine whether distortion remains. If so, note where, do not invent values or silently adjust parameters.

## Safety and limitations

- The touch-calibration subsystem **does not alter** HID decoder, LTK bond, Barrot WinUSB, LE connection parameters, pointer controls, or microphone handling. The separate early-ATT fix does modify ATT listening time; test it independently. Never perform a pairing, driver change, or OS input injection just to collect calibration data.
- A valid five-point profile is a **first-order asymmetric axis calibration**, not a 2D nonlinear distortion model. Future refinement may require intermediate and diagonal landmarks and per-contact angular measurements.
- The app captures only a median of recent decoded single-finger contact positions, no raw HID packet/blob. `0xFC` hover, release and previously stale events are not treated as usable captured samples.
- Since the independent tests showed a peer-initiated **0x13 disconnect near 30.1s even during active input**, plan to gather points over multiple sessions; do not regard short connection life as idle sleep.
- For QA, keep logs and screenshots **locally** under `AppleRemoteWin-Validation/`. Do not create an additional GitHub QA repository.

## Independent QA checklist

- HEAD matches developer commit; build Core/WPF in Release without driver/dongle downloads; **one full test pass** plus new `A2854TouchCalibrationTests`.
- In offline unit tests: physical center and asymmetric cardinal points, reversed axes, points missing or invalid, outlier clamping, median requires 5 recent frames.
- WPF: check all five choices, default preview OFF, live X/Y updates only in A2854 active touch mode, Capture disabled by input validation when no recent samples, clear failure messages, saved point reload across restarts, visual preview toggles; legacy drawing untouched.
- Live A2854 only if available, safely bounded, with keyboard and mouse injection OFF. User picks physical landmarks; without physical reach, do not claim actual calibration values.
- Exit app, check settings restored, bond unchanged and no residual processes.


## 2026-10-24 — QA passed five-point touch calibration and next early-ATT fix (pending new QA)

**Independent user-provided local Windows QA of HEAD `9d400747ef1e293598686ca6891f08c29e5ba5af`:** Release build 0 warnings/errors, **313/313 tests passed**, real user-captured five physical clickpad reference points retained in separate local `A2854-touch-calibration.json` and physically checked: touch display alignment improved substantially. Original verifier left app stopped and settings restored, protected DPAPI bond unchanged, residual processes 0. Local report: `AppleRemoteWin-Validation/touch-9d40074/QA_REPORT.md` (not in repository). This result does **not** test automatic application or subsequent early-ATT source.

**Main developer source modifications awaiting independent Windows QA:**
- **Automatic calibrated visual display:** on startup and on returning to A2854 mode, a complete, geometrically valid saved five-point profile is applied by default to the **drawn touch markers and trails**. Partial/invalid files retain the uncalibrated fallback, first-gen behavior is unchanged, and the user's Toggle preview comparison can temporarily turn calibrated projection on/off. These controls never alter the actual Windows pointer mapper, sensitivity, OS input synthesis or bond.
- **Early ATT handling:** the existing `GattClient` was registered only **after the LTK encryption phase**, so a remote-initiated ATT Exchange MTU (0x02) or other ATT request between HCI LE Connection Complete and that registration could be silently dropped. ATT protocol transactions can time out in ~30 seconds and a remote could then close with 0x13; this is **a credible hypothesis, not a demonstrated root cause**. The new dedicated A2854 session creates a *single* GattClient and attaches its L2CAP ATT listener **before ConnectAsync**, binding it synchronously upon `LinkEstablished` through the already-existing safe trace event, *before* the caller's async continuation and EncryptAsync. It replies to early peer ATT requests using the same prior MTU/error handling, without re-pairing, new GATT writes or feature changes. The existing fixed-handle GattClient constructor for legacy call sites is unchanged. A fake-HCI regression test verifies initial MTU response before encryption/await, safe error response to unsupported ATT request, unrelated-handle exclusion and Detach cleanup. Logs now count early ATT opcodes without payloads, addresses or keys. **This is the first actual protocol behavior change to investigate 30s loss since passive tracing was added.** An on-device outcome must be proven with a bounded trial; no automatic retries or persistent keepalive.
- **Unchanged:** controller event mask (LE 0x06 still masked), link interval/latency/supervision, encrypted-bond bytes, Barrot/Intel drivers, 0xFA microphone exclusion, first-gen/ESP32, pointer synthesis disabled by default. Do not confuse an initial ATT MTU exchange with an unsolicited OS media-button injection.

**Next local independent QA:** checkout exact new HEAD, read AGENTS, build Release with `-p:AppleRemoteDriver=false -p:AppleRemoteDongle=false`, run one full test suite and targeted `GattValidationTests` plus loaded five-point UI checks; **do not overwrite or restore away actual user's valid touch measurements**. With prior user authorization for normal A2854 GUI tests, one bounded real-device reconnection with pointer/keyboard OFF; capture whether an early ATT request was seen/answered, then whether the peer passes **35 seconds**. If still 0x13 after ~30 seconds, report and stop: do not infer new fix worked. If link lasts past 35s, trigger host GUI Stop and verify both CCCD disable acks and HCI local 0x16. Preserve DPAPI bond/settings/profile and terminate processes. No new driver, pairing, security or BLE parameter changes without a separately justified decision.


## 2026-10-24 — harden physical touch calibration editing (developer source; pending Windows QA)

Follow-up after **b575771** (valid saved five-point profile automatically used for A2854 visualization, not SendInput; pre-encryption ATT responder attached before ConnectAsync). This commit does **not** change Bluetooth, native USB, bond or connection-timeout behavior. It makes calibrated touch capture safer for the physical measurements which the owner painstakingly collected:

- Only one **currently active** finger can be captured. On release, hover or multitouch, the sample window is cleared, preventing stale points captured after the finger has left the pad.
- In addition to the prior median, capture now requires >=8 frames covering >=75ms, and per-axis median absolute deviation <=40 signed coordinate units, rejecting active swipes while tolerating a single bad frame.
- An A2854-only portable `A2854TouchCalibrationStore` validates stored names and signed 12-bit coordinates, writes the JSON using a same-directory temporary file plus atomic rename, and **backs up the last complete valid physical profile to `A2854-touch-calibration.json.bak` before overwriting**. A corrupt existing file is refused instead of silently discarded. This does not read or write the DPAPI bond.
- Settings indicates whether the calibrated display or raw comparison is active, labels the toggle clearly, and offers **Copy points** to share the five measured coordinate pairs without exposing BLE addresses/keys.
- Four offline tests added for stable vs moving contact, outlier/staleness, intact backup, and corrupt-file refusal. **No Windows build or full suite was executed by the main agent.**

The user previously reported local Windows QA at `9d40074`: 313/313, real five-point physical calibration much better; the newly added backup and stability rules are unverified and must not be presented as that result. The `b575771` early-ATT and auto-apply trial is pending external QA. Next validation should build the new HEAD once, test Store/save/restart and affected UI bindings without overwriting real measurement data; preserve existing `.bak` where present.


## 2026-10-24 — separate opt-in use of physical touch calibration for Windows pointer (source, not QA-verified)

The previously user-measured five-point A2854 physical profile is now optionally usable for **real Windows relative mouse motion**, in addition to the automatic screen-contact visualization. The new Settings preference `A2854PointerUseCalibration` defaults **false** even for owners with a valid profile. Enabling it loads `%APPDATA%\\AppleRemote\\A2854-touch-calibration.json` read-only through the core store; a complete validated profile yields physical-relative X-right and Y-up coordinates, correcting reversed axes and asymmetric spans. Movement is converted to bounded relative steps inside the existing pure `A2854PointerMapper`. Existing anti-jump checks, max 60px/frame, hover/multitouch resets, click release and sensitivity controls remain in effect. Changing the optional calibration re-anchors the first touch to prevent a jump.

The actual OS SendInput continues to be gated by **A2854PointerEnabled** separately; opting into physical-axis mapping does not enable cursor movement. If stored landmarks are missing or invalid, the original raw pointer mapping remains active and a non-sensitive status line explains the fallback. First-generation and ESP32 mapping are unchanged. No BLE, GATT, reader, link duration, driver, DPAPI bond, keyboard controls or calibration profile file is modified by this feature. Two additional pure fake-sink tests cover axis reversal and mapping changes without accidental click or cursor jump. Source is pending independent Windows build/interactive pointer test; never treat a test of the GUI touch marker as proof that actual mouse motion works.

Five-commit development batch after external successful `9d40074` QA now includes calibration backup/stability, safe manual reconnect, A2854-specific Remote card, sanitized 48-event timeline and this optional physical pointer mapping. The *earlier* `b575771` includes early ATT responder and auto-application of the valid visual profile. None of these post-`9d40074` features has Windows runtime verification by the main agent; run a single final HEAD full-suite validation and focused GUI/physical smoke test without overwriting measured landmarks or disabling bond security. Since real SendInput can move/click other applications, test pointer opt-in only in a controlled visible desktop with an immediate OFF/Stop fallback.
