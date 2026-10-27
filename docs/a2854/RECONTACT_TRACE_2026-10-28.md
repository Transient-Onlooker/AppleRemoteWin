# A2854 contact-boundary trace

This diagnostic observes, but does not change, pointer movement or BLE rules.
It is built on the independently committed recontact tests `dc01808`.
No real A2854 connection or SendInput has been performed for this change.

## Contents and privacy

The BLE notification callback captures a monotonic arrival timestamp before
the decoder lock. The validated touch report is passed through the existing
input gate; the session records its arrival time and 16-bit sequence delta,
decoded release/hover/single/multi state, pointer reset/anchor/guard/move
outcome, and the sign of actual pointer movement requested. A `none` outcome
means the pointer was not processed, such as when pointer input is disabled.
The output preserves receiver order. It records **no raw coordinates, payload,
BLE address, bond or key**. Pointer signs describe requests to the existing
sink, not proof of OS acceptance or actual display movement.

The ring has a fixed 512-entry capacity; a copy includes only reports whose
arrival lies within the two seconds immediately before the request. If more
than 512 reports arrive in that interval, older entries are overwritten.
Old entries are pruned on the next report or copy; when completely idle they
can remain in bounded memory longer than two seconds, but cannot be exported
after the horizon. Starting a new A2854 session clears the ring.

The sequence is a device clock rather than a reliable packet counter. A
normal unsigned wrap is marked forward. `backward_or_large_gap` covers both
backward-looking values and sufficiently long gaps; it does not prove a stale
packet. Missing release cannot be inferred solely from a gap. Arrival time is
relative to the copy; a report delayed inside the pipeline can arrive earlier
than the processing of an already-copied report. Lines preserve processing
order, so their ages need not be sorted in that case.

## Later hardware procedure

Only after separate user approval, Start an existing bonded session. On the
Remote page, use **Copy recent touch trace** immediately after the observed
reversal; paste it into a local text file or support message only after review.
The last-two-second window is measured at button press, so a slow response can
miss the event. The snapshot is placed on the Windows clipboard only on
explicit button press; it is not automatically appended to logs or sent to
GitHub. Check session disconnect reason separately, then Stop/Quit and preserve
the protected bond and calibration. Compare release/hover with `trackingreset`,
first contact with `anchor`, and successive `move` signs. A sign change alone
does not establish that a physical swipe reversed or that an input was lost.

## Offline validation

Use Release builds with `AppleRemoteDriver=false` and `AppleRemoteDongle=false`.
The unit suite exercises fixed capacity, two-second snapshots, wrap, late
arrival, privacy-safe output, mapper outcomes, and 0xFC release/recontact
through a fake pointer. Windows Release solution and Probe builds succeeded
with 0 warnings and 0 errors. The full unit suite passed 515/515. A preceding
focused suite passed 92/92 before the final integrated pipeline case was added.
Runtime SendInput, GUI layout, physical link and user timing are separate
hardware/UI validation gates. The first Probe build using `--no-restore`
failed because this worktree had no assets file; the subsequent normal
restore/build succeeded.
