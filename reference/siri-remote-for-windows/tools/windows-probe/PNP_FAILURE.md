# Remote-owned HIDClass Code 10: failure status investigation

## Established Windows evidence (2026-10-17)

Native Windows **paired** the Siri Remote A2854 (its displayed name begins with `DL1`). In one GATT attempt, Windows successfully connected and listed seven services, including HID-over-GATT service `0x1812`; requesting HID report characteristics returned `ACCESS_DENIED`. The visible HID interface list remained unchanged at 20.

A separate read-only PnP diagnostic found two BLE HIDClass nodes using `mshidumdf`. The user then confirmed parent-chain correlation to the Siri Remote:

- A **Siri Remote-descended** HIDClass/BTHLEDEVICE node: **Status=Error**, `Problem=10`, service `mshidumdf`.
- An **unrelated** BLE HIDClass/BTHLEDEVICE node: **Status=OK**, `Problem=0`.

Unlike the earlier aggregate PnP inventory, this identifies the **failed child HID device as belonging to the Siri Remote**. However, **Code 10 identifies a failure to start the driver node, not the specific root cause**. GATT `ACCESS_DENIED` can be consistent with OS HID service ownership, but cannot independently explain why the HID child failed to start.

References: [Microsoft Code 10](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/cm-prob-failed-start), [ProblemStatus](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/devpkey-device-problemstatus), [HID transports](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/hid-transports).

## Next: read-only, remote-specific NTSTATUS and device-start event

From the Windows PowerShell prompt in the repository root:

```powershell
git pull --ff-only
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\windows-probe\pnp_failure.ps1
```

The script (1) uniquely selects the `DL1` Bluetooth parent, (2) follows private PnP parent links to identify a single code-10 BLE HID child, (3) prints `DEVPKEY_Device_ProblemStatus` formatted as an eight-digit NTSTATUS, (4) optionally searches recent **Kernel-PnP/Configuration event 411** entries matching that **exact** child instance ID and prints sanitized problem/status fields.

All device IDs, Bluetooth addresses, parent IDs, event messages, GUIDs, and device names are **excluded** from output. The event lookup is bounded to 200 events in the preceding 14 days by default. A missing event is not proof no start failure occurred: the log may be unavailable, aged out, or beyond the search cap. The NTSTATUS can also be zero or missing, which is non-diagnostic.

No device is restarted, unpaired, disabled, updated or reconfigured. Do **not** install drivers or disable Windows HID security protections on the current evidence.

CI regression tests use synthetic PnP nodes and events to verify exact identity matching, NTSTATUS formatting, filtering unrelated HID nodes, and identifier redaction. Only running on the physical Windows system establishes the real failure status.
