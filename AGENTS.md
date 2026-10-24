# Repository instructions for coding agents

## First read — current A2854 work

**Read [docs/HANDOFF_2026-10-23_A2854.md](docs/HANDOFF_2026-10-23_A2854.md) in full before continuing the Siri Remote A2854 (USB-C) port.** Then read [docs/a2854/README.md](docs/a2854/README.md), [docs/a2854/GUI_PASSIVE_STARTUP_2026-10-23.md](docs/a2854/GUI_PASSIVE_STARTUP_2026-10-23.md), and [docs/a2854/APP_INTEGRATION_2026-10-23.md](docs/a2854/APP_INTEGRATION_2026-10-23.md). The 2026-10-18/10-21 handoffs and historical README statements are superseded where they disagree.

Repository: `Transient-Onlooker/AppleRemoteWin`. Working branch: `feat/a2854-support`. Always fetch/inspect its current HEAD rather than assuming a SHA from chat.

### Roles

- **ChatGPT main agent** owns code changes, architecture, bug fixes, commits/pushes and final documentation.
- **Local Windows Codex** independently builds/tests, performs static review, reports precise defects, and performs user-approved real-device checks. Unless the user explicitly redirects work, **do not ask the local verifier to implement the main changes**.

### Safety requirements

- Real A2854 native USB controller is **external Barrot 33FA:0001 WinUSB** on a USB 3.0 hub. Preserve `oem86.inf` and the user's healthy internal **Intel AX200 BTHUSB**; do not reinstall Bluetooth drivers, touch certificates/test-signing, or restart/change internal Bluetooth.
- **Preserve the existing CurrentUser DPAPI-protected bond** at `%LOCALAPPDATA%\AppleRemoteWin\A2854\bond.v1.dpapi`. Never print/upload keys, BLE addresses, raw SMP frames or the blob. Never silently re-pair, erase/overwrite a bond, fall back to legacy plaintext `BondStore`, or use the dedicated Barrot through gen-1 `SiriRemoteBle`.
- **Never operate the real remote, start its BLE connection, send CCCD / 0xF0 GATT writes, use Windows `SendInput`, or change pairing/drivers without explicit user authorization covering that stage.** Merely launching the WPF GUI must remain passive when A2854 is selected or a dedicated Barrot is present, pending verification.
- Do not subscribe to or record microphone Report 0xFA; only button 0xFB and touch 0xFC are in scope.
- Keep first-generation Siri Remote, ESP32, WPF behavior compatible. `Core` stays portable (.NET 10; no Windows references), with regression tests for new core behavior.

### Current validated baseline and next gate

- **2026-10-24 functional reliability follow-up (new source awaiting independent Windows QA):** the owner prioritizes successful A2854 connections over further localization polish. Source distinguishes HCI Command Status/Complete vs Encryption Change causes of `0x2019/0x3E`, recognizes the LinkEstablished→ConnectAsync pre-handle disconnect window and avoids encrypting a link already closed by the peer. Fixed-format privacy-sanitized new diagnostic categories and mock regressions. **No RF settings, driver, DPAPI bond, GATT, or SendInput changes; no evidence yet this resolves real link reliability.**

**Authoritative QA status as of 2026-10-24 (historical details remain in the handoff):**

- `d1e570d`: **351/351** Release tests, **171/171** WPF checks, build 0 warnings/errors. A2854 and gen-1 Settings overflow measured **0 DIP** at **1024/1280/1600px**. Settings width bug is **resolved**; do not repeat earlier 50/516/196 DIP failure as a current issue.
- `0d5bcc7`: **359/359** Release tests, LocTests **10/10**, all en/fr/ko 326 resource keys/format strings match, Korean auto-selection and six model/width combinations without overflow PASS. However **Korean WPF localization is PARTIAL**: empty language selection on switching, untranslated previous status/no-touch/(none), English calibration labels and Blockly. User's full independent report is local: `AppleRemoteWin-Validation/localization-0d5bcc7/QA_REPORT_KO.md`.
- **Next source correction (not yet independently built):** stable language and keypicker collections, redraw existing localized status/contact text, code-safe localized calibration landmark choices, Korean Blockly core/custom messages with workspace-preserving language reload, and refreshed docs. Validate every UI state on Windows without touching saved mappings, measured landmarks or bond. Blockly Korean message source credited with its upstream license.
- Hardware history is **mixed**: an encrypted ~70s A2854 GUI session with both CCCDs disabled and local HCI 0x16 at `dd06b2f`; a separate `71733be` physical trial failed immediately at HCI encrypt opcode 0x2019/status 0x3E. Intermittent ~30s peer 0x13 root cause is not proven solved and bond corruption was **not** demonstrated.
- **Main/local roles and security gates above remain mandatory.** Next validation is Release build/test **once**, targeted localization UI/browser tests and zero-overflow check, **no BLE/driver/SendInput/re-pairing**. Preserve real 5-point JSON and CurrentUser DPAPI bond. See `docs/HANDOFF_2026-10-23_A2854.md` for chronological QA evidence.
