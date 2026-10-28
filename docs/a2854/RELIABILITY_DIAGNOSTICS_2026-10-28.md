# A2854 reliability and input diagnostics

This isolated branch builds on `4d6d839`. It includes the local cleanup-state and
battery-inspection work from the feature worktree without altering that worktree.
It also carries the earlier touch-stall aggregate counters. The source worktrees
and the local protected bond and measured calibration remain separate.

## Behavior

* Recent touch records are bounded to 512 entries and a two-second copy window.
  Their monotonic clock is relative to the current A2854 session. The five-second
  aggregate includes elapsed and actual window durations so it can be compared
  with the connection timeline. The last 12 summaries are kept only in memory
  and included in explicit copies. Counters are approximate across threads.
* When a user explicitly enables incident capture, the first rejected Windows
  pointer movement or backward-looking/large-gap touch clock preserves up to
  two seconds before and one second after the event, capped at 768 records.
  An incident remains in memory until explicitly cleared or the next session.
  The clock gap is not proof of an old frame or user-visible malfunction.
* Output contains contact category, decoded processing outcome, relative
  timing, sequence delta, movement signs and accepted/rejected/unknown status.
  It omits touch coordinates, raw frames, BLE addresses, bond data and keys.
  A copy occurs only on explicit UI action; no trace is sent or saved by itself.
  Acceptance means `SendInput` reported success, not proof that an app acted.
* The mouse movement calculations, click thresholds and BLE parameters are
  unchanged. Diagnostic counters distinguish attempted, accepted, rejected,
  faulted and unknown mouse operations without replaying rejected movement.
* USB cleanup pending, success and failure are distinct. A failed or pending
  cleanup blocks opening that controller again. Native sends can still block
  indefinitely; the app must not force-release handles used by a live sender.
* A2854 suspend or Windows session lock requests a stop on the UI dispatcher.
  Duplicate stop requests coalesce; resume/unlock never starts a BLE connection.
  OS suspension can occur before the dispatcher runs, so timely execution and
  final device cleanup still require physical testing.
* Settings are written to a same-directory temporary file and replaced with a
  previous-version backup. Failure is shown in Settings. An unreadable original
  file disables saving for that run to avoid overwriting it with defaults.

## Validation and limits

Windows .NET 10 Release solution and A2854.Probe builds completed with driver
and dongle downloads disabled, 0 warnings and 0 errors. The full offline suite
passed 560/560, including fake movement result, incident retention and privacy,
settings failure, cleanup policy, power-stop coordination, and original input
regressions. No GUI, live Bluetooth, OS `SendInput`, bond, pairing, calibration,
driver or certificate operations were performed for this change.

A local synthetic single-thread measurement of 100,000 pre-created touch events
using a fake pointer took 33.92 ms for the mapper alone, 42.21 ms with the
aggregate and two-second ring, and 36.94 ms with incident capture armed.
Per-thread allocations after setup were 3704, 920 and 832 bytes respectively.
These are one-run microbenchmarks affected by JIT and scheduling, not a claim
about BLE, UI latency, process-wide memory or real-device performance.

The intermittent reverse movement remains unexplained on physical hardware.
Gap and rejected-input triggers can be benign and do not capture every possible
reversal. Snapshot copying on a quiet Windows desktop with a separately approved
live test is needed; ask before BLE connection or Windows input injection. The
previous unrelated user changes in `feat/a2854-support` must not be overwritten.
