# Windows-native port architecture — proposal

**Status:** Proposed; Windows HID/GATT feasibility unverified

## Architecture boundary

```text
A2854 BLE remote
      |
Windows built-in Bluetooth pairing / native BLE stack
      |
  +---+----------------------------+
  |                                |
HID device interfaces             WinRT GATT fallback
(if reports exposed)              (if HID does not provide them)
  |                                |
  +---------- selected backend ----+
              |
    normalized typed DeviceEvent
    (reuse tested Rust decoder)
              |
    configurable input mapper
              |
 Windows SendInput / media keys
              |
 cursor / click / shortcuts / media
```

The prototype that used WSL2 with a Windows loopback bridge is **not** the intended production data path. No permanently disabled Windows Bluetooth, no Linux host, no usbipd at runtime.

## Feasibility gate before implementation

1. Restore the real Bluetooth USB adapter to Windows.
2. Pair A2854 using Windows Bluetooth UI and record the exact outcome.
3. Enumerate PnP/HID device interfaces and check access to input reports. Read actual Report IDs if available, not just existence of a device icon.
4. If HID reports are not accessible, inspect WinRT GATT service/characteristics, Report Reference descriptors, and whether `StartNotify` and required feature/output writes succeed.
5. If neither backend exposes the needed 0xFB / 0xFC data, **stop and document the blocker** before designing unsupported workarounds.

Windows may reserve or hide HID-over-GATT characteristics; a device pairing successfully is not proof that userspace can read raw reports.

## Reuse vs replace

| Component | Decision |
| --- | --- |
| `decoder.rs` / protocol definitions | Reuse with tests and attribution where OS-independent |
| Linux BlueZ/D-Bus service discovery | Do not carry into Windows runtime |
| Linux PipeWire/Opus microphone output | Out of Windows MVP scope |
| Linux `/dev/uinput` gamepad output | Out of Windows MVP scope |
| Windows Bluetooth adapter | Native user-mode Windows API |
| Input injection | Windows `SendInput`, proper key-release cleanup |
| User experience | Tray app, reconnect, configurable mapping, battery, kill switch |

## Safety and privacy

- No default microphone recording, raw input uploads, background telemetry, or secret cloud API.
- One-click suspension / exit; release synthesized held inputs on disconnection.
- Do not modify Windows Bluetooth security policy or require disabling driver protections for MVP.
- Pair/unpair devices only after explicit user action.
- Preserve GPL-3.0 obligations and upstream attributions in redistributed binaries.

## Compatibility

Target modern Windows 10/11 desktop systems (actual minimum version TBD by testing). Use Rust conditional build features as needed during transition; the planned product is Windows-only, not cross-platform.
