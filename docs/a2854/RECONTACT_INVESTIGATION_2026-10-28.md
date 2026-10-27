# A2854 lift/recontact reverse-cursor investigation

Base: `b9ea68e9c3a6b37f89dcbbe815e8d02c1820de7c`.
Isolated branch: `fix/a2854-recontact-boundary`.

## Findings

The reported physical symptom is not yet explained by a captured Windows
release/recontact sequence. Existing aggregate touch counters cannot identify
whether an individual release was missing, stale or delayed. No confirmed
production defect was established, so this investigation changes tests and
documentation only. It does not change the decoder, pointer thresholds,
direction, pressure policy, BLE parameters or wheel behavior.

`A2854Decoder` emits `Down=false` for an empty slot or hover. `A2854PointerMapper`
then clears tracking, subpixel remainders and click/drag anchors; the next
single contact is a new position anchor and generates no movement. The
observed Rust lift-off fixture retains a nonzero contact ellipse with zero
empirical pressure **and has the hover flag set**. It is already decoded as
non-contact. Zero pressure alone is therefore not evidence that the existing
hover classification is wrong. Raw slot parsing deliberately retains geometry
for visual/protocol inspection.

The pointer has no persistent contact ID. It consumes event order and does
not use the decoded timestamp as an independent contact boundary. The decoder
accumulates the 16-bit sequence clock with unsigned wrap; an old sequence is
not distinguished from a long interval. Consequently, loss of a release or
delivery of a stale contact can produce an opposite delta smaller than the
existing discontinuity limit. This is a conditional weakness, not proof of
the source of the user's symptom. Rejecting old-looking clocks or introducing
an idle timeout without confirming controller timing/reset behavior could
discard legitimate input and is intentionally not implemented here.

## Executed offline evidence

Five new tests in `A2854RecontactTests` use the actual 0xFC parser and decoder
through a fake pointer. They cover empty release and observed-shape hover
boundaries, unsigned sequence wrap, raw versus reversed calibrated axes,
first-frame re-anchoring, and the archived lift-off fixture. No OS input is
performed. The fixtures are synthetic geometry plus existing public protocol
examples, not the user's measured calibration.

A separate local Core-only reproducer compares identical synthetic swipes:

| Input delivery | Relative cursor outputs | Meaning |
|---|---|---|
| Complete release/recontact boundary | `(10,0), (10,0)` | No reverse movement |
| Release deliberately omitted | `(10,0), (-20,0), (10,0)` | Old anchor spans separate contacts |
| Earlier contact delivered after recontact | `(10,0), (-60,0)` | Stale coordinates accepted |

These are three expected diagnostic outcomes, not three fixed bugs. Neither
fault sequence has been observed from the user's current Windows session.
The repro source and output remain local in
`AppleRemoteWin-Validation/recontact-local/repro/` and `repro.log`.

## Wheel first-send rejection

The existing wheel branch records the physical input in `_owners` before
attempting its first action. A rejected action creates **no repeat timer**
and no accepted OS key-down ledger entry. Retaining the physical-edge marker
suppresses duplicate reports during that same failed press; release clears
it and the next distinct press may retry. Stop/remap clears it as well.
This is not a held Windows wheel key and does not require a synthetic up.
The existing fake-scheduler cases for initial rejection, duplicate reports,
next press, release, remap and repeat cancellation passed; no corrective
production change is justified by these results.

## Validation and remaining gate

Windows Release solution build: 0 warnings, 0 errors, external driver and
dongle download targets disabled. Full tests: **506/506**, failed/skipped 0.
Focused decoder/pointer/recontact/wheel tests: **63/63** (included in full
suite). The first local run of the new tests had two incorrect numeric
expectations in synthetic calibrated movement; the fixture was corrected to
use binary-exact axis normalization and the corrected suite passed. This was
a test expectation error, not the user's reverse-cursor symptom.

No GUI, actual USB/BLE, `RemoteSession.Start`, SendInput, pairing, settings or
protected profile changes were executed. Existing feature and diagnostic
worktrees were preserved.

Before changing pointer policy, separately authorized hardware diagnostics
need to establish the release/hover/contact ordering, sequence deltas and
mapper anchor-reset versus movement counts around a reported reversal.
Aggregate these transitions without storing raw coordinates, addresses,
keys or bond data. A gap/reordering hypothesis must not be labeled confirmed
until that observation exists. The physical fix and actual usability remain
unverified.
