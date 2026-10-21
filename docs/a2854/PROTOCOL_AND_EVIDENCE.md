# A2854 protocol evidence and limitations

The high-confidence measurements below came from one physical **Apple Siri Remote A2854 (USB-C, third generation)** on Ubuntu/BlueZ. Their source remains the exact Rust snapshot and its [protocol findings](../../reference/siri-remote-for-windows/docs/windows-port/PROTOCOL_FINDINGS.md) and [anonymized fixtures](../../reference/siri-remote-for-windows/docs/windows-port/OBSERVED_REPORT_FIXTURES.md).

| GATT/HID item | Observation |
| --- | --- |
| HID service | 0x1812 |
| Report characteristic | 0x2A4D repeated (distinguish by Report Reference descriptor 0x2908) |
| Input enable | Write byte 0xAF to Feature Report 0xF0 (required in observed Linux session) |
| 0xFB input | Two-byte little-endian button bitmap, **excluding** report ID |
| 0xFC input | Touch payload begins 0x32; 11 bytes (one slot) or 18 bytes (two slots), **excluding** report ID |
| 0xFA input | Microphone report enumerated/subscribed; audio capture/decode **not verified** |
| Battery | Service 0x180F, characteristic 0x2A19 |

## Anonymized fixture examples

| Data including report ID | Verified meaning |
| --- | --- |
| `FB 00 04` | Right button press |
| `FB 00 00` | Button release following Right |
| `FB 01 00` | TV button |
| `FB 40 00` | Back/Menu mapping |
| `FC 32 14 FC 01 C9 9E FB 51 44 01 66` | Hover, x=-311, y=-71, reported pressure=1 |
| `FC 32 36 FC 00 C9 9E FB 00 00 00 67` | Touch release |

Button mask mapping (0xFB): `0x0001 TV`, `0x0002 Volume Up`, `0x0004 Volume Down`, `0x0008 Click`, `0x0010 Power`, `0x0020 Siri`, `0x0040 Back`, `0x0080 Mute`, `0x0100 Play/Pause`, `0x0200 Up`, `0x0400 Right`, `0x0800 Down`, `0x1000 Left`.

Touch coordinates are encoded as signed 12-bit values; the 7-byte finger trailer contains contact ellipse axes, pressure-like reading and hover/orientation flags. Do **not** interpret raw pressure as force in newtons or a hover coordinate as a measured height. Some behavior has only been observed for one physical remote; full multitouch gesture semantics remain incomplete.

## Source cross-reference

- [Rust `src/decoder.rs`](../../reference/siri-remote-for-windows/src/decoder.rs): independently implemented protocol decoder from observed Linux data.
- [Rust `src/session.rs`](../../reference/siri-remote-for-windows/src/session.rs): BlueZ connection and notification flow.
- [Rust HID over BlueZ](../../reference/siri-remote-for-windows/src/bluez/hid.rs): multiple same-UUID characteristics, notify and unlock.
- [A2854 Windows HID descriptor study](../../reference/siri-remote-for-windows/docs/windows-port/HID_DESCRIPTOR_FAILURE.md): historical investigation; descriptor corruption is **hypothesis, not proven root cause**.
- [Windows WinRT raw input analysis](../../reference/siri-remote-for-windows/docs/windows-port/WINDOWS_DIRECT_RAW_INPUT_2026-10-18.md): why protected HID attributes were not available through ordinary WinRT.
- [C# decoder tests](../../tests/AppleRemote.Tests/A2854DecoderTests.cs): modern Windows target's derived fixture tests.

Capture/log provenance: the 0xFB/0xFC successful reception is **Linux**. No real A2854 0xFB/0xFC notifications have yet been captured through the Windows-native WinUSB path. Do not publish BLE addresses, raw paired advertisements containing identifiers, pairing keys, full raw ETL, or Siri microphone audio.
