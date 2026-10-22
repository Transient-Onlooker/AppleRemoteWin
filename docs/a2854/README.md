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
| [Protected bond, encrypted reconnect and read-only GATT](PROTECTED_BOND_AND_GATT_2026-10-22.md) | Validated Windows software; physical protected save, encrypted reconnect and read-only GATT verified |
| [Protected-bond hardware logs](PROTECTED_BOND_HARDWARE_2026-10-22.md) | A/B/C success, report value handles and earlier failures; retained bond and next input gate |
| [HID input probe — implementation/safety gates](HID_INPUT_DIAGNOSTIC_2026-10-22.md) | Opt-in CCCD/feature activation and button/touch capture code; Windows software validated, physical writes await approval |
| [2026-10-23 HID software validation](HID_INPUT_VALIDATION_2026-10-23.md) | 179/179 tests, clean Release builds, handle/write restrictions and notification teardown fixes |
| [2026-10-23 first HID hardware attempt](HID_INPUT_HARDWARE_2026-10-23.md) | One approved trial timed out before LE connection completion; no ATT writes or input capture, external Reset cleanup succeeded |
| [Consolidation record](CONSOLIDATION.md) | Source commit, copied-file SHA checks, licenses and Git history |

## Latest verified status

| Test/area | Status |
| --- | --- |
| A2854 Linux/BlueZ via USB/IP Intel AX200 | **Succeeded on real hardware**: buttons 0xFB and touch 0xFC |
| Windows WinRT access to HID 0x1812 | **Denied** when enumerating protected HID characteristics |
| Windows A2854 HIDClass child | **Code 10 / 0xC00000B9**; one explicitly authorized PnP child restart did **not** recover it |
| Windows internal Intel AX200 | **Healthy BTHUSB**, driver 23.160.0.9 at last check |
| Windows C# A2854 decoder and diagnostic program | **179/179 Release tests passed**, including 12 HID cases; solution and Probe builds **0 warnings/errors**. See [Oct 23 validation](HID_INPUT_VALIDATION_2026-10-23.md). |
| Current privacy-address implementation | **Physical A2854 LE connection and clean local disconnect verified**, including via the USB 3.0 hub. Address type was public in tested sessions; privacy/reconnect across rotations unverified |
| ThinkPad T490s built-in Bluetooth | Owner confirmed **Intel Wireless-AC 9560**; A2854 test **not conducted** |
| **Purchased Axxen USB dongle** | **USB 3.0 hub + WinUSB `oem86.inf`**, physical A2854 pairing, CurrentUser DPAPI bond save, stored-LTK encrypted reconnect and **read-only GATT HID metadata verified**, with clean `0x16` disconnects. Report value handles: `0xFB=0x0039`, `0xFC=0x003D`, `0xF0=0x004D`. HID input notifications remain unverified. |
| Working Windows mouse / media remote | **Not yet delivered** |

## Latest validated and source-prepared checkpoint (2026-10-22)

**Physical milestone confirmed:** Windows 11 with Barrot `33FA:0001` WinUSB completed A2854 Legacy Just Works pairing and CurrentUser DPAPI bond saving, with pairing at **958ms** and clean `0x16` disconnect at **989ms**. A separate `--reconnect-only` process loaded the protected bond, confirmed LTK link encryption through HCI Encryption Change and disconnected with `0x16`.

Validated source `4c86fae`: **167/167 Release tests passed**, full solution and Probe Release builds at **zero warnings/errors**. Following an initial C connection timeout, an explicitly approved `--gatt-inspect-only` retry with a normal-button wake succeeded: encrypted reconnect, HID service `0x1812`, eight Report References and exact `0xFB`/`0xFC`/`0xF0` value handles. It made no ATT writes or notification subscriptions and disconnected with `0x16`. The protected bond remains present; see [sanitized per-stage logs](PROTECTED_BOND_HARDWARE_2026-10-22.md). Actual Windows input reports remain unverified.

## Next gate — live button/touch inputs

The first separately approved HID trial at Release `fde9554` timed out before LE connection completion. No encryption, GATT writes or input observation occurred, and external HCI Reset cleanup succeeded. A further attempt requires fresh approval and a coordinated ordinary-button wake; see [trial log](HID_INPUT_HARDWARE_2026-10-23.md).

The HID-input source now passes **179/179 Release tests** (12 HID cases plus all 167 existing tests), with clean full solution and Probe builds. This validates software and mocks; live button/touch inputs still need a separately approved trial.

1. **Built and software-tested:** `--hid-input-only --confirm-hid-writes --usb 33FA:0001` uses the **existing DPAPI bond** and known-public-address encrypted reconnect; it will NOT SMP pair, delete or overwrite the bond.
2. It rediscovers report reference `0x2908` and exact CCCDs `0x2902`, verifies the real observed `0xFB=0x0039`, `0xFC=0x003D` and `0xF0=0x004D` **value handles before writing**, and permits only two CCCD notification enables plus one `0xF0 <- 0xAF` unlock. No microphone `0xFA` subscription/reads, Windows input synthesis or raw payload/key logging.
3. [Software validation is complete](HID_INPUT_VALIDATION_2026-10-23.md), including the first-generation, DPAPI and GATT regressions. Mandatory measured-handle checks and client-bound maps prevent writes through an unverified or reused map; partial enable and notification teardown paths were hardened.
4. After **new explicit human approval**, perform one bounded hardware trial using ordinary button wake (**not Back+VolumeUp**), observe decoded `0xFB` buttons and `0xFC` touch, attempt CCCD cleanup, and normally HCI disconnect. A lack of reports is not evidence of incorrect SMP keys if encrypted link and ATT writes succeed.
5. Only after authentic Windows-native 0xFB/0xFC events are captured, design optional mouse/media-key injection as a separate opt-in task. Keep the external Barrot WinUSB / internal Intel BTHUSB separation intact.

See [full probe implementation and hardware gate](HID_INPUT_DIAGNOSTIC_2026-10-22.md).

Past reports: [Oct 21 handoff](../HANDOFF_2026-10-21_A2854.md), [chronological engineering status](../A2854_PORT_STATUS.md), [Oct 18 historical handoff](../HANDOFF_2026-10-18_A2854.md).
