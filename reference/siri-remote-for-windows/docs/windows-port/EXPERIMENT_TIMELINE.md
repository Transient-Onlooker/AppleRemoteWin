# Hardware experiment timeline — 2026-10-17

This is a concise **observed vs. attempted** account, not a claim that a native Windows application exists. Times are from an interactive 2026-10-17 session. Personal identifiers are redacted.

| Step | Attempt | Result |
| --- | --- | --- |
| 1 | Scan for the 3rd-gen remote with Windows BLE tools | Observed Apple manufacturer data, nearby signal and HID service UUID `0x1812`; this did not prove input access on Windows |
| 2 | Pass Intel AX200 USB Bluetooth (VID:PID `8087:0029`) from Windows to WSL2 Ubuntu 22.04 via `usbipd` | Bluetooth adapter appeared under Linux `lsusb`, and BlueZ scanning worked |
| 3 | Build upstream Rust CLI under Ubuntu 22.04 | Initial build failed because system PipeWire/libspa headers were incompatible with the selected crates; a temporary audio-disabled development build succeeded |
| 4 | Scan and pair Siri Remote in BLE pairing mode | First command printed “Connected and paired”, followed by a bond-verification error; follow-up `bluetoothctl info` confirmed `Paired: yes`, `Trusted: yes`, and `Connected: yes` |
| 5 | Run `events --address <remote-address>` | Read battery/device metadata, but **HID service was absent** from the exposed GATT tree; BlueZ journal showed `input-hog profile accept failed` |
| 6 | Restart BlueZ with its `input,hog` auto-HID plugins disabled | Exposed `0x1812` GATT HID service and 8 per-report characteristics; input notifications started working |
| 7 | Interact with touch surface and hardware buttons | Received 0xFC position/hover/ellipse/pressure-related data and 0xFB pressed/released button events; battery read **99%** at test time |
| 8 | Try `view` dashboard | Suggested and later invoked while an `events` process was still producing output; dashboard's successful launch was **not independently proven by provided logs** |
| 9 | Discuss “usable on Windows” | WSL-only Linux uinput gamepad was explicitly rejected as not useful for the Windows goal; decided on native Windows fork |
| 10 | Draft documentation and Windows feasibility approach | Plans created; **native Windows integration not tested** |

## Representative device success output

On the successful session:
- `services: 6 chars: 20 descriptors: 6`
- `Service 00001812-0000-1000-8000-00805f9b34fb`
- Input report IDs: `0x60`, `0xFA`, `0xFB`, `0xFC`, `0xFD`, `0xFE`
- Input-enable feature report ID `0xF0` accepted; `0xF1` write produced ATT error `0x82`, but live events were still received
- `0xFB` button press/release examples included Right, Down, TV, Back, Play/Pause, Mute, Volume Down and Volume Up
- `0xFC` touch events reported positions, ellipse geometry, hover and release

## Critical interpretation

The early “not bonded” program diagnostic did **not** establish an actual bond failure: `bluetoothctl info` later showed `Paired: yes` and `Connected: yes`. The precise cause of that incorrect/contradictory check has not been definitively isolated. In particular, the BlueZ version alone should not be assumed to explain it.

The experiment demonstrates successful **Linux userspace access to the physical remote** when BlueZ HID plugin interference is bypassed. Nothing here establishes that Windows native HID or GATT APIs grant equivalent access.
