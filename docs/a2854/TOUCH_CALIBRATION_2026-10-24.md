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
7. After recording all five, use **Toggle preview** to display the *calibrated contact positions*. Preview is only for the drawn contact/trace; never for Windows cursor injection. Toggling off returns to uncalibrated signed-12-bit display. It starts **OFF** on application startup and whenever models switch.
8. Verify visually: center is at the center; cardinal points reach the inner, marker-safe circle; try diagonal movements and determine whether distortion remains. If so, note where, do not invent values or silently adjust parameters.

## Safety and limitations

- This feature **does not alter** HID decoder, LTK bond, Barrot WinUSB, LE connection parameters, pointer controls, or microphone handling. Never perform a pairing, driver change, or OS input injection just to collect calibration data.
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
