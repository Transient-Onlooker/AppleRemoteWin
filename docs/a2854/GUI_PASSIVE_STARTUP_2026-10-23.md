# A2854 — passive WPF GUI smoke-test gate

**Source written on 2026-10-23.** This is a safety correction discovered after the independent Windows verifier reported three consecutive **256/256** Release-test passes at `302fdb1`, a focused **99/99** subset, full Release/WPF/Probe builds with zero warnings/errors, and no physical A2854 activity. The old intermittent gen-1 random-address test did not fail in these three runs (observed ~250ms, 48ms, 102ms). Those results predate this new GUI-startup guard and must not be counted as its validation.

## Why this gate is necessary

The pre-change existing WPF application did `MainWindow.Window_Loaded() -> MainViewModel.Start() -> RemoteViewModel.Start() -> StartSession()` unconditionally. If `A2854Mode` was persisted from a previous session, simply opening the GUI could open the Barrot HCI, establish encrypted BLE reconnection and issue CCCD writes and `0xF0 <- 0xAF` without the operator pressing Start. The device-change timer could likewise automatically restart a connected A2854 session in some cases. This is inappropriate when a separate operator approval is required for hardware GATT writes.

## Source correction (not yet built on local Windows)

- `RemoteStartupPolicy.AutoStartSession(true) == false`: only **A2854** skips startup transport initialization; original first-generation / ESP32 auto-start remains unchanged.
- `RemoteViewModel.Start()` still starts existing WPF UI timers, but with A2854 selected it **does not StartSession and does not Start the Windows BLE scanner**.
- `SettingsViewModel.OnA2854ModeChanged()` now stops WinRT BLE scanner on switching to A2854; switching back to 1st gen resumes scanner without starting a new session.
- `RemoteStartupPolicy.AllowBackgroundRestart(true) == false`: automatic ESP32 takeover, PnP adapter-change restarts and the Debug pairing callback cannot implicitly start/restart an A2854 link. Existing 1st-gen behavior remains unchanged when the flag is false.
- `RemoteViewModel.PairDevice()` rejects Windows Bluetooth pairing while A2854 is selected, even when no WinUSB adapter has yet been opened.
- New `RemoteStartupPolicyTests` verify the pure policy values and persisted-state semantics.

The app still **requires a separate deliberate Start button click** for an A2854 session; that action may use an already stored CurrentUser DPAPI LTK and send ATT writes. It must not be part of a GUI-only smoke test. A2854 mouse/keyboard injection also stays separately opt-in. Existing USB/Bluetooth drivers, bond files, internal Intel AX200 and authentication certificates are unchanged.

## Independent local verifier: software first, then separately authorized passive GUI test

1. Safely fast-forward the `feat/a2854-support` branch after preserving local work. Run full Release `dotnet test`, complete solution and existing WPF/Probe Release builds, adding `-p:AppleRemoteDriver=false -p:AppleRemoteDongle=false`. Ensure the 256 pre-existing tests and all new `RemoteStartupPolicyTests` pass without warning.
2. Review all startup/autostart entry points, especially app launch, Debug onPaired, device-change events, ESP32 takeover and click-to-pair. Report any bypass that opens `33FA:0001` while A2854 mode is selected and no one pressed Start. **Do not run hardware tests in this validation step.**
3. Only after the owner separately authorizes a **passive GUI smoke test**, launch the existing WPF app with a known, current settings state. Verify opening and visiting Settings/Remote pages, toggles and sensitivity slider, and no accidental auto-connect, key injection, DPAPI file change, pairing or write. **Do not press Start**, driver reinstall, pairing, keyboard toggle or pointer toggle. Do not delete an existing settings file; verify persisted A2854 selection safely, ideally with a controlled settings backup and explicit owner consent.
4. A later, separately authorized **A2854 live GUI** test may enable A2854 mode, press Start and observe connection/UI with pointer and keyboard injection **OFF**. Only following that successful session should a further user-approved supervised live mouse test enable pointer injection, with a low sensitivity and immediate Stop access.

### Remaining unverified

WPF runtime data bindings, passive GUI launch, physical GUI encrypted reconnection, Windows SendInput mouse input, long-duration BLE stability, and cross-process Barrot USB exclusive access. The original real 8-second `A2854.Probe` observation (33 0xFB + 360 0xFC frames; successful CCCD disable and host 0x16) remains the physical HID baseline, not a test of GUI auto-start.
