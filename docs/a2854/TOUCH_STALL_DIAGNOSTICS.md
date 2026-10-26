# A2854 touch response gaps — non-invasive diagnostic branch

Base: `f170c4a7dbe1f17e65e3b3ffaf7fec4f87e928e1`. Branch:
`diagnostics/a2854-touch-stalls`.

## Field observation that motivates this work

The user and Windows tester reported about **three minutes of uninterrupted
BLE connection**, and a **normal Stop/disconnect reason 0x16**, but
**intermittent response stalls WHILE moving a finger on the touch surface**.
This is not evidence that the BLE link disconnected. Previously the device
sustained about 83 seconds with normal shutdown, but no comparable continuous
touch performance measurements exist. No changed live hardware test is
authorized by this diagnostic commit.

## Design

Capture aggregate, interval-based counts **without recording raw packets,
touch coordinates, peer addresses, keys or bond data**, nor changing any
binding, ATT/GATT commands, connection parameters, hardware device access,
settings or persistent calibration.

- `raw`: ATT notifications arriving on discovered A2854 0xFC value handle.
- `hid`: 0xFC passed HID validation.
- `gate`: input gate accepted the decoded report and forwarded it.
- `session`: RemoteSession decoder emitted a TouchEvent.
- `ptr`: pointer converter processed a TouchEvent (zero if pointer disabled).
- `mouse`: a Windows relative cursor Move was requested; `mouseFail`
  counts SendInput rejects as observed by the current pointer backend.
- `uiQueued`: RemoteSession TouchEvent queued through Dispatcher.
- `uiDone`: remote-page WPF handler finished processing that event; NOT
  evidence that the DWM/compositor painted the frame.
- `uiDrop`: stale-epoch or stopped-session UI handlers skipped.
- `rawGapMax`, `sessionGapMax`, `mouseGapMax`, `uiGapMax`:
  maximum observed inter-event gaps in milliseconds (monotonic Stopwatch).
- `uiQueueMax`: longest time between UI enqueue and end of WPF handler.
- `receiverMax`: slowest synchronous input gate forwarding, including
  pointer/keyboard/UI enqueue callbacks.
- `pointerMax`: slowest pointer converter call, including SendInput.
- `uiHeartbeatMax`: longest interval between nominal 1-second scan timer
  ticks (indirect UI-thread starvation signal).
- `noMove[contact,first,jump,wobble,drag,pressure,subpx]`: explicit,
  unchanged pointer policy suppression counts. A high pressure count or
  jump count during continuous swipe identifies an algorithmic lead.

**Important limitations:** inter-event gap maxima include legitimate
intervals when the finger is lifted; examine them **only while actively
moving continuously**. A 5-second snapshot is concurrent and counts at
different stages may differ by one or more in-flight events at the boundary.
The logger outputs one aggregate line per ~5 UI timer ticks, not one line per
touch; queue buildup can delay the timer itself. These are diagnostic
measurements, not proof that Windows compositor rendered every input.

## Diagnostic procedure for Windows Codex

Before any live test: first independently compile Release solution and Probe,
run the full unit suite including A2854TouchDiagnosticsTests, and assert there
are no real hardware calls from test harnesses. This diagnostic branch does
NOT authorize starting the actual remote, input injection, probe, adapter
driver changes, or modifications to protected bond/calibration. Merge or
cherry-pick only after reviewing local improvements (uncommitted user work)
for conflicts.

If the user separately approves real device tests, record a baseline with
pointer OFF and A2854 app UI contact projection running for ~20 seconds of
continuous finger movement, then if separately approved enable pointer input
on a safe empty desktop and reproduce ~20 seconds. Note the wall clock of
each stall and compare the adjacent `A2854 touch/5s` lines:

1. `raw` drops and `rawGapMax` spikes: transport/radio/WinUSB/GATT arrival,
   or device intentionally stopped sending — investigate lower layers.
2. `raw` remains normal while `hid`/`gate` decline: HID validation, gate,
   or decoder path; inspect failures before changing ATT.
3. `session` normal but `mouse` stalls and `noMove` grows:
   pointer suppression policy or SendInput failure, not missing BLE packets.
4. `uiQueued` normal but `uiDone` stalls with large `uiQueueMax` or
   `uiHeartbeatMax`: WPF dispatcher/backlog. Investigate 1-second HID
   enumeration timer and touch-trail visual work.
5. Normal metrics but visible stall: OS mouse scheduling/paint/display;
   this instrumentation does not measure compositor frame presentation.

Preserve old working baseline and avoid making unrelated UI/button/battery
changes during touch-stall diagnosis. Always Stop/Quit, verify final link HCI
reason and USB reader cleanup, and check protected file hashes. A visual
pointer or apparent "disconnected" symptom must not be labeled BLE disconnect
unless the actual HCI event proves it.

## Validation status

Source-only instrumentation assembled on an isolated diagnostic branch.
It has not been compiled or physically tested by this author on Windows.
The existing `479/479` Release validation applies to the older production
commit, not to this diagnostic branch. Do not mark READY from this change.
