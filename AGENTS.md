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

- **NEW live WPF GUI evidence at `4ffd858` (2026-10-23):** Existing DPAPI LTK encrypted connection and GATT notification enable **succeeded**, and mute press/release was received. Visual/coordinate problems remained; remote ended with **0x13** before locally initiated CCCD disable or host **0x16** could be confirmed. Local AppleRemote/Probe processes were fully stopped, settings restored, bond unchanged. See GUI validation history below and local `AppleRemoteWin-Validation/gui-4ffd858/GUI_VALIDATION.md`.
- **Subsequent main-agent UI source awaiting independent Windows test:** separate A2854 live drawing with mute, direction ring, power and side Siri status; mode switching retains first-gen rendering; A2854 touch-display projection separated from gen-1 using signed-12-bit bounds (physical pad extents not yet measured). Do not claim this new UI has been built or run.

- **Latest independently Windows-validated SOURCE HEAD:** `d9dfd10df9930cc3ad3dda76c802644f1e7a1edb` on `feat/a2854-support`. Complete Release tests **285/285 on each of three finished runs**; solution/WPF/A2854.Probe Release builds **zero warnings/zero errors**; focused USB tests **25/25**, no xUnit1031/xUnit2031. Builds used `AppleRemoteDriver=false` and `AppleRemoteDongle=false`.
- **Independent hardware-free fault injection:** **36/36 scenarios, 88/88 checks passed**. Automatic multi-adapter selection skips busy candidates; explicitly selected busy adapters remain blocked; partial reader startup and cleanup cannot reopen the same device prematurely; active-reader `WinUsb_Free` occurred **0 times**. Other adapter paths and 1st-gen/Barrot separation passed.
- **Intermittent testhost caveat:** at preceding `30f7a74`, one separate test run hung for 60 seconds and was terminated, with zero official TRX results, even though its dump showed 282 passed and normal completion in the testhost. It was **not** counted as a pass; exact cause is unconfirmed. No proven ThreadPool exhaustion or `UsbReaderShutdown` deadlock. Do not relabel or conceal this incident.
- **Real A2854 hardware baseline (earlier, CLI A2854.Probe only):** protected DPAPI bond, encrypted reconnection, button 0xFB **33 frames**, touch 0xFC **360 frames**, zero malformed, CCCDs off and HCI local disconnect 0x16. **None of these are WPF GUI runtime results.**
- **Next gate:** owner-authorized **passive WPF GUI smoke test** only: launch app and inspect settings/pages; do **not** press Start, initiate BLE/GATT, pair, toggle live input synthesis, install/change drivers, delete settings, or alter bond. Separate later consent is needed for live A2854 Start and still later Windows mouse/keyboard input. No further native-USB hardening is planned unless a concrete new defect appears.

See the handoff for detailed commands, file map, test/risk history, and sequential authorization gates.

- **New failure at `a49db62` (mock reproduction):** event reader starts, ACL reader Thread.Start fails, native abort remains blocked, and the failed Open returns before the cleanup Task can reach RemoteSession. A second Start can then reach CreateFile with the first reader still alive. The main-author patch adds a process-local, per-PnP-instance **UsbAdapterOpenGate** acquired before CreateFile; partial-start cleanup retains this lease until reader exit AND both WinUSB/file cleanup succeed. Faulted/stuck cleanup keeps the same adapter blocked, without blocking unrelated adapters. Six portable tests added. This patch **awaits independent Windows builds and mock fault injection**.

- **Independent Windows check at `30f7a74`:** 282/282 Release tests passed three times (one separate test run stalled for 60s and was stopped; root cause unknown); solution Release build had one `xUnit2031` warning, WPF/Probe clean. External USB mock harness: 27/28 scenarios passed; the partial-startup duplicate `CreateFile` was blocked, active-reader native free 0, and safe reopen after cleanup worked. **Remaining issue:** in automatic multi-adapter discovery, first busy candidate raised immediately rather than skipping to the next available adapter; explicit PnP instance selection must still fail closed.
- **Latest main-author changes awaiting Windows validation:** `UsbAdapterOpenGate.AcquireForSelection(instance, explicitlySelected)` skips busy candidates only in auto mode and throws for explicit instance selection. Three regression tests added, `xUnit2031` fixed. Mock cleanup-worker tests now release held mock readers in `finally` even if an assertion fails. A stalled full-test run remains unexplained: collect testhost logs/dumps and use blame-hang in local independent verification; do not claim the tests fix explains the stall.
