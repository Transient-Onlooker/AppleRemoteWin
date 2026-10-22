# A2854 full Legacy SMP diagnostic — source-only implementation (2026-10-22)

**Status: committed source + mock tests; NOT YET COMPILED or run with the physical A2854 after these changes.** The real device's Pairing Response (AuthReq 0x01, SC=0, MITM=0, EncKey 0x01, 16-byte key) was verified in the previous [live negotiation report](SMP_NEGOTIATION_2026-10-22.md). That supports a Legacy Just Works candidate but does **not** prove successful Confirm/Random, STK encryption or LTK exchange.

## New diagnostic

`--smp-pair-only --usb 33FA:0001 --confirm-pairing --seconds 30` is an **explicit opt-in** for a SINGLE full physical pairing. **Do not run on hardware without a separate informed owner approval.** The remote may replace or disrupt its bond with an existing Apple TV, even though no Windows bond file is saved. If this diagnostic succeeds, the remote may need to be paired again for use with Apple TV.

- `--smp-pair-only` is refused without both `--usb 33FA:0001` and `--confirm-pairing`. Ordinary link and negotiation diagnostic modes remain independent.
- Strong Apple 0x004C / HID 0x1812 / known A2854 manufacturer header required, then native LE connection.
- 500ms unpaired link observation; one **45-second overall** SMP session window, alongside existing 30-second per-response waits and bounded LE encryption-event wait.
- Uses physical A2854's reported Legacy Just Works feature profile. Refuses SC/MITM, OOB, changed IO capabilities, mismatched key distribution, missing bonding/EncKey and malformed response rather than downgrading silently.
- Executes Pairing Request / Response / Confirm / Random, constant-time peer Confirm verification, HCI LE Enable Encryption with STK, then responder Encryption Information (LTK) + Central Identification (EDIV/Rand). Reports **only non-secret stage names**, not addresses, random values, confirm data, keys, raw ACL frames or identity.
- Does **not** create GATT clients, enable HID, unlock Apple feature 0xF0, read the microphone, or access BondStore.
- Returned LTK/Rand only exist in volatile process memory and are zeroed on exit (best effort; runtime/transport may have copies). **No file is created; encrypted reconnection is not established by this diagnostic.**
- Attempts matching-handle HCI Disconnect / Disconnection Complete and expects local reason 0x16. If cancellation/error leaves a link possibly alive, the existing last-resort HCI Reset applies **only to the dedicated external WinUSB Barrot adapter**.
- The historical implicit full driver was **disabled as the default invocation**. To opt into that old path explicitly requires `--full-legacy-driver --confirm-plaintext-bond`. **Do not use that legacy mode for this A2854 gate**: it stores LTKs in plaintext JSON. Future persistent pairing needs an independently tested Windows user-protected secret store and a distinct `--save-bond` approval flag.

## Regression coverage prepared

- Fake physical-style remote: successful Confirm/Random, STK encryption, LTK / EDIV / Rand delivery; no ATT/GATT writes.
- A2854 exact security profile accepted and changed SC/MITM/IO/OOB/key-distribution/bonding flags rejected.
- Key size outside 7–16 rejected.
- Cancellation while awaiting Pairing Response exits without proceeding to Confirm or HCI encryption.
- Pairing Failed and intentionally incorrect Confirm are rejected before encryption.
- The shared `SmpPairing.PairAsync` keeps the older general device compatibility path; A2854 strict checks are enabled only by the new separate diagnostic. Prior tests and gen-1 code paths remain available.

**Validation boundary:** No .NET SDK is available to the assistant's code-editing environment. The previous 125/125 reported test result applied to `1653c23` **before** these changes. Run fresh full Release tests and build in the owner's Windows environment and inspect compile/runtime errors before approving a live run.

## Next steps for local Windows agent

1. Fetch latest `feat/a2854-support`. If there are local modifications or private experiments, preserve them first and review diffs.
2. Use `dotnet test tests/AppleRemote.Tests -c Release` and `dotnet build tools/A2854.Probe/A2854.Probe.csproj -c Release`. Validate all new tests, including negative cases; correct build/runtime regressions and commit fixes.
3. Keep Barrot `33FA:0001` on its working **USB 3.0 hub** with `oem86.inf` WinUSB binding; built-in Intel AX200 remains unchanged. Read-only verification only.
4. **STOP BEFORE LIVE FULL PAIRING** and ask for explicit user approval. Explain Apple TV pairing disruption and loss of reconnection ability if LTK is discarded. Do not auto-run under `--no-build` without first proving binaries include new code. Never log sensitive material.
5. If the operator approves, execute **once** under supervision with the external adapter only and wake the remote after the scan starts. Report exact non-secret protocol stage, HCI status, disconnect reason and presence/absence of complete encrypted LTK exchange. Do not auto-retry with altered pairing flags.
6. Subsequent key retention / encrypted reconnect and GATT/HID input capture are separate gates, not part of this initial physical pairing.

Bluetooth reference: [Bluetooth SIG Core SMP Specification](https://www.bluetooth.com/wp-content/uploads/Files/Specification/HTML/Core_v6.3/out/en/host/security-manager-specification.html): feature exchange, STK derivation, link encryption before LTK distribution, central identification; legacy Just Works is unauthenticated against an active MITM.

## Further security review before first hardware execution

The new diagnostic already implements a strict physical-response profile check (`AuthReq=0x01`, max key length 16, IO 0x03, no OOB, initiator key distribution 0 and responder EncKey 0x01), bounded abort paths, and no bond storage. Source review additionally identified two secret-buffer lifetime cases:

- If `Encryption Information` (LTK) arrived but `Master Identification` (EDIV/RAND) did not, the original received LTK buffer previously remained reachable after cancellation. Both SMP receive buffers are now retained for **unconditional clearing in `finally`**, even after mid-distribution failure.
- If the non-secret `Complete` progress callback throws, the caller must not receive partially constructed unmanaged copies of its bond secret. Returned key-array allocations now happen **after** the completion callback succeeds.

The fake controller now supports a **LTK-only distribution fault**. Unit tests assert the underlying received LTK PDU was cleared when the second message was missing and cancellation occurred. Another test injects a throwing completion callback and asserts both received SMP key PDUs were erased. These checks establish managed-buffer cleanup in those mocked scenarios, **not total process-memory erasure** (copies may still exist in USB transport and runtime allocations).

**Build verification boundary:** All code and tests have been pushed to the feature branch, but this assistant runtime does not have a .NET SDK and GitHub's workflow-run lookup returned no usable build run. The existing `125/125` pass belongs to the earlier negotiation-only source. Do **not** claim the new full-pair-only code or these security regressions passed until the local Windows agent runs `dotnet test tests/AppleRemote.Tests -c Release` and `dotnet build tools/A2854.Probe/A2854.Probe.csproj -c Release`.

**Physical authorization:** Before any real `--smp-pair-only --confirm-pairing` attempt, the owner should explicitly acknowledge that the device may change its Apple TV pairing. This experiment intentionally erases the Windows-side LTK, so even a successful encrypted pairing will **not** enable encrypted reconnection until a separately consented, OS-protected bond store is implemented. No SMP, HID or driver operation was executed in this source-only review.
