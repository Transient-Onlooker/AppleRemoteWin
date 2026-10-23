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

### Validation provenance and next gate

- **Last user-reported independently verified Windows source:** `49d5169314114445284a74fe53c0deae17e5cd96`: full Release tests **272/272 × 3**, focused **91/91**, full solution/WPF/Probe Release builds **0 warnings/0 errors**, no xUnit1031. Clean local worktree; no WPF GUI or physical device execution.
- **Later source awaiting Windows revalidation:** the main agent has corrected a pre-existing `UsbHci.TryOpenAdapter` handle leak when `new UsbHci(...)` throws in `FindPipes` or subsequent constructor setup. Both WinUSB and file handles now roll back unless construction completes. A partially started reader is aborted/joined before rollback. This is **not yet independently Windows-built or fault-injected**. Always check current HEAD; do not equate the 272/272 result with this later source.
- **Real hardware, separately verified using A2854.Probe:** DPAPI LTK encrypted reconnect, real button 0xFB **33 frames** and touch 0xFC **360 frames**, 0 malformed, CCCD disables acknowledged, HCI local disconnect `0x16`, process exit 0. Existing WPF A2854 GUI and OS mouse/media injection remain unverified.
- **Next step:** independent Windows software-only Release build/tests and static failure-path review of the **latest** USB cleanup patch, with `-p:AppleRemoteDriver=false -p:AppleRemoteDongle=false`. Then a passive GUI smoke test only with separate user approval; pressing Start/GATT writes and actual OS input synthesis require later approvals.

See the handoff for detailed commands, file map, test/risk history, and sequential authorization gates.
