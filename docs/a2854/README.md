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
| [Consolidation record](CONSOLIDATION.md) | Source commit, copied-file SHA checks, licenses and Git history |

## Latest verified status

| Test/area | Status |
| --- | --- |
| A2854 Linux/BlueZ via USB/IP Intel AX200 | **Succeeded on real hardware**: buttons 0xFB and touch 0xFC |
| Windows WinRT access to HID 0x1812 | **Denied** when enumerating protected HID characteristics |
| Windows A2854 HIDClass child | **Code 10 / 0xC00000B9**; one explicitly authorized PnP child restart did **not** recover it |
| Windows internal Intel AX200 | **Healthy BTHUSB**, driver 23.160.0.9 at last check |
| Windows C# A2854 decoder and diagnostic program | **Implemented**; most recent reported agent run at `1375d4d`: **103/103 Release tests passed**, Probe Release build succeeded |
| Current privacy-address implementation | **Previously built and scanned on real Barrot WinUSB hardware**; new connection-mask and strong-pairing-match fixes await fresh local build |
| ThinkPad T490s built-in Bluetooth | Owner confirmed **Intel Wireless-AC 9560**; A2854 test **not conducted** |
| **Purchased Axxen USB dongle** | **WinUSB `oem86.inf` Started; physical HCI Reset/Read Local Version OK (`0x21`).** Physical A2854 **LE Connection Complete OK** and **SMP Pairing Response OK** verified. Remote confirms **Legacy Just Works (SC=0, MITM=0, EncKey=1)**. Clean local disconnect (**0x16**). **SMP STK/LTK key exchange & GATT HID next.** |
| Working Windows mouse / media remote | **Not yet delivered** |

## Latest validation checkpoint (2026-10-22)

The user/local Windows agent reported **109/109 Release tests passed**, probe and solution build success at `9c98435`. On actual hardware, A2854 entered pairing advertisement mode and Barrot `33FA:0001` produced **`LE Connection Complete OK`**, the first measured Windows-native physical BLE link completion. The diagnostic subsequently logged **`Disconnection Complete OK (reason=0x3E)`**. Bluetooth SIG defines `0x3E` as **Connection Failed to be Established / Synchronization Timeout**, while an ordinary local host-initiated Disconnect should yield `0x16`; therefore the old probe's "OK" message **must not be read as proven clean local termination**. [Physical session and standards evidence](LINK_PROBE_2026-10-22.md).

After that agent run, source-only fixes in GitHub added a **1500 ms observation period** after link completion, correlated disconnect command/event stages, status and reason interpretation, and tests for `0x16` versus `0x3E`. **These edits have not been locally built or exercised on hardware.** The local agent also reported a backed-up uncommitted `Program.cs` patch. Inspect that patch and protect any local work **before** pulling the new branch.

## What to do next

1. Preserve any local `Program.cs` patch and local report data. Compare it to the new remote source; do not overwrite uncommitted files or blindly apply a saved diff.
2. Build and test the latest source in a writable Windows environment. Only report fresh tests after execution. Confirm the external Barrot remains WinUSB / Started and internal Intel remains BTHUSB / Started without changing them.
3. With the A2854 present, rerun `--connect-only --usb 33FA:0001 --seconds 30`. Wake it after the scan begins. Observe **whether 0x3E occurs during the 1500 ms idle period, before the host Disconnect, or afterward**. Exact command-status and event chronology now matter more than adding features.
4. Remain **SMP/GATT/bond-free** until the anomaly is understood. Do not proceed to experimental pairing based solely on a Connection Complete event.
5. If repeated tests show stable links and clean local disconnect, proceed to a separately authorized SMP/encryption diagnostic before GATT report reference and button/touch tests.

Past reports: [Oct 21 handoff](../HANDOFF_2026-10-21_A2854.md), [chronological engineering status](../A2854_PORT_STATUS.md), [Oct 18 historical handoff](../HANDOFF_2026-10-18_A2854.md).
