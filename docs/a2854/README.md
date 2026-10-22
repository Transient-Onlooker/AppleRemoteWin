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
| [New full SMP pair-only implementation](SMP_PAIR_ONLY_IMPLEMENTATION_2026-10-22.md) | In-memory Just Works source, controls, fault tests, compilation/hardware gate |
| [Consolidation record](CONSOLIDATION.md) | Source commit, copied-file SHA checks, licenses and Git history |

## Latest verified status

| Test/area | Status |
| --- | --- |
| A2854 Linux/BlueZ via USB/IP Intel AX200 | **Succeeded on real hardware**: buttons 0xFB and touch 0xFC |
| Windows WinRT access to HID 0x1812 | **Denied** when enumerating protected HID characteristics |
| Windows A2854 HIDClass child | **Code 10 / 0xC00000B9**; one explicitly authorized PnP child restart did **not** recover it |
| Windows internal Intel AX200 | **Healthy BTHUSB**, driver 23.160.0.9 at last check |
| Windows C# A2854 decoder and diagnostic program | **Implemented**; most recent reported local agent run: **125/125 Release tests passed** at `1653c23`, before the new full SMP source changes |
| Current privacy-address implementation | **Physical A2854 LE connection and clean local disconnect verified**, including via the USB 3.0 hub. Address type was public in tested sessions; privacy/reconnect across rotations unverified |
| ThinkPad T490s built-in Bluetooth | Owner confirmed **Intel Wireless-AC 9560**; A2854 test **not conducted** |
| **Purchased Axxen USB dongle** | **WinUSB `oem86.inf` Started; physical HCI Reset/Read Local Version OK (`0x21`).** Physical A2854 **LE Connection Complete OK** and **SMP Pairing Response OK** verified. Remote confirms **Legacy Just Works (SC=0, MITM=0, EncKey=1)**. Clean local disconnect (**0x16**). **SMP STK/LTK key exchange & GATT HID next.** |
| Working Windows mouse / media remote | **Not yet delivered** |

## Latest validation checkpoint (2026-10-22)

**Latest real hardware result:** USB 3.0 hub + external WinUSB Barrot `33FA:0001` physically connected to A2854, held the LE link for 1500 ms, and completed local HCI Disconnect with `0x16`. Next, `--smp-negotiate-only` elicited a real SMP Pairing Response with `IO=0x03` (NoInputNoOutput), `OOB=0`, `AuthReq=0x01` (Bonding, no MITM, no SC flag), 16-byte maximum key size, initiator key distribution `0x00`, responder EncKey `0x01`; disconnected locally with `0x16` at 575 ms. Local agent reported **125/125 tests passed** for the new negotiation implementation before the live run. The negotiation is real, but no Pairing Confirm/Random exchange, STK encryption, responder LTK delivery, protected bond persistence, encrypted reconnect or GATT/HID success has been observed yet. The Legacy-only request cannot establish whether A2854 supports Secure Connections under other offers.

See [SMP empirical findings](SMP_NEGOTIATION_2026-10-22.md) and [the gated full-pairing plan](SMP_PAIRING_PLAN_2026-10-22.md). **Do not run full bonding automatically**, because an incomplete or successful pairing may affect the A2854's existing Apple TV pairing, and the older `BondStore` currently serializes LTK as plaintext JSON.

## What to do next

1. **New source prepared, not yet locally built:** separate `--smp-pair-only --confirm-pairing --usb 33FA:0001`, strict A2854 security-profile validation, cancellation/timeouts, ephemeral LTK handling, explicit disconnect and fault-injection tests. See [implementation report](SMP_PAIR_ONLY_IMPLEMENTATION_2026-10-22.md).
2. Run **fresh** Windows .NET 10 Release tests and build, including fake encrypted pairing, wrong peer confirm, Pairing Failed, encryption rejection and missing-LTK cancellation. The prior **125/125** test result predates these commits.
3. The in-memory diagnostic can **alter the remote's Apple TV bond** even though no Windows key is saved. Obtain separate informed owner approval for one actual full pairing run. **Do not execute it automatically** just because compilation passes.
4. Default CLI invocation no longer silently launches the historical plaintext-bond-writing full driver. That older path now requires `--full-legacy-driver --confirm-plaintext-bond` and is **not recommended** for A2854; protected local key persistence and encrypted reconnection need separate engineering and explicit authorization.
5. GATT, HID feature unlock and microphone traffic remain outside the pairing-only test. Preserve the external Barrot WinUSB binding on the USB 3.0 hub and the built-in Intel BTHUSB adapter, Windows pairings, drivers and installed certificates.

Past reports: [Oct 21 handoff](../HANDOFF_2026-10-21_A2854.md), [chronological engineering status](../A2854_PORT_STATUS.md), [Oct 18 historical handoff](../HANDOFF_2026-10-18_A2854.md).
