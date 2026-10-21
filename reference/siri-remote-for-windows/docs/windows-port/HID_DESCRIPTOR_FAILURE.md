# A2854 Windows HID start failure: descriptor investigation

## Established (physical device, 2026-10-17)

The native Windows PnP chain connects the remote-like BLE parent to its **own** `BTHLEDEVICE/HIDClass` child; the unrelated BLE keyboard child does not match. That Siri Remote child has:
- Driver service: `mshidumdf`
- PnP problem code: **10 / failed to start**
- `DEVPKEY_Device_ProblemStatus`: **`0xC00000B9`**
- Exact-instance Kernel-PnP Configuration **event 411**, at 2026-10-17 12:53:25 local time, `Problem=0xA` and **`Status=0xC00000B9`**.

The remote had also produced a native WinRT GATT `SUCCESS/CONNECTED` session with **seven services**, including HID `0x1812`, while listing its HID characteristics returned `ACCESS_DENIED`. No additional HID interface became visible in the before/after 20-interface snapshots. The first of three connection attempts in one run was `UNREACHABLE` and the second succeeded; this timing is a separate issue.

## Status meaning and limits of attribution

Microsoft's NTSTATUS table names `0xC00000B9` **`STATUS_COULD_NOT_INTERPRET`**. Its general documented meaning discusses an ACL, which **does not imply** an ACL is faulty here. Crucially, Microsoft's **HidP_GetCollectionDescription** reference explicitly lists this same NTSTATUS when **an error is detected in a HID report descriptor**. The `HIDP_GETCOLDESC_DBG` structure can provide a parser error code and byte offset in the appropriate driver-debug context.

This makes a Windows HID descriptor parsing/compatibility failure the **leading hypothesis**, *not a proven root cause*. The identical NTSTATUS could be propagated from another layer. Microsoft also documents other possible HID parser outcomes, such as truncation, missing top-level collections, and malformed items. No report-map bytes or HIDCLASS parser trace for this actual A2854 have yet been captured, so do not identify a specific invalid field, assume all Siri Remotes have the issue, or infer that the remote is incompatible with every Windows transport.

Authoritative references:
- [MS-ERREF: 0xC00000B9 meaning](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-erref/596a1078-e883-4972-9bbc-49e60bebca55)
- [Microsoft HidP_GetCollectionDescription return codes](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpddi/nf-hidpddi-hidp_getcollectiondescription)
- [Microsoft HIDP_GETCOLDESC_DBG / BreakOffset](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpddi/ns-hidpddi-_hidp_getcoldesc_dbg)
- [Microsoft HID I2C troubleshooting (HIDCLASS parser example; not necessarily the same BLE cause)](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/troubleshoot-hid-over-i2c-device-issues)
- [BlueZ GATT Characteristic ReadValue](https://bluez.readthedocs.io/en/latest/gatt-api/)

## Practical next evidence: raw HID Report Map (UUID 0x2A4B)

We have physical samples of Input reports `0xFB` (buttons) and `0xFC` (touch), and Linux previously accessed multiple `0x2A4D` report characteristics. **These samples are not the descriptor.** The raw HID Report Map characteristic `0x2A4B` is necessary to examine the HID item stream and potentially reproduce Windows parser objections.

A [read-only BlueZ capture and offline inspector](../../tools/protocol/report_map.py) has been added. It requires a **pre-existing Linux BlueZ session that is already connected and has resolved GATT services**, where the OS does not claim the HID service. It does **not** re-pair, connect, modify BlueZ, write feature reports, or change Windows drivers. Do not switch the Windows Bluetooth adapter to Linux just to run the Windows-side command below.

Once an eligible connected BlueZ session is available:

```bash
python3 -m pip install --user dbus-next
python3 tools/protocol/report_map.py --output a2854-report-map.bin
```

If several HID peripherals are connected, narrow to the already known local Bluetooth name prefix with `--name-prefix DL1`, or disconnect unrelated peripherals. **No BLE address is needed in the command.** The script fails closed unless precisely one suitable Report Map can be located. It creates the output file exclusively and never overwrites an existing capture.

Then inspect **the saved file** offline on either platform, without a Bluetooth adapter:

```powershell
py -3 tools\protocol\report_map.py --inspect .\a2854-report-map.bin
```

The inspector prints SHA-256, length, Report IDs, item/collection counts, and basic structural warnings. This is a **lightweight bounds checker**, not a full HID standard validator or reproduction of Windows HIDPARSE. A clean result therefore does not contradict Code 10.

When the Report Map is available, the next development step is to test it against Microsoft's HID parser or collect a sanitized HIDCLASS driver trace to identify the exact error offset. **No driver disabling, inf edits, registry writes or Windows pairing reset is authorized or necessary for the present diagnosis.**

## 2026-10-17 WSL recovery: pip absent and BLE link drops

The physical-device owner restored the prior Ubuntu 22.04 WSL instance, confirmed `8087:0029` passed through from Windows as bus ID `1-3`, and verified BlueZ was running with `--noplugin=input,hog`. The A2854 remains **Paired: yes, Trusted: yes**, but one `bluetoothctl connect` succeeded only transiently and soon reported **Connected: no**. Ubuntu Python also lacked `pip`.

Install the user-space Python dependency using Ubuntu's package manager:

```bash
sudo apt update
sudo apt install -y python3-pip
python3 -m pip install --user dbus-next
python3 -c "import dbus_next; print('dbus-next OK')"
```

The capture tool now optionally supports **`--connect`** so it can invoke BlueZ `org.bluez.Device1.Connect` and promptly read the Report Map in the **same process**. It checks for one **previously paired** `DL1` remote before connecting, polls `ServicesResolved`, and makes at most three connection/read attempts. It does **not** pair/unpair, write GATT attributes, disable HID plugins, change a driver, or deliberately disconnect the device.

```bash
python3 tools/protocol/report_map.py \
  --name-prefix DL1 --connect \
  --output a2854-report-map.bin
```

If that still fails, do **not** clear the pairing. Preserve the sanitized failure summary and examine BlueZ logs / connection reasons before deeper interventions.

## Current blocking output: all 3 BLE attempts failed (2026-10-17)

On the restored WSL Ubuntu 22.04 BlueZ session, a unique already-paired `DL1` remote was found, but `report_map.py --connect` made three attempts and ended with a generic `RuntimeError`. The user pressed the remote's buttons during the attempts. That original program suppressed all stages, so the output alone does **not** establish that the remote was asleep, that Bluetooth failed to connect, or that `0x2A4B` is absent.

Code now prints **sanitized per-attempt stages**, including generic BlueZ Connect result, whether Connected/ServicesResolved were ever observed, count of HID `0x1812` services, Report Map `0x2A4B` characteristics, Report `0x2A4D` characteristics, attempted GATT reads, and a generic sanitized last read error code. No device IDs, paths, MACs or private error message strings leave the program.

Because the Windows working directory contains **uncommitted changes** to two other files, `git pull --ff-only` aborts. Fetch only the newer file, *without modifying any tracked work*:

```bash
git fetch origin main
git show origin/main:tools/protocol/report_map.py > /tmp/a2854-report-map.py
python3 /tmp/a2854-report-map.py --name-prefix DL1 --connect --output a2854-report-map.bin
```

If this shows `Connect=success` but `ServicesResolved=False`, focus on GATT discovery and BlueZ logs. If `ServicesResolved=True` but no HID service is visible, verify plugin configuration and HID ownership. If HID service/report characteristics are visible but Report Map count remains 0, check the device's actual GATT attribute set, rather than assuming the peripheral exposes `0x2A4B`. If a Report Map exists but read returns a non-success result, investigate ATT permissions/authorization and BlueZ behavior. Preserve all existing pairings.

## 2026-10-17 BLE connection failure: corrected diagnostic method

The user ran the updated `--connect` probe while pressing remote buttons. It selected exactly one existing bonded remote but reported:

- Attempt 1: `Connect=timeout`, no observed Connected or ServicesResolved, no HID GATT tree, no report-map reads
- Attempts 2-3: `Connect=Failed`, same absent observations

**Important correction:** these results are not proof that an ACL link never formed. The program originally awaited `Connect()` for up to 15 seconds **before** starting to observe GATT states. A momentary link during that wait would be missed. Therefore `0x2A4B count=0` also does **not** establish that the device lacks the characteristic.

The program is now revised to **launch D-Bus Connect asynchronously and observe `Connected`, `ServicesResolved`, HID `0x1812`, and Report Map `0x2A4B` concurrently**, allowing detection of a short connection even while `Connect()` remains pending.

BlueZ's [bluetoothctl LE connection documentation](https://github.com/bluez/bluez/blob/master/doc/bluetoothctl.rst) states that an LE connect needs an active advertising report and can fail if no advertising report is seen before the timeout. The program now offers `--scan-before 8`, which temporarily invokes `Adapter1.StartDiscovery` on the existing adapter, observes **RSSI property updates for only the already paired remote**, releases its discovery session via `StopDiscovery`, and then tries the connection. No raw addresses or unrelated BLE devices are logged.

A **zero** fresh RSSI update count is **inconclusive**; BlueZ can suppress signals or report a device using a changed resolvable private identity. A positive count establishes only that BlueZ observed an RSSI change for the stored remote object, not that a GATT or HID connection succeeded.

Working-tree updates are blocked by unrelated local edits. Safely run the newest tracked script from a temporary path:

```bash
git fetch origin main
git show origin/main:tools/protocol/report_map.py > /tmp/a2854-report-map.py
python3 /tmp/a2854-report-map.py --name-prefix DL1 --connect \
  --scan-before 8 --output a2854-report-map.bin
```

If this again fails, **stop reconnection attempts** and inspect only the local Bluetooth controller/power state and time-scoped HCI/BlueZ reason codes. Prefer a privacy-safe filtered diagnostic over copying raw journals that may reveal hardware addresses. Do not unpair, delete BlueZ cache, modify Apple TV pairings, or reinstall the Bluetooth driver based on this data alone.

## Observed after 8s scan (2026-10-17, WSL)

A unique previously bonded device was selected. A scan produced zero **new RSSI PropertiesChanged updates**, which is inconclusive. Unlike previous attempts, all three `Device1.Connect` calls returned `success`, and `Connected=True` was observed. However, `ServicesResolved=False` during the observation windows, no HID `0x1812` or Report Map `0x2A4B` objects appeared, and no Map reads were sent.

Interpretation: Bluetooth connectivity and GATT service discovery are **different milestones**. BlueZ `Connect()` success is not a guarantee of completed service resolution; see [BlueZ Device1 API](https://bluez.readthedocs.io/en/latest/device-api/). The absence of HID objects before service resolution **does not establish** the physical remote lacks these characteristics. The Windows `0xC00000B9` issue remains a separate, already-established HID initialization failure.

The capture tool now also reports `connected samples / total samples` and the final observed Connected state, distinguishing a fleeting BLE link from a sustained connection whose GATT discovery is stalled.

Next perform a **read-only, privacy-safe** inspection of the relevant BlueZ journal:

```bash
git fetch origin main
git show origin/main:tools/protocol/bluez_journal.py > /tmp/bluez_journal.py
python3 /tmp/bluez_journal.py --minutes 60
```

This emits only generic failure categories and short numeric codes, **not raw journal messages, addresses, serials or D-Bus paths**. If no event is found, BlueZ could be running without detailed debug logs. Do not restart the controller or repeat pairing solely based on an unresolved GATT state.

## Journal from actual WSL session (2026-10-17)

The user read 60 minutes of `bluetoothd` journal twice (ordinary user and `sudo`), with identical results: **17** total lines, **4** broadly GATT/service-tagged, **8** other Bluetooth-failure-tagged, **no parsed numeric status code**. This is **not an evidence-backed classification of 12 actual errors** because the original keyword-based classifier includes informational messages containing `service` or `gatt`. Equality with `sudo` means permissions did not account for the missing detail in this window.

An enhanced [privacy-safe diagnostic](../../tools/protocol/bluez_journal.py) supports `--detail` to print chronological line numbers and allowlisted operation/outcome fingerprints, and `--kernel` to examine the kernel HCI/USB journal separately. Neither mode emits raw BlueZ messages or identifiers. These fingerprints are only approximate; they are not guaranteed to come from the A2854 device.

```bash
git fetch origin main
git show origin/main:tools/protocol/bluez_journal.py > /tmp/bluez_journal.py
python3 /tmp/bluez_journal.py --minutes 120 --detail
python3 /tmp/bluez_journal.py --minutes 120 --detail --kernel
```

An important alternative hypothesis is stale bond credentials: the **same physical Intel AX200** controller was used to pair this remote under both native Windows and WSL BlueZ. The `Paired: yes` flag reflects local stored credentials, not necessarily the remote still accepting those credentials after any subsequent Windows pairing. However, the current evidence does **not** establish key mismatch. Look for safe authentication/encryption/GATT failure indicators in logs before considering any re-pairing, which could disrupt the working Windows bond. Do not erase either OS's bonding database as a first step.

## Cross-check of the previously successful WSL experiment (2026-10-17)

The agent handling the earlier successful hardware session recovered original command order. The durable handoff ([WSL_COMPATIBILITY_NOTES](WSL_COMPATIBILITY_NOTES.md), [EXPERIMENT_TIMELINE](EXPERIMENT_TIMELINE.md), and [HANDOFF](../../HANDOFF_2026-10-17.md)) independently agrees:

1. `~/siri-remote/target/release/siri-remote pair --scan-seconds 8 --hold-seconds 10` established an actual BlueZ bond, notwithstanding a misleading post-pairing error message.
2. The first `siri-remote events --address ...` connected but listed **5 services**, excluding HID `0x1812`; BlueZ journal reported `input-hog profile accept failed`.
3. After `bluetoothd --noplugin=input,hog` was applied through a systemd override **and the service restarted**, the **same Rust `events` CLI** enumerated **6 services**, **20 characteristics**, **6 descriptors**, with **8 HID report characteristics**, and subscribed to `0xFB` buttons and `0xFC` touch. Feature report `0xF0` received `0xAF`.
4. No bond deletion, cache wipe, remote reset, or manually executed `bluetoothctl connect` step was documented as necessary.
5. **Historical `ServicesResolved=True` was not independently sampled**; earlier CLI text saying “Services already resolved” coexisted with missing HID services.

The current WSL session confirms an installed systemd override and an active Bluetooth service, but **the live process command line has not yet been checked**. The new Python `report_map.py` reports `Connect=success` / `Connected=True` / `ServicesResolved=False` in short observations. This is not proof that BlueZ is in precisely the same state as the historical missing-HID case.

### Next discriminating experiment (no pairing changes)

First check the actual running `bluetoothd` argv, then **replay the existing successful Rust binary**, which implements the older connection/subscription workflow and may differ from our newer D-Bus-only map reader:

```bash
ps -C bluetoothd -o args=
test -x "$HOME/siri-remote/target/release/siri-remote" && echo 'historic CLI: available' || echo 'historic CLI: missing'
REMOTE=$(bluetoothctl devices | awk '/DL1|Siri Remote/ {print $2; exit}')
if [ -n "$REMOTE" ] && [ -x "$HOME/siri-remote/target/release/siri-remote" ]; then
  timeout 45s "$HOME/siri-remote/target/release/siri-remote" events --address "$REMOTE" 2>&1 \
    | sed -E 's/([[:xdigit:]]{2}:){5}[[:xdigit:]]{2}/[BLE ADDRESS]/g'
fi
```

Report the service count and presence of `0x1812`, plus any raw-GATT or BlueZ failure **with MACs redacted**. `timeout` may end the successful continuous event stream; this does not imply the stream failed. If the historic binary is missing, do not rebuild from unmodified upstream automatically: the local build used a temporary audio-disabled workaround. First examine the preserved backup/build tree.

**Do not restart BlueZ or clear bond state until the process arguments and historic CLI results are known.** If the actual process is already started with `--noplugin=input,hog`, reapplying the same override without new evidence is unlikely to help.

## Rust historical CLI reproduced service discovery timeouts (2026-10-17)

The physical-device owner **verified the actual process** is running `/usr/lib/bluetooth/bluetoothd --noplugin=input,hog` and that the old, previously successful, audio-disabled Rust `~/siri-remote/target/release/siri-remote` binary still exists.

Running its actual `events --address ...` against the existing paired remote for 45 seconds repeatedly yielded **`Service discovery timed out`**, with one **`le-connection-abort-by-local`**. Thus the failure **reproduces across both the historical Rust CLI and our Python D-Bus reader**; it cannot be assigned solely to our new Python script or simply to failure to apply the BlueZ HID-plugin override. Historic success already confirmed real `0x1812` and `0x2A4B` GATT characteristics, so do not conclude the remote no longer supports HID.

**Important upstream clue:** [btleplug 0.13.5 changelog, 2026-10-10](https://docs.rs/crate/btleplug/0.13.5/source/CHANGELOG.md) explicitly fixes BlueZ `connect()` returning the *identical* `Service discovery timed out` when the remote takes longer than the older **5-second** service-resolution limit, extending the limit to **30 seconds**. This repository currently pins **btleplug 0.11.8** and **bluez-async 0.8.2** in `Cargo.lock`, predating the fix. This establishes a plausible *library timeout* contributing to the Rust symptoms; it does **not prove** BlueZ would resolve successfully if given 30 seconds, since a separate Python observation also saw `ServicesResolved=False`.

The least disruptive controlled experiment is now **one bounded BlueZ D-Bus connection with longer monitoring** instead of many Rust reconnections at 0.5-second intervals:

```bash
git fetch origin main
git show origin/main:tools/protocol/report_map.py > /tmp/a2854-report-map.py
python3 /tmp/a2854-report-map.py --name-prefix DL1 \
  --connect --scan-before 8 --observe-seconds 45 --attempts 1 \
  --output a2854-report-map.bin
```

The sanitized output includes `connected samples=N/M` and final Connected state. If GATT resolves and a 0x2A4B Report Map is readable, this verifies the timeout hypothesis *for the BlueZ observation*, not necessarily all Rust-side behavior. If still unresolved after a sustained connection, investigate authentication/encryption and actual ATT transactions at the controller level, **without exposing stored pairing keys or modifying the pairing databases**. Restarting a properly configured bluetoothd, wiping its cache, or forcibly re-pairing is not warranted by current evidence.

## 2026-10-17 Windows fresh pairing: exact Code 10 recurred

After returning Intel AX200 to Windows (`usbipd list` showed **Not shared**) and pairing the Siri Remote again natively, Windows briefly displayed a connected state for ~5 seconds, followed by disconnection and a **Driver error** UI status. The owner re-ran the **unchanged** remote-ancestor-correlated read-only PowerShell `tools/windows-probe/pnp_failure.ps1` from the correct repository root and obtained:

```text
Remote anchors=1; BLE HIDClass nodes=2
BLE HID problem-code-10 nodes descended from remote: 1
HID problem code: 10; service: mshidumdf
DEVPKEY_Device_ProblemStatus (NTSTATUS): 0xC00000B9
Matching Kernel-PnP event 411 entries: 1
2026-10-17 16:28:53  EventId=411  Problem=0xa  Status=0xC00000B9
```

This confirms a **new**, post-repairing failure with **exactly the same driver/service and NTSTATUS** as the earlier 12:53:25 observation. It rules out “the PnP error happened only before re-pairing.” The approximately five-second Bluetooth disconnect **may** be related to HID initialization failure but direct cause/timing remains unproven. A normal idle BLE disconnection is also possible. Do not interpret a Windows Bluetooth connection label alone as an HID readiness indicator.

Microsoft [HidP_GetCollectionDescription documentation](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpddi/nf-hidpddi-hidp_getcollectiondescription) explicitly includes `STATUS_COULD_NOT_INTERPRET` for HID Report Descriptor errors, and [HIDP_GETCOLDESC_DBG](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpddi/ns-hidpddi-_hidp_getcoldesc_dbg) describes an internal parser failure byte offset. But this PnP status does **not** establish the actual source of the propagated error, so do not claim a descriptor byte is defective without raw `0x2A4B` and/or driver trace.

**Next** rerun the repository's existing **read-only native Windows WinRT GATT probe** *after* this fresh pairing. Run it once without `--index` to identify the current Siri Remote index (indexes can change after re-pairing), then use that index with `--hid-snapshot --scan-before 10 --attempts 3`. Compare against the earlier WinRT session's **7 services, HID 0x1812 present, HID characteristic discovery `ACCESS_DENIED`, HID interfaces 20→20**. Do not rerun `pair`, remove device nodes, change Bluetooth drivers, bypass GATT access controls, or reset Windows PnP just to repeat a known Code 10.

## 2026-10-17 WinRT GATT re-test following Windows re-pairing

The newly paired A2854 is paired BLE index **0** (`DL1…`); the only other visible paired LE device is a keyboard. The user ran `py -3 tools/windows-probe/gatt_probe.py --index 0 --hid-snapshot --scan-before 10 --attempts 3`:

- Scan: 88 matching advertisement events across 9 unverified BLE identities; one Apple-manufacturer advertisement had the HID `0x1812` UUID. **Do not ascribe this advertisement to A2854 without matching identity evidence.**
- First and only GATT attempt: `SUCCESS`, BluetoothLEDevice `CONNECTED`, **seven** GATT services, including HID `0x1812`.
- Uncached HID characteristic enumeration: **`ACCESS_DENIED`**.
- HID interface signatures: **20 before → 20 after**. No new signature appeared, but unchanged count cannot exclude an existing broken HID instance (which is now independently proven).

This exactly **reproduces the prior Windows GATT pattern after a fresh re-pair**, alongside the newly repeated exact Siri Remote HID driver node `mshidumdf` Code 10 / NTSTATUS `0xC00000B9` / Kernel-PnP Event 411. The Windows Bluetooth LE transport does work for normal GATT service discovery. It is **not justified** to blame the five-second Windows Settings “disconnected” indicator for HID Code 10; Windows may close an otherwise idle BLE link. `ACCESS_DENIED` to a user-mode GATT characteristic operation cannot alone distinguish OS HID ownership from other access-control behavior. The proven failure boundary is **Windows HID child driver start**.

The best next non-invasive investigation is **read-only inventory of available HIDCLASS trace providers and event channels**:

```powershell
logman query providers | Select-String -Pattern 'HIDCLASS','BTHLE','Bluetooth'
Get-WinEvent -ListLog '*HID*' -ErrorAction SilentlyContinue |
    Select-Object LogName, IsEnabled, RecordCount
```

According to [Microsoft's HID troubleshooting guide](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/troubleshoot-hid-over-i2c-device-issues), HIDCLASS WPP tracing can reveal a report-descriptor parser **byte offset** when an invalid HID descriptor is returned. The manifest-based `Microsoft-Windows-Input-HIDCLASS` ETW provider is also described there. However, the illustrated transport is **I2C**, not our Bluetooth LE HID transport, and the precise WPP trace availability and messages on this system must be determined before promising an offset or capturing any trace. This provider inventory is read-only and does not install a driver, change the pairing, or turn on tracing.

Do not upload raw ETL traces, SetupAPI logs, or Windows instance paths publicly without redaction; they may contain unique Bluetooth addresses and other identifiers. The next step is deciding whether a supported HIDCLASS trace is available or whether the **actual Report Map `0x2A4B`** must be captured again from Linux following an explicitly user-approved, disruptive re-pair.

## 2026-10-17 HIDCLASS ETW capability inventory (actual Windows)

The owner inspected Windows trace providers with `logman query providers` and found the **manifested** `Microsoft-Windows-Input-HIDCLASS` provider (GUID `{6465DA78-E7A0-4F39-B084-8F53C7C30DC6}`). Other registered providers include Bluetooth Bthmini and BthLEPrepairing. `Get-WinEvent -ListLog '*HID*'` showed one enabled channel, `Microsoft-Windows-hidcfu/Operational`, with **zero records**. The `hidcfu` channel is not the HIDCLASS driver-start trace and zero records there do **not** contradict the observed `mshidumdf` Code 10.

Microsoft [HID over I2C troubleshooting](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/troubleshoot-hid-over-i2c-device-issues) identifies manifested HIDCLASS ETW events with provider name `Microsoft-Windows-Input-HIDCLASS`. The [HID event tracing guidance](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/event-tracing) explains `logman` collection for ETW and separately documents WPP tracing. **Do not conflate the HIDCLASS manifest ETW provider with WPP parser-trace capture**: manifested events alone may not include an erroneous descriptor byte offset, and the Microsoft example's transport is I2C rather than Bluetooth LE.

Next diagnostic after **explicitly starting** a temporary ETW session in elevated Windows PowerShell:

```powershell
# Read-only provider metadata and declared event IDs; no trace started here.
logman query providers Microsoft-Windows-Input-HIDCLASS
$p = Get-WinEvent -ListProvider Microsoft-Windows-Input-HIDCLASS
$p | Select-Object Name, Id
$p.Events | Group-Object Id | Select-Object -First 20 Name, Count
```

If event schemas look potentially informative, create a separate short trace around a **user-initiated** Windows reconnect or device rescan. Do not equate an empty trace with missing driver events because the failed HID device must actually initiate operations while tracing. No registry modifications, driver removal/reinstallation, device re-pairing, or direct transport bypass is required for provider inspection.

## Confirmed manifested HIDCLASS ETW event catalog (Windows 2026-10-17)

The physical Windows computer reported:

- Provider: `Microsoft-Windows-Input-HIDCLASS` `{6465DA78-E7A0-4F39-B084-8F53C7C30DC6}`
- Provider keywords: `Default=0x1`, `Microsoft-Windows-Input-HIDCLASS-Analytic=0x8000000000000000`
- `logman` lists level `Informational=4`
- `Get-WinEvent -ListProvider` declares **5 event templates**: ID 1 v0, ID 2 v0, and ID 3 v0/v1/v2.
- These are **declarations**, not evidence that any corresponding event has yet been emitted for Siri Remote; event meanings/payload schemas were not captured.

A [bounded ETW collector](../../tools/windows-probe/hidclass_trace.ps1) is available, using only the manifested HIDCLASS provider. It runs for 5–60 seconds (default 20), starts a private ETW trace with keywords `0x8000000000000001` and level 4, and stops it in `finally`. It does not re-pair, restart PnP nodes, install drivers, or change the adapter.

**Run from elevated Windows PowerShell**; where the local worktree has modifications blocking a pull, retrieve only the fresh script:

```powershell
git fetch origin main
git show origin/main:tools/windows-probe/hidclass_trace.ps1 |
    Set-Content "$env:TEMP\hidclass_trace.ps1" -Encoding UTF8
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\hidclass_trace.ps1" -Seconds 20
```

During that 20-second window touch/press buttons on the Siri Remote. The script **never prints raw event bodies**, only event ID, version and count; the raw ETL stays in the local `%TEMP%` directory and **must not be published**. An empty trace **does not** prove absence of a parser error, because already-failed Code 10 PnP nodes may not re-run driver initialization on ordinary BLE activity. Any future PnP-node restart / driver re-enumeration to reproduce Code 10 should be intentional and limited to the correct A2854 node, never the shared Intel AX200 adapter or the paired keyboard. Users reliant on Bluetooth keyboard/mouse should not toggle the host adapter for this experiment.

Sources: [Windows ETW tracing](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/event-tracing), [HIDCLASS manifested ETW trace](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/troubleshoot-hid-over-i2c-device-issues), [HidP_GetCollectionDescription error possibilities](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpddi/nf-hidpddi-hidp_getcollectiondescription).

## 2026-10-17 Actual HIDCLASS ETW events captured

The first 20-second manifested HIDCLASS trace on the physical Windows machine **succeeded** while the user pressed the Siri Remote's Back/TV/touchpad:

```text
HIDCLASS ETW started for up to 20 seconds.
HIDCLASS events found: 12
EventId Version Count
1       0       1
2       0       1
3       2      10
```

**This proves the provider emitted records during the observation window**, not that those records originated on the Siri Remote or that any were errors. Other HID devices were present. The event descriptions/payloads have not been inspected yet. Avoid inferring descriptor corruption or event meanings solely from these event IDs.

A new **read-only metadata-only** inspector [`tools/windows-probe/hidclass_trace_inspect.ps1`](../../tools/windows-probe/hidclass_trace_inspect.ps1) locates the newest `siri_hidclass_*.etl` in local `%TEMP%`, reports static provider event schema field **names/types** and each observed event's relative time, numeric task/opcode/level, ID/version. It never outputs any per-event data field value, event message/XML, device paths, addresses or ETL filename.

Run in administrator PowerShell (same Windows account whose TEMP contains the trace):

```powershell
cd "C:\Users\무조림.000\Desktop\코딩\01_working\siri-remote-for-windows"
git fetch origin main
git show origin/main:tools/windows-probe/hidclass_trace_inspect.ps1 |
    Set-Content "$env:TEMP\hidclass_trace_inspect.ps1" -Encoding UTF8
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\hidclass_trace_inspect.ps1"
```

The next step after schema inspection is to decide whether **specific safe numeric error/status/offset fields** can be extracted and whether the events can be correlated to the known broken Siri Remote PnP HID node **without printing or publishing unique instance paths**. Do not re-pair or reset Bluetooth based on the 12-event count alone.

## 2026-10-17 HIDCLASS ETL schema revealed status and descriptor bytes

The **same ETL** was inspected on the owner's Windows machine, using `hidclass_trace_inspect.ps1`. The manifested event ID **3 v2** schema includes:

- `DeviceStartStatus:win:UInt32` (**potentially directly informative for Code 10**)
- `DeviceInstancePath:win:UnicodeString` (private; **do not print**)
- `ReportDescriptorLength:win:UInt32` and `ReportDescriptor:win:Binary` (the **full descriptor may already be in this ETL**)
- `VendorID`, `ProductID`, `DeviceServiceName`, `CollectionCount`, device power state, etc.

Observed trace: ID 1 v0 once, **ID 3 v2 ten times**, ID 2 v0 once. All printed `SecondsFromFirst=0`: records are effectively simultaneous at the supplied millisecond display precision, perhaps a provider rundown/snapshot, **not demonstrated to follow each button press**. Events may describe other HID devices. Do not guess that one of these ten is A2854 before attribution.

A new [local, read-only safe status parser](../../tools/windows-probe/hidclass_trace_status.ps1) reads **numeric** `DeviceStartStatus`, `ReportDescriptorLength`, and correlates each event's private `DeviceInstancePath` with a local PnP node by **exact** `InstanceId`, then checks parent ancestry to the **unique DL1/Siri Remote Bluetooth node**. No paths/addresses/descriptor bytes are emitted. **UnmatchedPath means unproven attribution**, not a negative proof. Re-run on the same Windows account whose TEMP stores ETL:

```powershell
cd "C:\Users\무조림.000\Desktop\코딩\01_working\siri-remote-for-windows"
git fetch origin main
git show origin/main:tools/windows-probe/hidclass_trace_status.ps1 |
    Set-Content "$env:TEMP\hidclass_trace_status.ps1" -Encoding UTF8
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\hidclass_trace_status.ps1"
```

This extraction neither starts new tracing nor restarts any device. If the event is **RemoteDescendant** and shows `0xC00000B9` plus a nonzero descriptor length, that is strong evidence that the HIDCLASS manifested ETW snapshot observed the same failure and may contain useful descriptor bytes for later local offline parsing. It does **not** prove malformed bytes until actual parsing or driver trace confirms it. If no remote-descendant event matches, leave linkage unresolved and develop a more selective correlation method; do not claim another device's error is Siri Remote's.

## 2026-10-17 HIDCLASS status parser encountered event layout mismatch

On the real PC, `hidclass_trace_status.ps1` opened the same locally retained ETL and found the expected **ten Event ID 3 version 2** records. The Siri Remote was uniquely identifiable as a Bluetooth PnP anchor, **but every event returned `SchemaMismatch`** under the script's original requirement that `EventRecord.Properties.Count` be at least as large as the provider's manifest template count. Thus **no event start status, device attribution, or descriptor length was actually extracted**. Do not describe this as a remote-side descriptor error; it is a **diagnostic parser layout assumption failure**.

The script was repaired in commit `8c86a4f`: it now inspects each record's **named XML EventData** fields first, falling back to manifest-indexed `Properties` **only if** actual property count equals the full template count. When neither is available, it prints `LayoutUnresolved` with **only numeric** `PropertyCount` and `XmlFieldCount` for further investigation; it never guesses positional indexes for a truncated event, nor prints unique identifiers or raw descriptor data.

Collect the repaired output from the original ETL (no new pairing/tracing):

```powershell
cd "C:\Users\무조림.000\Desktop\코딩\01_working\siri-remote-for-windows"
git fetch origin main
git show origin/main:tools/windows-probe/hidclass_trace_status.ps1 |
    Set-Content "$env:TEMP\hidclass_trace_status.ps1" -Encoding UTF8
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\hidclass_trace_status.ps1"
```

Only `Source=NamedXML` or `Source=FullTemplate` gives meaningful start-status and correlation rows; `Source=LayoutUnresolved` is **inconclusive**. Inspect counts to determine the next trustworthy decoding approach without requesting raw ETL upload.

## 2026-10-17 Event 3 v2 decoded, but failed A2854 HID child was NOT represented

The owner reran the fixed read-only status inspector against the same locally saved ETL. All **10** HIDCLASS Event ID 3 v2 records now decode with **named XML** (21 actual fields, vs 32 in the current provider template). All 10 yielded \`DeviceStartStatus=0x00000000\` and \`Attribution=OtherPnP\` (event path matched a known Windows PnP node that **was not** a descendant of the unique Siri Remote Bluetooth anchor). The ten descriptor lengths were 109, 170, 218, 77, 343, 63, 151, 67, 381 and 35 bytes.

This means **none of the previously captured event snapshots can be ascribed to the failed A2854 HID child**. In particular, do **not** use any of those ten ReportDescriptors as the Siri Remote's descriptor or infer that A2854 HID started successfully. The actual remote child remains independently confirmed \`mshidumdf\`, Code 10 / \`0xC00000B9\`. ETW rundown of other, already-started HID nodes does not demonstrate a fresh A2854 driver-start attempt.

For a more discriminating experiment, the repository now provides [\`tools/windows-probe/hidclass_restart_trace.ps1\`](../../tools/windows-probe/hidclass_restart_trace.ps1), an **explicitly opt-in PnP-state-changing** trace. It verifies a *unique* DL1/Siri Remote Bluetooth ancestor and *one* BTHLEDEVICE HIDClass child of that anchor having **ProblemCode=10**, **Service=mshidumdf**, and **ProblemStatus=0xC00000B9**. It refuses to restart anything if this evidence differs. It starts a short manifested HIDCLASS ETW session and invokes Windows \`pnputil /restart-device\` on **only that exact child InstanceId**; there is no \`/deviceid\` broad match, no \`/reboot\`, no \`/remove-device\`, no host Bluetooth adapter change, no re-pair. It then reads local status, sanitizes ETW match counts and Kernel-PnP 411 statuses. The ETL stays on the owner's PC and must not be posted publicly.

**Administrator Windows PowerShell**, explicitly opting into a targeted HID child restart:

\`\`\`powershell
cd "C:\Users\무조림.000\Desktop\코딩\01_working\siri-remote-for-windows"
git fetch origin main
git show origin/main:tools/windows-probe/hidclass_restart_trace.ps1 |
    Set-Content "$env:TEMP\hidclass_restart_trace.ps1" -Encoding UTF8
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\hidclass_restart_trace.ps1" -Execute
\`\`\`

The trace reinitializes only the already-failed remote HID child; Windows PnP restart may still fail or leave Code 10 unchanged. If no event matches, the absence remains inconclusive; consider a more targeted **WPP HIDCLASS parser** trace or obtaining the true report map from a controlled bonded Linux session **only if** separately justified, rather than restarting the whole adapter or deleting the Windows pairing. [Microsoft PnPUtil command syntax](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-command-syntax) documents \`/restart-device <instance ID>\`.

## 2026-10-17 confirmed failed A2854 HIDCLASS ETW event during targeted PnP restart

The owner executed the opt-in \`hidclass_restart_trace.ps1 -Execute\` against the unique **Code 10 / mshidumdf / 0xC00000B9** HID child of the Siri Remote. Actual sanitized output:

\`\`\`text
Unique remote anchor found; matching failed HID child nodes: 1
Before: HID child Code 10; NTSTATUS=0xC00000B9
Targeted PnP restart command exit code: 0
After: HID child problem code=10; NTSTATUS=0xC00000B9
Trace: HIDCLASS event count=13
Trace: Event 3 v2 records exactly matching failed HID child=1
  DeviceStartStatus=0xC00000B9; events=1
Kernel-PnP 411 query unavailable or no events were found.
\`\`\`

**This is the first HIDCLASS ETW event confirmed to belong to the A2854 failed HID node**, not one of the 10 unrelated successful HID rundown events from the earlier trace. \`pnputil /restart-device\` returned success at the command level; the target HID driver start still failed with the same status. The Kernel-PnP 411 lookup returned no usable result, but the exact-attributed HIDCLASS failure event is independently sufficient to establish HID initialization failure. This does **not** yet prove malformed descriptor bytes (nor that this error causes the user-visible idle BLE disconnection).

Crucially, HIDCLASS Event ID 3 v2's manifest includes \`ReportDescriptorLength\` and \`ReportDescriptor\` binary fields. The new safe local offline tool [\`tools/windows-probe/hidclass_extract_descriptor.ps1\`](../../tools/windows-probe/hidclass_extract_descriptor.ps1) checks the newest locally retained \`siri_hid_restart_*.etl\`, identifies the unique Siri Remote \`mshidumdf\` HID node, and matches event \`DeviceInstancePath\` **exactly** in process memory. Only if there is exactly one matching event with \`DeviceStartStatus=0xC00000B9\` and an internally coherent nonzero descriptor byte length will \`-SaveBinary\` create a new local \`%TEMP%\a2854_hidclass_report_map.bin\` (never overwrite). Nothing else is read from a BLE service, and no device is restarted. If descriptor data is absent/truncated/unsupported, the script leaves it untouched and reports an inconclusive local diagnostic.

Run in administrator PowerShell:

\`\`\`powershell
cd "C:\Users\무조림.000\Desktop\코딩\01_working\siri-remote-for-windows"
git fetch origin main
git show origin/main:tools/windows-probe/hidclass_extract_descriptor.ps1 |
    Set-Content "$env:TEMP\hidclass_extract_descriptor.ps1" -Encoding UTF8
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\hidclass_extract_descriptor.ps1" -SaveBinary
\`\`\`

If the output says a validated binary was saved, inspect the local copy:

\`\`\`powershell
py -3 tools\protocol\report_map.py --inspect "$env:TEMP\a2854_hidclass_report_map.bin"
\`\`\`

\`report_map.py --inspect\` provides a **basic** HID item/collection structure check, not a formal Windows parser equivalent. Even a clean structure would not rule out a semantic incompatibility causing \`STATUS_COULD_NOT_INTERPRET\`. Never upload the raw ETL, PnP instance ID, or raw report descriptor to a public repo without review. The script prints numeric status, descriptor byte count, extraction status, and a proposed local offline command, not the private content.

## 2026-10-17 breakthrough: 38-byte descriptor extracted from exact failed Windows HID child

The owner ran `tools/windows-probe/hidclass_extract_descriptor.ps1 -SaveBinary` against the locally retained **targeted HID restart** ETL, not against the earlier other-HID rundown trace. Actual sanitized output:

```text
Targeted restart ETL and unique Siri Remote HID child found.
Exactly matching HIDCLASS Event 3 v2 records: 1
HIDCLASS DeviceStartStatus: 0xC00000B9
HIDCLASS declared ReportDescriptorLength: 38
ReportDescriptor binary source: EventBinary; actual bytes=38
Descriptor length and actual bytes MATCH for the exact failed Siri Remote HID node.
Saved validated local binary under TEMP as a2854_hidclass_report_map.bin
```

**Confirmed:** one ETW event correlates to the exact `mshidumdf` HID child beneath the paired Siri Remote. The event itself reports `DeviceStartStatus=0xC00000B9` and contains a **38-byte** binary `ReportDescriptor` whose actual byte count matches the event's declared `ReportDescriptorLength`. This was saved only to local `%TEMP%\a2854_hidclass_report_map.bin`; neither descriptor contents nor raw ETL nor PnP IDs were published.

**Not confirmed:** 38 bytes may be the transport-supplied complete GATT `0x2A4B` Report Map, a Windows/HIDCLASS view of the descriptor, or a partial/alternative representation. The exact mechanism and why the HID parser rejects/does not start this device are still under investigation. Do not assume the short length alone establishes truncation or corruption. The prior known Linux Report Map `0x2A4B` characteristic existed but was never successfully read in full.

**Immediate offline next step** (does not modify device or driver):

```powershell
cd "C:\Users\무조림.000\Desktop\코딩\01_working\siri-remote-for-windows"
py -3 tools\protocol\report_map.py --inspect "$env:TEMP\a2854_hidclass_report_map.bin"
```

The inspector reports length, hash, report IDs, and *basic* HID item/collection warnings. It does **not** fully reproduce Windows HIDCLASS's parser nor prove USB/BLE HID compliance. Investigate the inspector output before any further tracing or re-pairing. Keep descriptor binary off public GitHub until reviewed.

## 2026-10-17 first offline analysis of captured A2854 38-byte HIDCLASS descriptor

The owner inspected locally extracted `%TEMP%\a2854_hidclass_report_map.bin` with `tools/protocol/report_map.py --inspect`. Observed:

```text
Report Map length: 38 bytes
SHA-256: 6f9795ae87c7bcfcba0d50c38cbde6abf5163f78e1299cc23dc110795bc57f49
Report IDs declared: 0x04, 0xB8
Maximum collection depth: 1
HID item count: 16
No BASIC item-boundary/collection problems found.
```

The existing **basic** structural inspector found no obvious item-overrun, unclosed Collection, or invalid Report ID zero. This **does not validate HID parser semantics**: Windows still reports `mshidumdf` Code 10 and exact-attributed HIDCLASS `DeviceStartStatus=0xC00000B9`.

Worth following up: the captured 38-byte HIDCLASS descriptor declares IDs `0x04` and `0xB8`, whereas previous **Linux observed input** involved reports `0xFB` (buttons) and `0xFC` (touch) after enabling input. The discrepancy is a **hypothesis-generating observation only**; HIDCLASS may have captured an alternate collection/representation or a subset, and GATT characteristic Report Reference IDs differ from report-map contents. Do not assert report-map truncation or corruption merely because IDs differ.

The next minimal offline diagnostic is to examine the 38 bytes locally, ideally printing only the compact hex sequence (no device path or MAC):

```powershell
cd "C:\Users\무조림.000\Desktop\코딩\01_working\siri-remote-for-windows"
py -3 -c "import os,pathlib; b=(pathlib.Path(os.environ['TEMP'])/'a2854_hidclass_report_map.bin').read_bytes(); print(' '.join(f'{x:02X}' for x in b))"
```

Determine exact HID item semantics (Usage Page/Usage, collection, Report Size/Count, Input/Output/Feature) from the actual sequence. Microsoft [HidP_GetCollectionDescription](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpddi/nf-hidpddi-hidp_getcollectiondescription) documents `STATUS_COULD_NOT_INTERPRET` as a possible descriptor parsing error, but ETW's status does not yet pinpoint a specific offending item.

## 2026-10-17 byte-level decoding of Windows-captured 38-byte A2854 HID Report Descriptor

The owner inspected the local, exact-remote-correlated 38-byte `%TEMP%\a2854_hidclass_report_map.bin` (SHA-256 `6f9795ae87c7bcfcba0d50c38cbde6abf5163f78e1299cc23dc110795bc57f49`). A direct offline hex dump reveals **one vendor-defined top-level application collection** and two report IDs.

The complete descriptor binary stays on the owner's machine; this note deliberately lists only minimal relevant item subsequences and semantics, not all 38 raw bytes.

| Byte offset (zero-based) | Bytes | Meaning |
| --- | --- | --- |
| 0 | `06 00 FF` | Usage Page vendor-defined `0xFF00` |
| 3 | `09 0B` | Local Usage `0x0B` applied to following Application Collection |
| 5 | `A1 01` | Collection(Application) |
| 7–10 | `06 00 FF 09 0B` | Vendor Page `0xFF00`, local Usage `0x0B` |
| 12–14 | `15 00 26 FF 00` | Logical min 0, max 255 |
| 17 | `75 32` | **Report Size = 0x32 = 50 bits** (NOT 32 decimal) |
| 19 | `96 06 00` | Report Count 6 |
| 22–24 | `85 B8 B2 01 00` | Feature report ID `0xB8`; Feature flags `0x0001` = Constant/Array/Absolute, bit-field |
| 27–34 | `85 04 75 08 96 11 00 82 22 01` | Input report ID `0x04`; 8-bit cells ×17; flags `0x0122` = Data/Variable/Absolute/No Preferred State/**Buffered Bytes** |
| 37 | `C0` | End Collection |

Widths derived from the descriptor: ID `0xB8` Feature has **300 payload bits** (6×50), rounded up to **38 payload bytes** (+1-byte report ID = **39 bytes**); ID `0x04` Input has 17×8 = **136 payload bits / 17 bytes** (+1-byte report ID = **18 bytes**).

### Potential incompatibilities, ranked for investigation—not established causal defects

1. **Missing local Usage for Data/Variable Input**: The second `09 0B` Usage is consumed by the preceding **Feature** main item. Under HID local-state rules, Local items reset after each Main item; **no new Usage or Usage range** appears before `82 22 01` Input. Vendor-defined collection Usage does not by itself supply a local Usage for this variable input. This is semantically suspicious; whether `HidP_GetCollectionDescription` specifically rejects it is unknown.
2. **Unusual 50-bit fields on constant Feature**: `0x75 0x32` creates 6×50 bits, i.e. a 300-bit, non-byte-aligned Constant Feature payload. HID fields can be bit-packed, so non-byte alignment or report size >32 **alone is not sufficient proof of invalidity**. A 50-bit size deserves a *Windows-specific* compatibility test.
3. **Report ID mismatch with prior Linux event notifications**: This descriptor has `0x04` Input and `0xB8` Feature only, while earlier Linux session handled vendor reports `0xFB` (buttons) and `0xFC` (touch). Do not assume the event descriptor is the complete over-the-air GATT `0x2A4B` map or that the difference causes start failure without a raw GATT capture and Report Reference correlation.

The successful basic item/collection framing check was **not** a semantic HID parser test. The contemporaneous exact-node Windows HIDCLASS ETW `DeviceStartStatus=0xC00000B9` remains proven, while the offending byte or precise Windows parser rule remains **unproven**. There is no evidence of a BLE bond problem in Windows's successful 7-service GATT discovery; prior `ACCESS_DENIED` against HID characteristics may reflect OS ownership and need not indicate transport corruption.

**Proposed next investigation**: Reproduce HID parser acceptance **offline** with alternate, isolated descriptor variants (one change at a time: add an explicit Input Usage; adjust 50-bit constant Feature size/count) under a supported Windows HID test driver/VM—not by patching the live remote, injecting reports into Windows, or circumventing Bluetooth HID access controls. Alternatively obtain a safe, locally redacted HIDCLASS **WPP parser error offset** if supported to identify the actual failing item. Only after identifying the defect should a compatibility architecture (driver, corrected virtual HID, or another supported input mechanism) be chosen.

References: [USB-IF HID specification](https://usb.org/hid), [WebHID descriptor parsing description / local item reset](https://hid.spec.whatwg.org/), [Microsoft HidP_GetCollectionDescription](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpddi/nf-hidpddi-hidp_getcollectiondescription), [Windows capability arrays](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/value-capability-arrays).

## Follow-up: controlled offline HID descriptor variants (2026-10-17)

See [HID_DESCRIPTOR_VARIANT_EXPERIMENT.md](HID_DESCRIPTOR_VARIANT_EXPERIMENT.md) for the eight isolated variants, local-only generation commands, and independent Windows CI tests with a synthetic descriptor. All eight synthetic shapes parsed in hid-parser 0.1.0; a single local Usage yielded an inferred 1-byte Input instead of 17 bytes, while an explicit Usage range preserved 17 bytes. This **does not** demonstrate Windows HIDCLASS acceptance or identify the exact reason for Code 10. The A2854 original .bin has not been committed or uploaded.

## Alternative research branch

If reusing BlueZ is impractical, check Windows driver diagnostic providers and HIDCLASS ETW/WPP availability without touching live driver bindings, before any trace capture. Raw traces may contain addresses/serials; do **not** upload raw ETL or setupapi.dev.log publicly without redaction. Whatever the approach, first validate the descriptor hypothesis before attempting a virtual HID or proprietary transport workaround.
