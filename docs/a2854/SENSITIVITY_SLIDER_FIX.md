# A2854 sensitivity slider usability fix (isolated branch)

Base: `f170c4a7dbe1f17e65e3b3ffaf7fec4f87e928e1` (production branch intentionally untouched).
Branch: `fix/a2854-sensitivity-slider`.

## User symptoms (2026-10-27)

The pointer sensitivity slider "jumps in chunks," mouse clicking on a desired
position does **not** move the thumb to that position, and clicking the track
then dragging doesn't work. Separately, a live remote session produced 2,718
0xFC touch frames through all the session and UI stages, 1,542 Windows mouse
move requests with no SendInput rejections, but UI queueing briefly reached
351 ms. Subsequent reconnect attempts were cancelled following a center-click
near the application's Stop button; do not reinterpret that as peer BLE loss.

## Root causes in inspected source

1. The WPF Slider had `IsMoveToPointEnabled=false` by default. Clicking the
   track therefore invokes discrete LargeChange behavior rather than setting
   the value to the clicked coordinate; thumb-only capture also prevents
   direct press-and-drag from an arbitrary track point.
2. `A2854PointerMapper.Sensitivity` called `ResetTracking()` every time the
   slider's two-way binding changed, discarding the next active 0xFC touch
   frame. This is especially bad when the *remote's own mouse pointer* is
   used to drag the sensitivity control.
3. `SettingsViewModel.OnA2854PointerSensitivityChanged` synchronously
   reserialized and wrote settings.json on **each** slider value change,
   generating unnecessary UI-thread I/O.

## Source fixes

- WPF `TrackDragSlider` preserves normal thumb drag but supports clicking
  anywhere on the track to set the value immediately, capturing subsequent
  mouse moves for smooth direct drag. Uses `Track.ValueFromPoint` and
  `SetCurrentValue` to preserve a two-way WPF binding. Uses the same min/max
  as previously (0.02–3.0) and does not change saved format.
- A2854 pointer sensitivity gain changes preserve current finger tracking,
  held click, click/drag anchor and previous coordinates. Only subpixel
  remainders from the previous gain are reset; recalibration/mode changes
  retain their original explicit ResetTracking behavior.
- Debounce settings writes (400 ms since last slider change), flush pending
  value on SettingsViewModel.Dispose. The current live gain always updates
  synchronously; only persistence is deferred.
- Two portable tests cover continuous touch motion and held-button dragging
  while sensitivity changes. No synthetic input or real BLE is invoked by tests.

## Required verification before merging

Windows Codex should check local uncommitted improvements independently,
build Release 0 warnings/errors, run full tests and focused
A2854PointerMapperTests, and open Settings on a passive app (no device Start).
With a **normal USB mouse first**, click at 10%, 50% and 90% of the slider,
then press anywhere on the track and drag steadily across the full width:
thumb and bound value should follow continuously in both directions. Check
dragging on the original thumb still works, settings value persists after
quit/relaunch, and 1st-gen UI is not affected. Check WPF theme and horizontal
overflow at 1024/1280/1600 DIP.

Only after separate explicit approval for actual A2854 link and Windows
SendInput may the same interactions be repeated using the remote's pointer:
enable pointer synthesis, ensure `click as keyboard` is **OFF** so physical
center button owns Windows left click, and use a safe UI location away from
Start/Stop. Verify left down is held continuously until actual center release
and that the slider follows the moving pointer. Stop/Quit and confirm bond,
calibration and original temporary settings remain unchanged.

Known non-goals: no changes to GATT/ATT, BLE connect/Stop, input button mapping,
TV power, gesture recognition or tray battery; those are separate tasks.
No physical test performed on this branch. The "earlier 479/479" suite belongs
to the prior production HEAD, NOT this patch.
