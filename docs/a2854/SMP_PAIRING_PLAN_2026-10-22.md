# A2854 native Windows — staged legacy SMP pairing and encryption plan (2026-10-22)

## Verified physical evidence and its limits

Source: [physical SMP negotiation report](SMP_NEGOTIATION_2026-10-22.md). The local agent observed a *real* A2854 Pairing Response following a Legacy-only Pairing Request (AuthReq 0x01):

- IO=NoInputNoOutput (0x03), OOB=0, AuthReq=0x01 (bonding requested, MITM=0, SC=0)
- Maximum encryption key size=16, initiator key distribution=0, responder key distribution=EncKey (0x01)
- Pairing Response reported at 545ms, and clean HCI Disconnect 0x16 at 575ms
- External Barrot 33FA:0001 on WinUSB via a USB 3.0 hub has already established and gracefully ended a *real* A2854 BLE link.

**Interpretation boundary:** The remote accepted the *feature exchange for a legacy pairing request*. This is strong evidence that Legacy Just Works pairing is a viable path, **not yet proof** that Pairing Confirm/Random, STK encryption, LTK distribution, reconnection or GATT HID will succeed. Our request did **not** set SC, so a response without SC **does not establish that the remote lacks SC support**. The reported responder EncKey bit indicates **planned** legacy LTK/EDIV/RAND distribution **if** phase 2 and encryption succeed; these keys have **not yet been received**. Existing c1/s1 spec vectors and mocked pairing tests validate arithmetic/plumbing but **do not demonstrate real A2854 protocol completion**. BLE legacy Just Works does not authenticate the peer against an active MITM.

The connector-resolved branch commit was `4e55193347c1da4ca57b39f757411af02ff69683`; do not confuse it with the mistyped `4e551932...` in a local handoff.

## Proposed code-only preparation (do not execute live pairing yet)

1. Add a distinct `--smp-pair-only --usb 33FA:0001` opt-in diagnostic; **never** run the existing default full `SiriRemoteBle.RunAsync` path for this gate. Require a distinct `--confirm-pairing` CLI flag for any live full SMP initiation. Disallow default/internal Intel adapter selection.
2. Reuse `SmpPairing.PairAsync` **after** adding caller cancellation, link-loss propagation and bounded waits to Pairing Response, Confirm, Random, Encryption Change, and responder's Encryption Information (LTK) + Central/Master Identification (EDIV/RAND). The current `PairAsync` has no cancellation parameter; its `Expect` calls can wait up to 30 seconds, and `EncryptAsync` has a separate 10-second wait.
3. Validate feature exchange against the latest physical response; reject unexpected SC/MITM requirements, invalid key size, missing responder EncKey, Pairing Failed and malformed PDUs. Never "downgrade" an offered stronger security mode or silently retry different AuthReq masks. Preserve distinct failure stages, HCI status/reason and exact command/event ordering **without logging credentials or addresses**.
4. First-stage physical success should mean **Confirm/Random match, STK link encryption succeeds, and actual responder LTK + EDIV/RAND received**. No GATT enumeration, HID report subscription, Feature 0xF0 unlock or microphone 0xFA access in this gate.
5. **Persisted bond keys must require a separate, explicit opt-in** (e.g. `--save-bond`), independent of `--confirm-pairing`. Do **not** write the existing plaintext `BondStore` JSON, a working-tree file, terminal output, crash report, GitHub artifact or cloud log with raw LTK/IRK/STK/EDIV/RAND/identity. Implement Windows user-scoped DPAPI or another locally protected secret store and strict private file ACL, with save/load/error tests, before offering persistence. If no protected store is ready, keep the keys in process memory for the single diagnostic run and clear arrays afterward; report that reconnection remains untested.
6. Always disconnect and await matching-handle HCI Disconnection Complete; use bounded external-controller-only reset as fallback and never touch the built-in Intel AX200, its driver, trust certificates, Windows paired-device list or previous Apple TV pairing metadata.
7. Warn before the first physical pairing that it **may replace or disrupt the Siri Remote's existing Apple TV bond**. Ask the human operator to verify the remote is available and authorize *one* attempt; avoid automatic retries after Pairing Failed or user cancellation.
8. Add fake-HCI tests for: good Legacy Just Works; unexpected SC response; MITM requirement; wrong confirm; SMP Pairing Failed; missing key material; invalid key size; encryption rejection; link drop during each phase; cancellation/timeout cleanup; key material never logged, persisted only under explicit consent, no GATT frames sent.
9. Run full .NET 10 Release solution + A2854.Probe builds and all unit tests on Windows, using a writable temporary source checkout if MSB3491 persists. Commit only source, tests and redacted docs. **Do not mark the physical pairing gate passed until the local agent actually runs it.**

## Gate sequence

A. Code and regression tests only.  
B. Explicit owner approval: one physical SMP full-pairing test, **in-memory key handling**, immediate local disconnect.  
C. Independent owner approval for protected local bond persistence and subsequent encrypted reconnection.  
D. Encrypted GATT HID service discovery, report-reference mapping, feature unlock and 0xFB/0xFC input capture.

Bluetooth specification references: [Core v6.3 Security Manager](https://www.bluetooth.com/wp-content/uploads/Files/Specification/HTML/Core_v6.3/out/en/host/security-manager-specification.html) and [Core v5.4 Security Manager](https://www.bluetooth.com/wp-content/uploads/Files/Specification/HTML/Core-54/out/en/host/security-manager-specification.html).
