# Windows-native HID access probe (Phase 1)

**PnP finding:** Two BTHLEDEVICE HIDClass/mshidumdf nodes exist; one failed to start (problem code 10). Their ownership is not established. Next run [read-only PnP relationship correlation](PNP_CORRELATION.md), without modifying drivers.

**Latest experiment:** The remote connected through WinRT after one `UNREACHABLE` retry, yet access to HID 0x1812 characteristics was denied and HID interface count stayed 20. The next diagnostic is a [read-only PnP driver inventory](PNP_DIAGNOSTICS.md), rather than repeating identical GATT calls or altering Windows security/driver settings.

**Update:** A first uncached GATT attempt was `UNREACHABLE`, but the next attempt **connected**, found 7 GATT services including HID `0x1812`, and received `ACCESS_DENIED` when listing HID characteristics. The next read-only step is [GATT `--hid-snapshot --scan-before 10 --attempts 3` comparison](GATT_DIAGNOSTICS.md) while the remote is connected. See the [BLE advertisement evidence](ADVERTISEMENT_DIAGNOSTICS.md). Do not attempt reads from unrelated HID devices.

**Status:** Diagnostics implemented; **not run against A2854 on native Windows yet**.
This is a read-only capability test, **not** the Windows Siri Remote mouse application.

## Prerequisites

- Native Windows 10/11 (not WSL) and Python 3.9+; no third-party packages.
- Bluetooth adapter returned to Windows and working in Device Manager.
- Try pairing A2854 via Windows Settings > Bluetooth & devices. A successful pairing alone does not prove HID access.
- Don't change Bluetooth driver security settings or disable system HID support.

If the adapter is still attached to WSL by USB/IP, first **inspect** the appropriate bus ID with `usbipd list` in an administrator PowerShell. For the previously observed `1-3` only (if still correct):

```powershell
usbipd detach --busid 1-3
usbipd unbind --busid 1-3
usbipd list
```

This may disconnect an active WSL Bluetooth session. It does not pair the remote on Windows.

## Run from repository root in PowerShell

```powershell
py -3 tools\windows-probe\hid_probe.py
py -3 tools\windows-probe\hid_probe.py --all
py -3 tools\windows-probe\hid_probe.py --all --index 0 --seconds 15
```

First two commands **enumerate**; the last makes a read-only attempt against the **chosen numbered HID interface**. Change `0` to the actual index that looks relevant (Apple/remote). With `--all`, many unrelated interfaces may appear, including internal laptop devices. No interface path, MAC address, serial, or raw microphone packet is printed.

Optional `--raw` prints only accepted `0xFB`/`0xFC` button and touch report bytes. Avoid publishing unreviewed dumps to public issues. Only use this tool against hardware you own or are authorized to test.

An Apple remote does **not necessarily** appear with USB vendor ID `05AC` in the Windows HID API: `--all` is a deliberate escape hatch. `input_length` is the HID API report buffer length, **not** the BLE characteristic value length. Each HID `ReadFile` buffer conventionally starts with its report ID, which may be absent or translated on some drivers.

## Interpreting results

- `0xFB` / `0xFC` printed: actual report delivery through Windows HID was observed; retain Windows version, adapter/driver version, and sanitized output for the next development phase.
- `Cannot open HID interface for reading`: the chosen interface rejected an open for `GENERIC_READ`; try other plausible candidates. This alone cannot prove a GATT fallback is impossible.
- `0 decoded events`: no recognizable reports arrived through that *particular* interface in this attempt; not evidence that the remote is unsupported.
- No candidate or metadata inaccessible: inspect Device Manager and Windows pairing first; do **not** assume the remote is a HID input device simply because it is visible in Bluetooth Settings.

**Not covered:** WinRT GATT service/descriptor access; output/feature `0xAF` write; auto-reconnect; mouse injection; audio. These require a separate implementation/verification phase. The probe never sends HID feature/output writes, records microphone data, or installs drivers.

## Cross-platform decoder tests

```powershell
py -3 -m unittest discover -s tools/windows-probe -v
```

Tests exercise sanitized `0xFB` / `0xFC` observations captured previously in Linux. Passing them does **not** establish native Windows device access.
