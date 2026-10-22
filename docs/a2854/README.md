# Siri Remote A2854 — project home

> **Experimental / Windows native input NOT verified.** This index is the canonical entry point after the two repositories were consolidated. It supersedes historical handoff instructions that still suggest repeating the final internal-HID restart.

## Project layout

| Location | Purpose |
| --- | --- |
| [`../../src/AppleRemote.Core/A2854Decoder.cs`](../../src/AppleRemote.Core/A2854Decoder.cs) | C# decoder from real 0xFB buttons and 0xFC touch frames |
| [`../../tools/A2854.Probe`](../../tools/A2854.Probe) | Native Windows diagnostic using a *separate* WinUSB USB Bluetooth HCI controller |
| [`../../tests/AppleRemote.Tests/A2854DecoderTests.cs`](../../tests/AppleRemote.Tests/A2854DecoderTests.cs) | Hardware-observed decode fixtures / regression tests |
| [`../../reference/siri-remote-for-windows`](../../reference/siri-remote-for-windows) | Exact, standalone historical Linux Rust and Windows diagnostic snapshot |
| [Architecture](ARCHITECTURE.md) | Ownership and boundaries of Windows, Linux and hardware paths |
| [Protocol and evidence](PROTOCOL_AND_EVIDENCE.md) | Known A2854 reports, provenance and unresolved behavior |
| [Hardware validation](HARDWARE_VALIDATION.md) | Safe experimental order / stop conditions |
| [Purchased BR8652A12 dongle](BARROT_BR8652A12.md) | Actual Barrot WinUSB/HCI/advertisement milestones, signing/rollback notes |
| [2026-10-22 protocol research](RESEARCH_2026-10-22.md) | Bluetooth SIG/Apple/BlueZ research, connection-completion fix, pairing risks and next hardware gates |
| [2026-10-22 live LE link probe](LINK_PROBE_2026-10-22.md) | Physical A2854 LE Connection Complete success; disconnection reason 0x3E requires follow-up |
| [2026-10-22 live SMP negotiation](SMP_NEGOTIATION_2026-10-22.md) | Physical A2854 SMP Pairing Response verified: Legacy Just Works accepted |
| [Staged SMP full pairing](SMP_PAIRING_PLAN_2026-10-22.md) | Approval gates for live pairing and future protected bond persistence |
| [Full SMP pair-only implementation](SMP_PAIR_ONLY_IMPLEMENTATION_2026-10-22.md) | Physical Confirm/Random, STK encryption and LTK receipt now confirmed |
| [Protected bond, encrypted reconnect and read-only GATT](PROTECTED_BOND_AND_GATT_2026-10-22.md) | New unbuilt DPAPI and reconnect/GATT source; separate experimental approval gates |
| [Consolidation record](CONSOLIDATION.md) | Source commit, copied-file SHA checks, licenses and Git history |

## Latest verified status

| Test/area | Status |
| --- | --- |
| A2854 Linux/BlueZ via USB/IP Intel AX200 | **Succeeded on real hardware**: buttons 0xFB and touch 0xFC |
| Windows WinRT access to HID 0x1812 | **Denied** when enumerating protected HID characteristics |
| Windows A2854 HIDClass child | **Code 10 / 0xC00000B9**; one explicitly authorized PnP child restart did **not** recover it |
| Windows internal Intel AX200 | **Healthy BTHUSB**, driver 23.160.0.9 at last check |
| Windows C# A2854 decoder and diagnostic program | **Implemented**; local agent reported **148/148 Release tests passed** at `6cd4bbb`, before latest DPAPI/reconnect/GATT changes |
| Current privacy-address implementation | **Physical A2854 LE connection and clean local disconnect verified**, including via the USB 3.0 hub. Address type was public in tested sessions; privacy/reconnect across rotations unverified |
| ThinkPad T490s built-in Bluetooth | Owner confirmed **Intel Wireless-AC 9560**; A2854 test **not conducted** |
| **Purchased Axxen USB dongle** | **USB 3.0 hub + WinUSB `oem86.inf` Started**, real A2854 LE connection and clean disconnect confirmed. Real SMP Legacy Just Works Confirm/Random, STK encryption, responder LTK/EDIV/RAND receipt and `0x16` disconnect **completed**. Keys were erased; **encrypted reconnect and Windows GATT HID remain unverified**. |
| Working Windows mouse / media remote | **Not yet delivered** |

## Latest validated and source-prepared checkpoint (2026-10-22)

**Physical milestone confirmed by the owner's local agent:** on Windows 11 with Barrot `33FA:0001` WinUSB on a USB 3.0 hub, the physical A2854 completed full Legacy Just Works **Pairing Request/Response, Confirm/Random and peer Confirm validation, STK encrypted link, and responder LTK+EDIV+RAND key distribution**, then a normal local HCI Disconnect with reason **0x16**. Agent-reported timestamps were **932ms for pairing** and **963ms for disconnect**. The original mode immediately wiped the key arrays. **No protected Windows bond existed from that run, and encrypted reconnection remains unverified.**

The agent reported **148/148 Release tests passed**, full solution and Probe Release builds succeeded on the preceding `6cd4bbb` baseline. **Newest** source work adds Windows user-scoped DPAPI bond storage (separate `--save-bond` option), protected-key encrypted `--reconnect-only`, read-only `--gatt-inspect-only`, stricter GATT response validation, and mock/Windows-only storage tests. **These newest changes are unbuilt and untested in the owner's environment.** See [new gates and exact commands](PROTECTED_BOND_AND_GATT_2026-10-22.md).

## What to do next

1. Local Windows Codex agent: preserve local work, fetch latest remote branch, compile full Release and A2854.Probe, then run entire suite including new Windows-only `ProtectedBondStoreTests` and mock `GattValidationTests`. Fix test/compiler findings and commit only source/redacted documentation. No physical pairing during this review.
2. The user must **separately approve another physical pairing** after local builds pass: the previous keys were intentionally discarded and a new bond may affect existing Apple TV pairing. Add `--save-bond` only when explicitly approved. The mode refuses to silently overwrite an existing protected bond.
3. Once a protected bond is safely saved, test `--reconnect-only` using the stored public BLE address without another SMP handshake. If the device isn't advertising or encryption is refused, do not re-pair, reset Windows pairing or delete/overwrite keys automatically.
4. After encrypted reconnection, `--gatt-inspect-only` reads service 0x1812 / Report Reference 0x2908 metadata. It **does not** subscribe, write the 0xF0 unlock value, or capture any microphone or HID report payloads.
5. Button/touch 0xFB/0xFC subscriptions and feature 0xF0 unlock require separate validated handle mapping and an explicit diagnostic gate. Preserve external Barrot WinUSB, Intel BTHUSB, existing drivers and trust state.

Past reports: [Oct 21 handoff](../HANDOFF_2026-10-21_A2854.md), [chronological engineering status](../A2854_PORT_STATUS.md), [Oct 18 historical handoff](../HANDOFF_2026-10-18_A2854.md).
