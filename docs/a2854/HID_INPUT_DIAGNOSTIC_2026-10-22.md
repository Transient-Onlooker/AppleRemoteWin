# A2854 Windows-native HID input diagnostic — implementation and safety gates

**2026-10-22: source and mock tests committed; actual Windows-side 0xFB/0xFC notifications not yet captured.**
The latest physical result recorded at commit `50916bc` demonstrates successful DPAPI-protected LTK encrypted reconnection, GATT HID service `0x1812` and Report Reference `0x2908` discovery on Windows 11 with external WinUSB Barrot `33FA:0001` connected through a USB 3.0 hub. The physical A2854 advertised:

| HID Report Reference | Physically observed characteristic **value handle** |
| --- | --- |
| Button input `0xFB` | `0x0039` |
| Touch input `0xFC` | `0x003D` |
| Feature `0xF0` | `0x004D` |

The old read-only probe **did not determine CCCD handle locations**. The remote was disconnected normally with local HCI reason `0x16`, and the DPAPI bond was retained. This milestone confirms encrypted GATT **metadata**, NOT live input notifications.

## New opt-in code (requires fresh Windows build/tests)

A separate `--hid-input-only --confirm-hid-writes --usb 33FA:0001` diagnostic performs the following:

1. Load an existing **CurrentUser DPAPI-protected bond** only; do NOT send SMP Pairing Request, overwrite/delete key storage or fall back to plaintext JSON.
2. Create an LE link to the previously verified public BLE address and confirm LTK-based HCI Encryption Change.
3. Rediscover HID service `0x1812`, its `0x2A4D` Report characteristics and the exact `0x2908` Report Reference metadata, without guessing CCCD offsets.
4. Identify exactly one input `0xFB`, one input `0xFC`, and one writeable feature `0xF0`. Their **value handles must match actual hardware report `0x0039`, `0x003D`, `0x004D`** in this first activation gate. If not, abort **before any ATT write**. For the two input characteristics, discover exactly one associated `0x2902` CCCD each and require Notify properties. Reject missing, duplicate or malformed report metadata.
5. **Only with explicit `--confirm-hid-writes`:** write `01 00` to the discovered `0xFB` and `0xFC` CCCDs (enable notifications). Then send **one** `0xAF` byte to the verified `0xF0` feature value handle. These are the only intended attribute writes. Do **not** subscribe to microphone Report ID `0xFA`, other report IDs, or the battery sensor, and do not read microphone audio.
6. Listen a bounded interval (default 30 seconds) and decode **only** notifications arriving on the two mapped value handles, using existing `A2854Decoder`. Log button names and a capped sample of touch coordinates/hover states; no raw HID payloads, BLE addresses, LTK, IRK, STK, RAND or secret storage. Do **not** inject mouse or media-key inputs into Windows yet.
7. Attempt best-effort write `00 00` to the two explicitly mapped CCCDs to unsubscribe, if link remains established. Await HCI Disconnect / Disconnection Complete; if link recovery is necessary reset **only** the external Barrot controller. Leave protected bond, Intel AX200/BTHUSB, Windows pairing data, drivers and certificates untouched.

**Important:** writing F0 to activate input may temporarily affect the remote's behavior, even if no new pairing occurs. Do not run the experimental feature write without user confirmation. The F0 unlock has been observed on Linux, **not yet physically verified on the Windows WinUSB path**. The diagnostic cannot promise to restore any undocumented remote feature state other than writing the two CCCDs back to 0, best effort.

## Fake-HCI regression tests

`tests/AppleRemote.Tests/A2854HidCaptureTests.cs` exercises:
- Dynamic report discovery and CCCD mapping using a dedicated A2854-style fake HID layout, leaving legacy Siri Remote fake fixtures unchanged.
- Precisely **two CCCD enable writes** and **one F0 AF feature write**, then only **two CCCD disable writes**.
- Fake `0xFB` button press/release and `0xFC` touch payload delivered through ATT notifications and decoded by `A2854Decoder`.
- An unrelated/unknown notification is dropped, with no raw data forwarding. The diagnostic contains no microphone subscription or audio decoding path.
- Live physical-handle mismatch causes **zero writes**; missing `0xFB` prevents any subscription/unlock.

**Validation boundary:** Previous **167/167 Release tests passed** at `4c86fae`, before this new HID code. The newest tests and code **have not been built or run in the user's native Windows environment**. Do not claim live Windows input success yet.

## Local agent steps

1. Preserve local edits and safely pull the latest `feat/a2854-support` branch.
2. Run a fresh Release build and full .NET tests, including `A2854HidCaptureTests`, existing `GattValidationTests`, `ProtectedBondStoreTests`, A2854 decoder, and all first-generation regressions. Fix and commit errors. Check the transaction log contains only permitted writes, and the CLI rejects missing `--confirm-hid-writes`, no bond file, and wrong external USB ID.
3. Before physically issuing ATT writes, obtain the remote owner's **new explicit approval**. There is a protected LTK bond already: do not re-pair, regenerate, delete or overwrite it. Confirm only Barrot `33FA:0001` is selected and the owner can interact with the remote.
4. If authorized, run **one** bounded hardware trial:
   ```powershell
   dotnet run -c Release --project tools/A2854.Probe/A2854.Probe.csproj -- --usb 33FA:0001 --hid-input-only --confirm-hid-writes --seconds 30
   ```
   Use an **ordinary remote button press** to wake it; do not hold Back+VolumeUp to re-enter pairing mode.
5. Observe button and touch events separately; if only one report arrives, record it as a partial success. If no events arrive, document encrypted reconnect, dynamic report/CCCD handles, Write Responses, F0 unlock result and disconnect reason, then stop. Do not silently change report IDs or retry different feature payloads.
6. Once both reports are confirmed on real Windows-native USB HCI, plan a distinct user-authorized Windows pointer/media key implementation and long-term service design. Microphone remains out of scope.

## Sources

- [Actual A2854 protocol captures and report decoder](PROTOCOL_AND_EVIDENCE.md)
- [Physical Windows encrypted GATT discovery](PROTECTED_BOND_AND_GATT_2026-10-22.md)
- [Bluetooth SIG GATT and ATT specifications](https://www.bluetooth.com/specifications/specs/)
