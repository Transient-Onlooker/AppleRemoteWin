# Windows PnP driver-binding investigation

**Observed on physical A2854 / Intel AX200 (2026-10-17):** the WinRT BluetoothLEDevice succeeded in connecting on its second attempt and GATT enumerated 7 services, including HID 0x1812. The next call to enumerate HID characteristics returned `ACCESS_DENIED`. The HID interface probe listed **20 interfaces before and after** connection. This does not prove which Windows component owns the HID GATT characteristic access or whether a pre-existing/non-present PnP node exists.

Windows ships an inbox **HID-over-Bluetooth-LE** transport driver (`HidBthLE.dll`), documented at [Microsoft HID transport overview](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/hid-transports). The presence of the transport does not guarantee this particular remote is recognized as a user-accessible HID collection. PnP service and problem-code metadata can help distinguish driver binding, stale nodes, and missing child devices.

## Completed native PnP inventory

User's full output showed 362 PnP devices, 81 Bluetooth/HID candidates, and two BTHLEDEVICE HIDClass nodes with service mshidumdf; one OK (problem 0) and one Error (problem 10). The problem-code-10 HID node has not yet been attributed to A2854. Use [PnP relationship correlation](PNP_CORRELATION.md) before changing any driver.

The previous tool performed three property queries per 81 candidates (up to 243 separate calls). Updated quick mode avoids per-node property queries, with optional capped -Details -DetailLimit 8.

## Next safe on-device test (native Windows PowerShell)

```powershell
git pull --ff-only
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\windows-probe\pnp_probe.ps1
```

The script reads **Bluetooth** and **HIDClass** PnP entries, plus records whose internal instance IDs contain BTHLE. It does **not** require administrative privileges and does not change pairing, Bluetooth settings, driver service state, registry entries, or Windows security. It also includes **non-present** devices that can retain historical driver metadata.

The output displays *counts* by PnP service and coarse candidate rows with class, status, whether the private instance ID contains BTHLE, service, categorized driver description, and problem code. **It does not print the underlying InstanceId, MAC, serial, device path, hardware ID or container GUID**. The remote name is generalized to avoid identifying unique hardware.

To compare connection-state effects, optionally run it once with the remote disconnected and again immediately after a successful GATT connection attempt; nothing runs continuously or connects on its own.

## How to interpret

- A `BTHLEDEVICE` node suggests Windows enumerated a Bluetooth-LE service child, **not** that it belongs to this particular Siri Remote. Exact correlation may need additional sanitized metadata.
- HIDClass service information can suggest whether the inbox HID transport is active, but an ordinary input device elsewhere may also account for the row.
- `Problem` is the Windows PnP problem code if present; `0` means Windows did not report an issue for that node, not that the Siri Remote is receiving input.
- A missing PnP candidate or a `ACCESS_DENIED` GATT characteristic operation is **not** proof that Windows cannot support the remote by any method.
- Avoid disabling HID-over-GATT drivers, changing driver signatures, removing unrelated PnP devices, or forcing vendor driver changes to test the hypothesis.

Reference: [Microsoft Get-PnpDeviceProperty](https://learn.microsoft.com/en-us/powershell/module/pnpdevice/get-pnpdeviceproperty).

**Validation:** The PowerShell script's execution is checked on GitHub-hosted Windows CI runners. They do not have the user's physical A2854; conclusions about actual remote driver binding require the on-device report.
