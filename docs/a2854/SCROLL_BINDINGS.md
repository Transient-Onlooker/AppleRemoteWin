# Remote directional buttons: mouse wheel mapping

Branch: `feat/a2854-scroll-mapping`, based on sensitivity slider fix
`443daeaef00f3e02c9320fe2bbf01809574d59bf`.

## Requested behavior

In the Settings remote key pickers / Assignment Studio, assign the physical
A2854 **Up** button to `ScrollUp` and **Down** to `ScrollDown`. Existing
`Up` / `Down` Windows keyboard arrow mappings remain the untouched defaults.
These names also work as `keymap.json` strings, e.g.:

```json
{"up": "ScrollUp", "down": "ScrollDown"}
```

The scroll actions are implemented as out-of-range synthetic `KeyCode`
identifiers 0x0100 and 0x0101. They are **not** actual Windows VK codes.
`KeyboardInjector.TryKey` intercepts them and sends `INPUT_MOUSE /
MOUSEEVENTF_WHEEL` using `SendInput`: +120 for up, -120 for down. Emit
one notch on a button-down transition; button-up performs no mouse action
but is acknowledged to preserve the existing KeyBinder and shared-owner
cleanup semantics. Keyboard direction buttons in their default assignment
continue to synthesize standard VK_UP/VK_DOWN.

Settings' `KeyChoices` picks from `Keys.Names`, so the two new entries
appear automatically in the dropdown. Assignment Studio also lists them in
its System category (and general keys catalogue), with the existing
assignment/persistence callback. No schema migration or change to saved
bond / calibration / USB drivers / BLE / touch handling.

**Input gating:** wheel actions follow existing remapped keyboard input
settings. Enable the app's **keyboard input** toggle to activate remote
key mapping. Pointer mode is not required. Center-click ownership and mouse
drag are unchanged. A held arrow currently scrolls one notch per distinct
press, not continuous autorepeat; if held-repeat is requested later, add it
separately with a cancel-on-release/Stop policy.

## Verification

Source contains five new portable Core tests for named actions, true
VK-arrow defaults, wheel signs, JSON round-trip, distinct physical press
behavior, and rejected-wheel send retry. These don't invoke Windows
`SendInput`; Windows Codex must independently Release-build and run
the complete suite before considering merge.

Offline UI inspection with **no actual remote connected**:
- Confirm Up/Down selectors expose `Up`, `Down`, `ScrollUp`, `ScrollDown`.
- Confirm Assignment Studio's System category shows both wheel actions.
- Assign/reload JSON and restore defaults; no protected file changes.

For a later **separately approved** real-device test on an innocuous
scrollable document, enable keyboard injection, map Up=ScrollUp,
Down=ScrollDown and verify positive/negative wheel direction; release
must not produce a second notch and holding must not generate unbounded
repeats. Stop and Quit should cleanly restore original settings and bond.

## Build status

Source-only feature branch created for Windows Codex independent QA. This
author has not compiled Windows/.NET code in the present environment, so
no new PASS claim is made. Production `feat/a2854-support` remains
untouched.
