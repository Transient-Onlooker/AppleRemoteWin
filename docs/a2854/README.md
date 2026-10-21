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
| [Purchased BR8652A12 dongle](BARROT_BR8652A12.md) | Axxen/Barrot chipset evidence, version ambiguity and Windows USB read-only preflight |
| [Consolidation record](CONSOLIDATION.md) | Source commit, copied-file SHA checks, licenses and Git history |

## Latest verified status

| Test/area | Status |
| --- | --- |
| A2854 Linux/BlueZ via USB/IP Intel AX200 | **Succeeded on real hardware**: buttons 0xFB and touch 0xFC |
| Windows WinRT access to HID 0x1812 | **Denied** when enumerating protected HID characteristics |
| Windows A2854 HIDClass child | **Code 10 / 0xC00000B9**; one explicitly authorized PnP child restart did **not** recover it |
| Windows internal Intel AX200 | **Healthy BTHUSB**, driver 23.160.0.9 at last check |
| Windows C# A2854 decoder and diagnostic program | **Implemented**; earlier 89 tests passed and probe built before latest BLE privacy-address changes |
| Current privacy-address implementation | **Committed but post-change Windows tests not yet reported** |
| ThinkPad T490s built-in Bluetooth | Owner confirmed **Intel Wireless-AC 9560**; A2854 test **not conducted** |
| **Purchased Axxen USB dongle** | **33FA:0001 WinUSB / `oem86.inf` Started; actual HCI Reset and Read Local Version succeeded with EP0 `0x21`!** Controller reports HCI/LMP 5.0 and manufacturer 0x08E7 (Barrot). **BLE scan, pairing, A2854 input unverified.** |
| Working Windows mouse / media remote | **Not yet delivered** |

## Latest validation caveat (2026-10-22)

The native Windows **WinUSB HCI-only stage is now physically verified**, see [Barrot field record](BARROT_BR8652A12.md). The latest local agent's `dotnet run` build **failed** and `dotnet test` **hung**, but the **existing Release binary** successfully ran HCI-only. The previous 96/96 unit-test pass refers to an earlier verified run. ChatGPT has introduced a new `--scan-only` CLI path; it must be compiled locally before use.

## What to do next

1. On the owner's Windows PC, fetch the working branch **after the Barrot 0x21 EP0 patch** and run `dotnet test tests/AppleRemote.Tests -c Release` and `dotnet build tools/A2854.Probe/A2854.Probe.csproj -c Release`; report the exact result, not an assumed pass.
2. Keep the currently working external Barrot `33FA:0001` WinUSB binding intact. Resolve the build failure/test hang and compile the new `--scan-only` mode. Do not alter the internal Intel adapter.
3. Perform **LE scan only first**, using the newly added `--scan-only --usb 33FA:0001 --seconds 30`. Do not connect/pair until successful LE advertising has been confirmed.
4. Only after native Windows raw 0xFB/0xFC capture succeeds, integrate decoder routing, mouse/key mappings, reconnect and WPF UI.

Past reports: [Oct 21 handoff](../HANDOFF_2026-10-21_A2854.md), [chronological engineering status](../A2854_PORT_STATUS.md), [Oct 18 historical handoff](../HANDOFF_2026-10-18_A2854.md).
