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
| [Protected bond, encrypted reconnect and read-only GATT](PROTECTED_BOND_AND_GATT_2026-10-22.md) | Validated Windows software; protected save and encrypted reconnect physically verified |
| [Protected-bond hardware logs](PROTECTED_BOND_HARDWARE_2026-10-22.md) | A/B success and C connection timeout; retained bond and next read-only gate |
| [Consolidation record](CONSOLIDATION.md) | Source commit, copied-file SHA checks, licenses and Git history |

## Latest verified status

| Test/area | Status |
| --- | --- |
| A2854 Linux/BlueZ via USB/IP Intel AX200 | **Succeeded on real hardware**: buttons 0xFB and touch 0xFC |
| Windows WinRT access to HID 0x1812 | **Denied** when enumerating protected HID characteristics |
| Windows A2854 HIDClass child | **Code 10 / 0xC00000B9**; one explicitly authorized PnP child restart did **not** recover it |
| Windows internal Intel AX200 | **Healthy BTHUSB**, driver 23.160.0.9 at last check |
| Windows C# A2854 decoder and diagnostic program | **167/167 Release tests passed**, solution and Probe builds **0 warnings/errors** at `4c86fae` |
| Current privacy-address implementation | **Physical A2854 LE connection and clean local disconnect verified**, including via the USB 3.0 hub. Address type was public in tested sessions; privacy/reconnect across rotations unverified |
| ThinkPad T490s built-in Bluetooth | Owner confirmed **Intel Wireless-AC 9560**; A2854 test **not conducted** |
| **Purchased Axxen USB dongle** | **USB 3.0 hub + WinUSB `oem86.inf`**, physical A2854 Legacy Just Works pairing, STK encryption and LTK/EDIV/RAND receipt confirmed. **CurrentUser DPAPI bond save and stored-LTK encrypted reconnect verified**, both cleanly disconnected with `0x16`. C timed out before LE connection; Windows GATT HID remains unverified. |
| Working Windows mouse / media remote | **Not yet delivered** |

## Latest validated and source-prepared checkpoint (2026-10-22)

**Physical milestone confirmed:** Windows 11 with Barrot `33FA:0001` WinUSB completed A2854 Legacy Just Works pairing and CurrentUser DPAPI bond saving, with pairing at **958ms** and clean `0x16` disconnect at **989ms**. A separate `--reconnect-only` process loaded the protected bond, confirmed LTK link encryption through HCI Encryption Change and disconnected with `0x16`.

Validated source `4c86fae`: **167/167 Release tests passed**, full solution and Probe Release builds at **zero warnings/errors**. The subsequent `--gatt-inspect-only` attempt timed out during its 15-second LE connection window, before GATT discovery; cleanup HCI Reset succeeded. The owner confirmed a normal-button press after the attempt had ended. The protected bond remains present; [sanitized per-stage logs](PROTECTED_BOND_HARDWARE_2026-10-22.md) distinguish successful A/B from unverified GATT and HID input.

## What to do next

1. Preserve the existing CurrentUser DPAPI bond. Do not run a new pairing, remove bonds or replace keys to recover from the C timeout.
2. Coordinate a read-only `--gatt-inspect-only` retry with an ordinary remote-button wake at connection start. Do not use Back + Volume Up or enter pairing mode for bonded reconnect.
3. Confirm service 0x1812 and Report Reference 0x2908 mapping for 0xFB/0xFC/0xF0. This mode must not subscribe, write the 0xF0 unlock value, or capture microphone/HID payloads.
4. Only after exact handle mapping, separately implement and authorize button/touch subscriptions and feature activation. Preserve Barrot WinUSB, Intel BTHUSB, drivers and trust state.

Past reports: [Oct 21 handoff](../HANDOFF_2026-10-21_A2854.md), [chronological engineering status](../A2854_PORT_STATUS.md), [Oct 18 historical handoff](../HANDOFF_2026-10-18_A2854.md).
