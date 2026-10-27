# Remote directional buttons: mouse wheel mapping

The mapping UX follow-up builds on `a8ca438edb24ec4c6d74f8e1a60739532a839f2c`,
which already includes the sensitivity slider fix `443daea`.

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

**Input gating:** wheel and assigned `MouseLeft`/`MouseRight` actions follow
the remapped keyboard input setting. Enable **Virtual keyboard** for button
assignments. Pointer movement is separately controlled. Fresh settings use
keyboard input by default for A2854; legacy settings preserve the existing
choice, and an explicit A2854 opt-out remains saved. The first-generation
default is unchanged. A held wheel button sends an initial notch, then repeats
after 400 ms every 80 ms. Release, remapping, input disable and Stop cancel
repeats. Mapped mouse buttons share ownership with the physical center click,
so releasing one owner does not release a button held by another.

## Verification

The earlier `a8ca438` Windows offline QA passed 486/486 full tests and
86/86 focused tests, with Release build 0 warnings/errors. The follow-up
Windows offline QA passed 501/501 full and 117/117 focused tests, with
Release build 0 warnings/errors. Tests use fake sinks and schedulers; they
do not invoke actual Windows `SendInput` or BLE. An independent review of
the earlier click-ownership change found no confirmed defect.

Offline UI inspection with **no actual remote connected**:
- Confirm Up/Down selectors expose `Up`, `Down`, `ScrollUp`, `ScrollDown`.
- Confirm Assignment Studio's System category shows both wheel actions.
- Assign/reload JSON and restore defaults; no protected file changes.

For a later **separately approved** real-device test on an innocuous
scrollable document, enable keyboard injection, map Up=ScrollUp,
Down=ScrollDown and verify positive/negative wheel direction; release
must stop repetition and Stop must prevent further wheel events. Verify
mapped left/right clicks and shared ownership separately. Stop and Quit
should cleanly restore original settings and bond.

## Build status

The follow-up has not had independent physical-device QA for long-press
repetition or mapped mouse buttons. The user observed scrolling with the
prior one-notch build after enabling Virtual keyboard. Do not infer that
the new repeat or click paths have passed hardware testing. Production
`feat/a2854-support` and its local uncommitted changes remain untouched.
