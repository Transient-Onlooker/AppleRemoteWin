# Siri Remote A2854 — project home

> **Verified 2026-10-24:** `d1e570d` full Release **351/351**, WPF **171/171**, and **zero Settings overflow** at 1024/1280/1600px, in both A2854 and gen-1. `0d5bcc7` Release **359/359**, LocTests 10/10, all 326 en/fr/ko keys aligned, Korean auto detection and no horizontal overflow. **Remaining localization UI failures:** missing selected language after switching, old-language status/no-touch/(none) labels, English-only calibration choice names and Blockly. Follow-up source attempts fixes but **awaits Windows QA**. Physical `dd06b2f` had one ~70s encrypted HID session with CCCD cleanup/local HCI 0x16; later `71733be` had one encryption failure 0x2019/0x3E. Do not infer permanent radio stability or broken bond. Preserve the DPAPI bond and real five-point calibration. [Korean guide](../../README.ko.md).

> **Historical evidence below is preserved for traceability; do not treat the older 256/256 and 8-second trials as latest results.** Main code changes/commits belong to ChatGPT, while local Windows Codex independently tests read-only. [Engineering handoff](../HANDOFF_2026-10-23_A2854.md) contains current QA provenance and safety gates.

## Latest local validation and passive GUI launch fix (2026-10-23)

**Windows verifier at `302fdb1`:** full Release tests **256/256 passed in three consecutive runs** (focused input/USB/teardown/first-gen subset **99/99**), full solution/WPF/Probe Release builds **zero errors and warnings**, xUnit1031 absent. The older gen-1 random-address test finished in ~250ms / 48ms / 102ms in those runs; intermittent failure **not proven cured**. No actual WPF app was launched and no hardware/input injection was performed.

**New main-author code after this validation (not yet Windows built):** we found the WPF startup path could auto-connect a persisted A2854 mode. The corrected `RemoteStartupPolicy` requires **explicit Start** if A2854 mode is selected **or a dedicated Barrot `33FA:0001` is present** (read-only PnP enumeration). Neither scenario starts a Windows BLE scanner on launch. The legacy GUI path now excludes `33FA:0001` from its generic WinUSB pairing fallback, but other first-gen adapters retain existing behavior. Auto-restarts triggered by ESP32 dongle takeover and PnP events are disabled for A2854; Windows nearby-device pairing is blocked in that mode. Pure startup-policy and USB-ID tests were added. See [passive GUI test gate](GUI_PASSIVE_STARTUP_2026-10-23.md). **The previous 256/256 pass does not validate these latest changes.**

## Latest WPF integration safety fixes — new build gate (2026-10-23)

The local Windows verifier reported **229/229 tests passed**, `A2854PointerMapperTests` **11/11**, and zero warnings/errors on WPF, Probe and full solution builds at `bd1e288`. Those results predate the **main-author fixes** to USB reader-failure input release/cancellation, terminal GATT-notification ordering, mouse-up SendInput failure retention/retry, and UI/model sensitivity range. Additional same-process Start/Stop serialization and portable race/fault tests are included. **These newest commits have not been rebuilt or executed on Windows.** Codex is assigned **verification only**, with no live A2854/GATT/SendInput runs or changes to DPAPI bond, drivers or certificates. See [integration report](APP_INTEGRATION_2026-10-23.md).

## Project layout

| Location | Purpose |
| --- | --- |
| [`../../src/AppleRemote.Core/A2854Decoder.cs`](../../src/AppleRemote.Core/A2854Decoder.cs) | C# decoder from real 0xFB buttons and 0xFC touch frames |
| [`../../tools/A2854.Probe`](../../tools/A2854.Probe) | Native Windows diagnostic using a *separate* WinUSB USB Bluetooth HCI controller |
| [`../../tests/AppleRemote.Tests/A2854DecoderTests.cs`](../../tests/AppleRemote.Tests/A2854DecoderTests.cs) | Hardware-observed decode fixtures / regression tests |
| [`../../reference/siri-remote-for-windows`](../../reference/siri-remote-for-windows) | Exact, standalone historical Linux Rust and Windows diagnostic snapshot |
| [CURRENT engineering handoff, 2026-10-23](../HANDOFF_2026-10-23_A2854.md) | **Start here:** verified hardware, exact source HEAD and test provenance, risks, role division, next approved work gates |
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
| [Existing WPF app integration](APP_INTEGRATION_2026-10-23.md) | New source: A2854 DPAPI BLE session, WPF device/pointer toggles, fake-sink pointer tests; native app build still pending |
| [Protected-bond hardware logs](PROTECTED_BOND_HARDWARE_2026-10-22.md) | A/B/C success, report value handles and earlier failures; retained bond and next input gate |
| [HID input probe — implementation/safety gates](HID_INPUT_DIAGNOSTIC_2026-10-22.md) | Opt-in CCCD/feature activation and button/touch capture; actual FB/FC input verified, further writes require approval |
| [2026-10-23 HID software validation](HID_INPUT_VALIDATION_2026-10-23.md) | 179/179 tests, clean Release builds, handle/write restrictions and notification teardown fixes |
| [2026-10-23 capture assessment validation](HID_CAPTURE_ASSESSMENT_2026-10-23.md) | 218/218 tests, clean Release builds; subsequent eight-second physical trial confirmed separate capture and cleanup outcomes |
| [2026-10-23 HID hardware attempts](HID_INPUT_HARDWARE_2026-10-23.md) | Latest: 33 FB / 360 FC, both CCCD disable responses and local 0x16; earlier timeout and peer-ended 13 retained |
| [Consolidation record](CONSOLIDATION.md) | Source commit, copied-file SHA checks, licenses and Git history |

## Latest verified status

| Test/area | Status |
| --- | --- |
| A2854 Linux/BlueZ via USB/IP Intel AX200 | **Succeeded on real hardware**: buttons 0xFB and touch 0xFC |
| Windows WinRT access to HID 0x1812 | **Denied** when enumerating protected HID characteristics |
| Windows A2854 HIDClass child | **Code 10 / 0xC00000B9**; one explicitly authorized PnP child restart did **not** recover it |
| Windows internal Intel AX200 | **Healthy BTHUSB**, driver 23.160.0.9 at last check |
| Windows C# A2854 decoder and diagnostic program | **218/218 Release tests passed**, including capture, assessment and completion regressions; solution and Probe builds **0 warnings/errors**. See [assessment validation](HID_CAPTURE_ASSESSMENT_2026-10-23.md). |
| Current privacy-address implementation | **Physical A2854 LE connection and clean local disconnect verified**, including via the USB 3.0 hub. Address type was public in tested sessions; privacy/reconnect across rotations unverified |
| ThinkPad T490s built-in Bluetooth | Owner confirmed **Intel Wireless-AC 9560**; A2854 test **not conducted** |
| **Purchased Axxen USB dongle** | **USB 3.0 hub + WinUSB `oem86.inf`**: pairing, DPAPI bond, encrypted reconnect, GATT discovery and HID capture verified. Latest short trial: **33 FB / 360 FC**, both CCCD disables acknowledged and host disconnect **0x16**. Values FB `0039`, FC `003D`, F0 `004D`; CCCDs `003A`/`003E`. |
| Working Windows mouse / media remote | **Main integration code added to existing WPF app; fresh app build, live Windows input and long-term stability NOT yet verified** |

## Earlier protected-bond checkpoint (2026-10-22)

**Physical milestone confirmed:** Windows 11 with Barrot `33FA:0001` WinUSB completed A2854 Legacy Just Works pairing and CurrentUser DPAPI bond saving, with pairing at **958ms** and clean `0x16` disconnect at **989ms**. A separate `--reconnect-only` process loaded the protected bond, confirmed LTK link encryption through HCI Encryption Change and disconnected with `0x16`.

Validated source `4c86fae`: **167/167 Release tests passed**, full solution and Probe Release builds at **zero warnings/errors**. Following an initial C connection timeout, an explicitly approved `--gatt-inspect-only` retry with a normal-button wake succeeded: encrypted reconnect, HID service `0x1812`, eight Report References and exact `0xFB`/`0xFC`/`0xF0` value handles. It made no ATT writes or notification subscriptions and disconnected with `0x16`. See [sanitized per-stage logs](PROTECTED_BOND_HARDWARE_2026-10-22.md). At that checkpoint actual input had not yet been captured; the later HID result is below.

## Existing AppleRemoteWin application integration — new source gate

**New implementation, not yet built or run in the user's Windows environment:** an explicit A2854 mode in the existing Settings screen selects the separate external Barrot WinUSB `33FA:0001`, loads the **existing** CurrentUser DPAPI bond, reconnects encrypted, subscribes only to observed 0xFB/0xFC reports and activates the verified F0 feature. Inputs feed the existing `RemoteSession`/GUI/`KeyBinder`; a pure A2854 pointer mapper and Windows SendInput mouse backend add **separately opt-in** cursor movement and click. Initial device and mouse toggles default to OFF, unsafe TV/Siri/Power actions are suppressed, and first-generation/ESP/HID behavior is unchanged when A2854 mode is OFF. No SMP auto-pairing, bond deletion, microphone subscription or driver/certificate changes were added.

The previous `218/218` test result and 8-second physical CLI trial apply to **older source before this integration**. The local Windows Codex now only needs to **build/test/review** these committed changes; physical GUI connection and real SendInput require separate explicit approval. Automatic reconnect and long-duration stability remain future work. Read the [main-coded GUI integration and verifier instructions](APP_INTEGRATION_2026-10-23.md).

## Input trial history and remaining development

**Latest actual trial:** the separately approved eight-second command at `a40c25c` completed with **33 button / 360 touch frames**, zero malformed, both CCCD disable Write Responses, HCI Disconnect Command Status OK and matching Disconnection Complete **0x16**; exit **0**. See [sanitized evidence](HID_INPUT_HARDWARE_2026-10-23.md#third-approved-trial-eight-second-capture-and-normal-cleanup). Earlier preparation and trial descriptions below are historical; normal cleanup is now verified for this bounded run.

The first approved HID trial timed out before LE connection completion. A separately approved retry of the same verified Release build then encrypted with the existing LTK, rediscovered the value/CCCD handles, completed CCCD enables and F0 AF, and received **59 button / 720 touch frames with zero malformed frames**. The remote disconnected during observation with reason **13**; the process exited 1 before CCCD disables and host disconnect. This is actual Windows-native input capture, with normal cleanup and longer stability still unverified; see [trial evidence](HID_INPUT_HARDWARE_2026-10-23.md).

The HID-input source at physical experiment commit `f2ea655` passed **179/179 Release tests** (12 HID cases plus all 167 existing tests), with clean full solution and Probe builds. These tests do not establish the cause of the physical reason 13 disconnect.

**Post-experiment follow-up software-validated:** **218/218 Release tests**, solution and Probe builds at **0 warnings/errors**. The CLI reports `0x13` plus both valid FB and FC streams as an **input-capture milestone**, while independently recording individual CCCD disable responses, HCI Command Status and Disconnection Complete. Partial/no valid reports, missing responses, cancellation and unexpected termination do not become complete success. Peer/host command races and malformed responses are regression-tested. No new physical connection or write was performed; see [validation and prepared 8-second command](HID_CAPTURE_ASSESSMENT_2026-10-23.md).

1. **Built and software-tested:** `--hid-input-only --confirm-hid-writes --usb 33FA:0001` uses the **existing DPAPI bond** and known-public-address encrypted reconnect; it will NOT SMP pair, delete or overwrite the bond.
2. It rediscovers report reference `0x2908` and exact CCCDs `0x2902`, verifies the real observed `0xFB=0x0039`, `0xFC=0x003D` and `0xF0=0x004D` **value handles before writing**, and permits only two CCCD notification enables plus one `0xF0 <- 0xAF` unlock. No microphone `0xFA` subscription/reads, Windows input synthesis or raw payload/key logging.
3. [Software validation is complete](HID_INPUT_VALIDATION_2026-10-23.md), including the first-generation, DPAPI and GATT regressions. Mandatory measured-handle checks and client-bound maps prevent writes through an unverified or reused map; partial enable and notification teardown paths were hardened.
4. A further hardware trial requires **new explicit human approval**. Use ordinary button wake (**not Back+VolumeUp**), capture bounded input and confirm both CCCD disables, HCI Disconnect status and reason 16. Preserve event timing to investigate any early remote disconnect; do not auto-re-pair or try different Feature values.
5. Mouse/media-key injection and long-term reconnect/service behavior remain separate development tasks. The input milestone does not authorize running Windows input synthesis. Keep the external Barrot WinUSB / internal Intel BTHUSB separation intact.

See [full probe implementation and hardware gate](HID_INPUT_DIAGNOSTIC_2026-10-22.md).

Past reports: [Oct 21 handoff](../HANDOFF_2026-10-21_A2854.md), [chronological engineering status](../A2854_PORT_STATUS.md), [Oct 18 historical handoff](../HANDOFF_2026-10-18_A2854.md).
