# Product brief — Windows native Siri Remote

**Documented:** 2026-10-17  
**Status:** Design direction; implementation paused

## Original user goal

Use a third-generation Apple TV Siri Remote (A2854) as a practical **Windows PC remote**:
1. Move the Windows mouse using the circular touch surface.
2. Use the physical buttons for click, navigation, escape/back, media play/pause, and volume.
3. Pair using normal Windows Bluetooth and reconnect without manual console commands.
4. Optionally provide a tray app for mapping, enable/disable, startup, battery status, and debugging.

### Non-goals for the finished app

- No requirement to keep working Linux-only CLI commands inside this Windows-focused fork.
- No dependency on Windows Subsystem for Linux, `usbipd`, BlueZ, D-Bus, PipeWire, or `/dev/uinput` in everyday usage.
- No requirement to maintain a Windows/Linux cross-platform build in this fork.
- No kernel-mode driver unless separate feasibility work demonstrates a compelling necessity, including explicit safety and installation costs.
- Microphone streaming and gaming-controller emulation are later optional investigations, not requirements for MVP.

## What should be reused from upstream

Keep the original project as upstream reference and retain the proven **protocol definitions and decoding algorithms**: 0xFB button events, 0xFC touch contacts, battery/power data, report identifiers, and candidate device identification. Keep relevant unit tests and copyright notices.

The transport, HID subscription, pairing workflow and OS input generation need new Windows implementations. Do not simply rename the Linux binary and advertise it as a native Windows app.

## License

The fork retains upstream GPL-3.0 licensing. Preserve LICENSE, original author attribution, and license notices when moving code. This is a downstream planning document, not a license change.

## Known current state

The fork was created from an existing Linux upstream. No Windows functional build, pairing validation, or HID report access validation has been completed. A standalone experimental WSL-to-Windows bridge was drafted previously, but **that bridge is not the Windows-native product**, and it was not verified running on the actual Windows host.
