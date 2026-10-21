# Architecture and consolidation boundaries

## Current production direction

````text
Siri Remote A2854 (BLE HID-over-GATT)
             |
   dedicated USB Bluetooth controller (WinUSB)
             |
      UsbHci → HciHost → LeCentral
             |
  SMP pairing/encryption → GATT discover/unlock/notify
             |
    0xFB / 0xFC → A2854Decoder
             |
  (future) gesture + SendInput → WPF Windows app
````

**Existing actual implementation:** `src/AppleRemote.Core/` holds portable HCI, LE, SMP and GATT and the experimental `A2854Decoder`. `src/AppleRemote.Windows/UsbHci.cs` drives a USB controller assigned to WinUSB. `tools/A2854.Probe` consumes only button (0xFB) and touch (0xFC) reports, not microphone (0xFA). `src/AppleRemote.App` / `RemotePipeline` still follow the original first-generation path; no A2854 GUI or cursor success is claimed.

**Previously successful research path:** built-in Intel AX200 detached from Windows via USB/IP → WSL2 Ubuntu 22.04 → BlueZ with `input,hog` disabled → legacy Rust CLI receives 0xFB/0xFC. This is *not* a native Windows finished product.

**Previously blocked Windows path:** regular Intel AX200 remains bound to BTHUSB; Windows WinRT detects HID service but returns `ACCESS_DENIED` for its protected Report characteristics, and the Siri Remote's HIDClass child shows Code 10 / `0xC00000B9`. A single scoped HID-child restart was tried and failed. Do **not** repeat it or casually substitute the old Gen-1 KMDF filter.

## Why the Linux implementation is kept separately

- The legacy repo is a full Linux CLI built around BlueZ D-Bus, PipeWire and optional gamepad/TUI. It should remain runnable **from its own directory** instead of becoming a Windows build dependency.
- The copied snapshot preserves source code, lockfile, flake, all research documents and diagnostic utilities under `reference/siri-remote-for-windows/`.
- No automatic C#/Rust source merge was made; original first-generation code remains intact, and GPL-3.0 source provenance remains visible.
- GitHub workflow files in the nested snapshot are **historical and inert**, not top-level CI. Do not move them to `.github/workflows/` without adapting triggers and OS dependencies.

## Architectural risks that still need real observations

1. A2854 advertises under rotating BLE addresses. The experimental code propagates random/public address type through HCI Create Connection and legacy SMP confirm, but A2854 security/privacy behavior is not yet validated.
2. Current discovery predicate expects Apple manufacturer data and HID service in one AD payload; advertisement and scan-response fragmentation may require merging without losing address type.
3. BLE legacy Just Works/LTK assumptions and address rotation may cause reconnection failure; a stable identity mapping is not yet validated.
4. HID Report characteristics share UUID 0x2A4D. Distinguish them using descriptor 0x2908, enable 0xF0 with 0xAF, and subscribe only to intended input IDs.
5. A2854 reports may not use the Gen-1 timing/gesture calibrations. End-to-end mouse control must be tuned after hardware success.

See [protocol evidence](PROTOCOL_AND_EVIDENCE.md) and [hardware plan](HARDWARE_VALIDATION.md).
