# A2854 protocol findings — measured Linux session

**Source of evidence:** Actual A2854 event traces produced by upstream `siri-remote events` running in WSL2 Ubuntu 22.04 with Intel AX200 and BlueZ 5.64.  
**Not:** Apple's official protocol specification.

## BLE facts

- Manufacturer ID: Apple `0x004C`.
- HID service UUID: `00001812-0000-1000-8000-00805f9b34fb`.
- Battery service: `0x180F`; battery percentage characteristic: `0x2A19`.
- Bond Management service `0x181E` and an Apple vendor-specific service were also observed.
- The device advertisement's manufacturer payload suffix did **not** match every byte of the fixed example fingerprint in upstream README. Identification should verify a combination of fields rather than hard-code only one complete payload.
- Real device identity addresses/serials were deliberately omitted from this public log.

## HID report IDs observed

| ID | Purpose | Validation |
| --- | --- | --- |
| `0xFB` | Physical button bitmap | Press/release logs for directional, TV, Back, Play/Pause, Mute, volume buttons |
| `0xFC` | Capacitive circular pad contacts | XY coordinates, one active contact, hover, geometry, relative pressure-related values, release |
| `0xFA` | Microphone report | Characteristic enumerated and notifications subscribed; **audio decoding not validated in this experiment** |
| `0x60`, `0xFD`, `0xFE` | Other input reports | Enumerated; semantics **not proven** |
| `0xF0`, `0xF1` | Feature reports used for input enable | Both attempted with byte `0xAF`; second write returned ATT `0x82` while notifications still worked |

The full GATT HID channel uses multiple distinct Report characteristics sharing UUID `0x2A4D`, distinguished by Report Reference descriptors. This makes characteristic identity and descriptor inspection essential for any Windows backend.

## Touch interpretation

- `x` and `y`: 2D position relative to the pad.
- `major`, `minor`: the reported touch ellipse axes.
- `angle`: quantized contact orientation in the upstream decoder.
- `hover=true`: proximity/hover classification, **not proof of surface contact**.
- `pressure`: relative reported byte-scale measure; **not calibrated physical force**.

Example interpretation sequence observed when a finger moved away:
`hover=false` with larger reported value → `hover=true` with smaller value → `pressure=1, hover=true` → `touch released`.

**Important:** `pressure > 0` does not prove the finger is touching the surface. The remote tracks some near-surface motion, but it does not directly provide a calibrated height in millimeters. Do not advertise 3D hand tracking.

## Experimental battery and buttons

Battery was reported at 99% during the tested session; this is a **time-specific measurement**, not a fixed capability. Press/release reports for several hardware keys demonstrate input event reception without Windows HID injection.

Keep these distinctions in any future README, user interface and regression tests.
