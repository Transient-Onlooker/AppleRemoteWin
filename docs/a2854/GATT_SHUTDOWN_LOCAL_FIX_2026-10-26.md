# GATT shutdown local fix and offline assessment

Base: `cb1dc8a488430df5236e61a6690e1ce0106455b7`. This document describes the follow-up changes shipped with it, not validation of the unmodified base.

## Change

- Fix three CS1503 compile errors by sending ATT replies with the validated callback's `ushort` connection handle.
- Use atomic send admission and an active-dispatch count for every GATT send. Detach closes admission without waiting for external callbacks.
- Add `DetachAsync` to await already-admitted transport callbacks. A2854 production cleanup awaits it before HCI disconnect. Probe outer-finally cleanup awaits it before fallback reset / USB disposal.
- A transport exception can mean a partially dispatched request: preserve the original exception but permanently reject later ATT requests on that bearer. Late replies cannot reopen it.

Synchronous `Detach` is deliberately not a physical transport barrier: a send admitted first may finish after it returns. `DetachAsync` is the drain barrier. Never synchronously wait on that barrier from inside the transport callback being drained. An indefinitely blocked transport callback can prevent the async barrier completing; there is no unsafe timeout that frees its native handles anyway. This change does not fix or prove recovery from an indefinitely stuck USB write.

## Remaining synchronous callers and ordering limits

- `src/AppleRemote.Core/SiriRemoteBle.cs`: the first-generation finally retains synchronous Detach. Normal request operations have already been awaited; this preserves existing behavior. It does not prove draining of a concurrent unsolicited callback before legacy transport release. If exception-path disconnect fails or its event wait expires, the next connection loop may overlap a previously admitted callback. That safety is unproven; no first-generation hardware teardown race was tested here.
- Remaining test calls deliberately exercise nonblocking/reentrant Detach or close completed fake transactions; they do not own real native handles.
- Probe's successful explicit HCI disconnect paths still run before its outer finally. The new await therefore guarantees drain before fallback reset / USB disposal, not before every successful explicit disconnect. No additional Probe/legacy lifecycle refactor is included.
- A permanently blocked native send can postpone A2854 Stop, HCI disconnect, key-memory zeroing and USB disposal indefinitely. This is a known limit, not a bounded shutdown guarantee. Never bypass it by releasing native handles still in use.

## Executed offline validation

- Final Release solution: 0 warnings, 0 errors. Probe Release: 0 warnings, 0 errors. Driver/dongle downloads disabled.
- Full tests: 479 passed, 0 failed, 0 skipped. GATT/battery/shutdown focused: 32 passed.
- New shutdown tests: 8 cases, including 600 cancel/detach/disconnect iterations and 300 send-admission/detach iterations. All tasks bounded and awaited; cancellation series checks ATT semaphore recovery and no pending request.
- Updated prior external Core-only fake HCI harness: 700/700 concurrency iterations; operation timeouts 0, state-inspection timeouts 0, callback timeouts 0, unfinished tracked tasks 0.
- Separate external behavior suite: 17/17 cases including 100 response/cancellation races. Normal path acknowledges both CCCD disables and simulates HCI 0x16; interrupted ATT path emits no later CCCD writes while HID notifications remain usable until link termination.
- Independent design review found no further concrete counter/drain defect. Admission-vs-close is stress-tested, not exhaustively proven for every scheduler interleaving. No new production-only test hook was added.

Local evidence: `AppleRemoteWin-Validation/gatt-fix-local/` (not uploaded). These runs use fake HCI delegates and the portable Core only; no actual `RemoteSession.Start`, controller initialization, BLE, SendInput, GUI, bond/calibration/settings/driver mutation.

## Assessment

Offline fix: PASS within the tested scenarios. Previous monitor stalls were not reproduced. The historical dump's underlying root cause remains unproven; removal of the monitor alone was not used as pass evidence.

Live readiness: CONDITIONAL, not final READY. The changed A2854 async cleanup integration still needs a separately approved hardware session confirming battery outcome, HID input, both CCCD responses when ATT remains healthy, real disconnect reason, USB cleanup and subsequent restart. Indefinitely blocked native send recovery is not established by this test suite.
