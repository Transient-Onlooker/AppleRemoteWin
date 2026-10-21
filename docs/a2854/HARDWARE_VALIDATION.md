# A2854 Windows hardware validation plan

## Gate 0 — software-only validation (no device modifications)

On the owner's Windows machine, update `feat/a2854-support`, preserving local edits, then:

```powershell
dotnet --version
dotnet test tests/AppleRemote.Tests -c Release
dotnet build tools/A2854.Probe/A2854.Probe.csproj -c Release
```

The earlier 89/89 tests and probe build passed **before** the latest privacy-address code changes. Report the *new* results separately. Do not infer actual BLE hardware success from compilation or fake-controller tests.

## Gate 1 — determine controller suitability

- **Built-in Intel AX200** (original experiment): BTHUSB healthy; protected HID access denied; one targeted HID child PnP restart did not clear Code 10. Do not repeat this test, replace the internal driver, enable test-signing or install a Gen-1 kernel filter speculatively.
- **ThinkPad T490s**: user reports Intel Wireless-AC **9560**, but no A2854 Windows HID result yet. If testing, begin with read-only PnP/WinRT/HID checks and preserve the stock driver.
- **External USB Bluetooth adapter**: inspect actual VID/PID, USB interface class `E0/01/01`, endpoints, chipset/revision and stock driver. Back up the exact original driver and plan to restore it before any *external-device-only* WinUSB change.

| External adapter candidate | Proven evidence | Unknown |
| --- | --- | --- |
| TP-Link UB500 Realtek family, specific USB `2357:0604` | [Upstream PR #15](https://github.com/AnthoDingo/AppleRemoteWin/pull/15) reports Gen-1 direct-HCI testing | Other revisions; A2854 pairing and notification |
| Daiso Bluetooth 5.0 (product 1035660), reported RTL8761BUV | Realtek family; plausible firmware-code reuse | Actual unit VID/PID, native HCI operation, A2854 |
| **PURCHASED: Daiso Axxen Bluetooth 5.3, reported BR8652A12** | USB **33FA:0001 rev 8891** confirmed, **E0/01/01** class, Barrot driver `oem80.inf` (`brbtusb.inf`) version 21.46.25.278 | **Stock driver Code 31 / 0xC0000001**; endpoints and WinUSB/HCI/A2854 untested. [Full 33FA:0001 notes](BARROT_BR8652A12.md) |

A Wi-Fi-only USB dongle is not a Bluetooth HCI controller. Bluetooth 5.3 branding alone does not establish suitability.

**Current priority:** investigate the purchased **33FA:0001** Code 31 and export `oem80.inf` before considering a reversible **external-only** WinUSB binding. The discontinued Realtek Daiso option is no longer available to the owner; do not recommend repurchasing it as though it were the current test device. See the [BR8652A12 evidence and preflight](BARROT_BR8652A12.md).

Under a new external WinUSB HCI binding, the updated diagnostic accepts `--usb 33FA:0001` to avoid choosing another controller. This option and the Barrot-specific USB HCI class/interface EP0 request (**0x21**) still require fresh build/real-device validation.

## Gate 2 — direct USB HCI and BLE receive

Use the existing native Windows `tools/A2854.Probe` with a *dedicated external* WinUSB Bluetooth adapter. Verify each milestone independently:

1. USB HCI interface opens (not `WinUSB controller unavailable`).
2. HCI initialization and, if required, controller-specific firmware patch succeed.
3. A2854 advertisements are detected; the scan reports whether peer addresses are public or random.
4. Connect to the discovered live peer; do not assume a rotating address stays valid indefinitely.
5. SMP pairing, encryption and bond reuse work with the actual A2854 security properties.
6. Discover GATT HID (0x1812), enumerate individual 0x2A4D reports by 0x2908, enable notifications for 0xFB/0xFC.
7. Write unlock `0xAF` to discovered Feature Report 0xF0; capture and decode physical button/touch notifications.

Log the stage, error code, controller ID, and redacted event counts; retain secrets only locally. Mic report 0xFA is not required. **Stop at the first failed stage to diagnose it instead of claiming a later-stage failure.**

## Gate 3 — user-facing control

After actual native 0xFB/0xFC receive: route reports through A2854Decoder / suitable gesture engine → Windows SendInput → WPF interface. Ensure that disconnect, focus changes and pairing failures release held keys. Validate clickpad drag/hover and button mapping with the user on physical hardware.

Sources: [current handoff](../HANDOFF_2026-10-21_A2854.md), [historical Windows probe details](../../reference/siri-remote-for-windows/tools/windows-probe/README.md), [experiment timeline](../../reference/siri-remote-for-windows/docs/windows-port/EXPERIMENT_TIMELINE.md).
