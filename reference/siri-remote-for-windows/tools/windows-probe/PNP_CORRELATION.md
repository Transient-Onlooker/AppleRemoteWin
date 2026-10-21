# A2854 to Windows HIDClass PnP association

## Measured on 2026-10-17 (native Windows)

The earlier PnP inventory **did finish successfully** and reported 362 PnP devices, 81 Bluetooth/HIDClass/BTHLE candidates, and two `HIDClass` entries with `BTHLEDEVICE` instance-ID prefixes and service `mshidumdf`:

| HIDClass BLE candidate | PnP status | Problem code |
| --- | --- | --- |
| One | `OK` | `0` |
| Another | `Error` | `10` |

Windows code 10 is `CM_PROB_FAILED_START`, meaning the device driver stack failed to start. **The current evidence does not establish which of the two is the Siri Remote.** Existing devices such as the paired Samsung keyboard or non-present nodes can also have Bluetooth HID entries. GATT `0x1812` characteristic `ACCESS_DENIED` alone is insufficient to assign identity.

Sources: [Microsoft Code 10](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/cm-prob-failed-start), [PnP ContainerId](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/devpkey-device-containerid), [PnP Parent](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/devpkey-device-parent).

## Next test: compare device relationships without revealing identifiers

Run in Windows PowerShell from repository root:

```powershell
git pull --ff-only
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\windows-probe\pnp_correlate.ps1
```

The tool uses the private `InstanceId`, `DEVPKEY_Device_ContainerId`, and `DEVPKEY_Device_Parent` only inside the current process to check whether each Bluetooth LE HIDClass candidate shares the same container as the unique `DL1…` Bluetooth device or is descended from it in the PnP tree. It prints only the candidate ordinal, status, service, problem code, and relationship type. It does **not** print raw device instance IDs, MAC addresses, container GUIDs, serials, or device paths.

An `ancestor` or `same-container` match for the error-code-10 HID node would strongly associate that failed HID binding with the remote and prioritize investigating `DEVPKEY_Device_ProblemStatus` and the inbox driver. A different match, or no clear match, must not be interpreted as proof of connection or failure. Device container grouping can be ambiguous; non-present parent links may be incomplete.

Do not uninstall drivers, disable HID/BLE services, clear bonds, or change Windows protections at this stage. The existing quick PnP report is kept as a fallback. The previous version queried three properties per 81 nodes (up to 243 individual calls); its successor is quick by default and optionally bounded to 8 nodes using `-Details -DetailLimit 8`.

Offline CI uses synthetic PnP devices to verify that matching relationships, unrelated HID nodes, and identifier redaction are handled correctly. These tests cannot prove association for the physical A2854; only local PowerShell output can.
