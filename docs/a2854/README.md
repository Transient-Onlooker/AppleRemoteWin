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
| **Purchased Axxen USB dongle** | **WinUSB `oem86.inf` Started; physical HCI Reset/Read Local Version OK (`0x21`).** Fresh 30-second scan: **595 events, 95 strong A2854-format packets** matching Apple 0x004C + HID 0x1812 + manufacturer header. **LE connection/pairing/GATT/Windows input remain unverified.** |
| Working Windows mouse / media remote | **Not yet delivered** |

## Latest validation checkpoint (2026-10-22)

The owner's Windows agent verified native Barrot USB HCI and a strong A2854-format advertisement fingerprint. **101/101** tests passed at the manufacturer-classifier milestone; after link-only preparation at **\`1375d4d\`**, a separate **103/103 Release run** and probe build passed. The latest \`--connect-only\` test timed out because the owner was **away and the remote did not advertise**. **No Create Connection command was sent**; this result says nothing about real link success/failure.

Subsequent ChatGPT commits, documented in [the standards-backed research note](RESEARCH_2026-10-22.md), enable LE **Enhanced Connection Complete (bit 9)** for controllers with extended advertising and require strong A2854 manufacturer-format matching in the future bonding flow. **These post-\`1375d4d\` changes are not yet compiled or hardware validated.**

## What to do next

1. Until the user has the physical remote, preserve the working Barrot WinUSB \`oem86.inf\` binding and internal Intel \`oem116.inf\`; do not run live pairing or mutate trust stores.
2. Pull latest \`feat/a2854-support\`; run \`dotnet test tests/AppleRemote.Tests -c Release\` and \`dotnet build tools/A2854.Probe/A2854.Probe.csproj -c Release\`. The local sandbox may require a writable temporary working copy for \`obj\` output. Only report newly completed tests as passing.
3. Once the remote is available, start \`--connect-only --usb 33FA:0001 --seconds 30\`, then wake the remote **after the scan starts**. Record scan, LE Create Connection, connection-completion, and disconnection stages separately. **No SMP/GATT/bond writes** during this gate.
4. After a clean link-only result and **separate approval for bonding**, investigate LE SMP/IRK privacy, encryption, GATT \`0x1812/0x2A4D/0x2908\`, unlock \`0xF0 <- 0xAF\`, then native \`0xFB\`/\`0xFC\` input. Leave microphone \`0xFA\` excluded.
5. After all experiments, test safe restoration of the external vendor driver and remove only the experiment's recorded certificate thumbprint. The cert expires **2026-11-21** but does not disappear automatically.

Past reports: [Oct 21 handoff](../HANDOFF_2026-10-21_A2854.md), [chronological engineering status](../A2854_PORT_STATUS.md), [Oct 18 historical handoff](../HANDOFF_2026-10-18_A2854.md).
