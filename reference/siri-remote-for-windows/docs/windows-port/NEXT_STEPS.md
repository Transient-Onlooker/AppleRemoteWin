# Windows port plan — resumed 2026-10-17

**Checkpoint:** Linux hardware I/O succeeded. Windows-native development resumed with a read-only [HID capability probe](../../tools/windows-probe/README.md) and decoder fixture tests; no native A2854 hardware I/O has been verified. The historical ESP32 IR pivot is documented in the dated handoff.

## Phase 0 — documentation [complete for archive]

- [x] Record intended Windows usability and limitations of WSL-only approach.
- [x] Record actual pairing, BLE HID access, touch/button trace and BlueZ workaround.
- [x] Define native architecture, open questions, license obligations.
- [x] Avoid public disclosure of physical BLE address and serial number.
- [ ] Independently validate all documented behavior on more than one A2854.

## Phase 1 — Windows capability proof [in progress]

- [x] Return Intel AX200 from USB/IP to Windows (`Not shared` / Windows PnP `OK` confirmed by user).
- [~] A2854 is paired in Windows, briefly displayed battery level, then disconnected; WinRT paired enumeration confirms its `DL1…` name. Physical GATT/HID access remains unknown.
- [x] Add a [paired-device WinRT GATT probe](../../tools/windows-probe/GATT_DIAGNOSTICS.md) to distinguish service visibility, `0x1812` access, and GATT errors (code only).
- [x] Native Windows WinRT paired enumeration found the remote; `from_id_async` succeeded, but uncached GATT discovery returned `UNREACHABLE`, connection stayed `DISCONNECTED` (2026-10-17). **Initial connectivity failure only, superseded by the later successful GATT session; see subsequent findings.**
- [x] Implement [read-only BLE advertisement detection](../../tools/windows-probe/ADVERTISEMENT_DIAGNOSTICS.md) and offline classification/privacy tests.
- [x] Windows advertisement scan saw HID 0x1812 once among seven BLE identities (204 matching events; identity not conclusively attributed to remote).
- [x] Next uncached GATT attempt succeeded: 7 services, connection `CONNECTED`, HID service 0x1812 found but characteristic discovery `ACCESS_DENIED`.
- [x] Add `--hid-snapshot` to compare Windows HID interfaces before and during GATT access while keeping device paths private.
- [x] Add optional pre-scan and capped retry on `UNREACHABLE` (max 3 attempts, 5-second intervals) with mock tests. Root cause remains unknown.
- [x] User ran `gatt_probe.py --index 0 --hid-snapshot --scan-before 10 --attempts 3`: first attempt `UNREACHABLE`, second `SUCCESS/CONNECTED`, 7 GATT services including `0x1812`, HID characteristic enumeration `ACCESS_DENIED`; HID interfaces 20 → 20 on both attempts. This does **not** prove Windows created no hidden/stale HID nodes.
- [x] Add a read-only [PnP driver binding diagnostic](../../tools/windows-probe/PNP_DIAGNOSTICS.md) which omits all device instance IDs, paths and MAC addresses.
- [x] Native Windows PnP inventory completed: 362 devices, 81 BLE/HID candidates; two BTHLEDEVICE HIDClass/mshidumdf nodes, one OK (problem 0), one Error (problem 10). **Remote ownership unknown.**
- [x] Implement read-only [PnP relationship correlation](../../tools/windows-probe/PNP_CORRELATION.md) via private PnP ContainerId/Parent (synthetic tests only).
- [x] Run PnP correlation locally: BLE HIDClass/mshidumdf problem-code-10 node is **descended from Siri Remote's unique DL1 Bluetooth PnP parent**; the healthy BLE HID node does not match.
- [x] Add [read-only remote-specific Code 10 details and Kernel-PnP 411 diagnostic](../../tools/windows-probe/PNP_FAILURE.md), with synthetic Windows regression tests.
- [x] Confirm on the remote's own HID child: ProblemStatus `0xC00000B9 (STATUS_COULD_NOT_INTERPRET)` and matching Kernel-PnP 411 with the same NTSTATUS (2026-10-17 12:53:25 local). This is consistent with, but does **not prove**, a HID report-descriptor parsing error.
- [x] Record [descriptor parsing hypothesis and evidence chain](HID_DESCRIPTOR_FAILURE.md), and add a Linux/BlueZ read-only HID Report Map (0x2A4B) capture and offline bounds checker with tests.
- [ ] Obtain the actual A2854 Report Map from an already-connected authorized BlueZ session, OR pursue a safe HIDCLASS trace; isolate the offending HID parser condition without changing Windows drivers.
- [x] Add Windows CI smoke job for Python imports/help and offline fixture tests — [run 38997823005 passed](https://github.com/Transient-Onlooker/siri-remote-for-windows/actions/runs/38997823005). This **does not** confirm actual HID enumeration or A2854 input access.
- [x] Implement read-only HID interface enumerator, `ReadFile` probe and sanitized 0xFB/0xFC fixture tests (code only).
- [ ] **On Windows hardware:** run enumerator and verify actual HID report reads.
- [ ] Test GATT service visibility / report reference descriptors if HID fails.
- [ ] Prove actual `0xFB` and `0xFC` button/touch receipt natively, including report enable.
- [ ] Select proven transport backend or document a clear no-go.

## Phase 2 — functionality [not started]

- [ ] Reuse decoder and add fixture tests drawn from sanitized real reports.
- [ ] Map touch XY to Win32 mouse motion; distinguish hover from actual touch.
- [ ] Map Back/TV/direction/play/volume to Win32 input/media handling.
- [ ] Add low-latency reconnect, graceful release of held keys, error reporting.
- [ ] Test an actual physical device across reboots and sleep/wake.

## Phase 3 — production [not started]

- [ ] Add tray app, pause/hotkey, mapping configuration, startup option.
- [ ] Build release binaries and CI, document system requirements and limitations.
- [ ] Respect upstream license and attribution; consider improvements upstream.

## Evidence to retain

The 2026-10-17 test showed physical BLE pairing, `0x1812` HID, active report notifications, a 99% battery reading, physical buttons and touch/hover data. It did *not* prove Windows native BLE capability or fully tested touch input injection. Treat all further work as a new validation phase.

## Avoid accidental assumptions

The host-specific USB/IP bus ID and WSL experimental BlueZ flags must not be embedded into the Windows product. The user should not need WSL, USB/IP or Linux for daily use. Do not describe the temporary audio-disabled build as a full upstream build.
