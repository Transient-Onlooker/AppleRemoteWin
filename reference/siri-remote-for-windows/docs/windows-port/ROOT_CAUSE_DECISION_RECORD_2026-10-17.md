# A2854 on Windows — Root-Cause Decision Record (2026-10-17)

## Executive decision

STOP treating repeated Bluetooth re-pairing, driver reinstallation, WinRT AccessDenied, or short-lived Windows Settings connection labels as the leading debugging path. The owner has now proved a real HIDCLASS driver-start failure on the exact Siri Remote child, independent of successful BLE GATT discovery. Future work requires a discriminating native HID parser trace or an isolated native parser test. No arbitrary remediation on the user's primary Windows installation.

Maintain two tracks: (A) diagnose and, if supportable, repair the Windows-native HOGP binding; (B) deliver usable Windows input via an independently controlled BLE central + a Windows user-mode receiver if A is disproportionately invasive.

## Facts we can actually prove

| Layer | Reproducible evidence | Conclusion |
| --- | --- | --- |
| LE radio, bonding and GATT | WinRT paired device; 7 services; uncached GATT discovery SUCCESS; CONNECTION=CONNECTED | BLE link and basic GATT discovery work. A 5-second Settings disconnection is not evidence of a broken radio. |
| Windows HID 0x1812 GATT access | HID service visible, characteristic enumeration ACCESS_DENIED | A user-mode app cannot access this HID service in the tested state. Do NOT infer the reason (OS ownership vs another access rule). |
| PnP device | One BLE HIDClass child of unique DL1 remote; mshidumdf, Code 10, NTSTATUS 0xC00000B9 | Native HID child initialization fails. |
| Kernel-PnP | Exact-node event 411 after fresh pairing | Failure is not merely a stale pre-pairing device node. |
| Native HIDCLASS ETW | Target-only pnputil restart returned 0; 13 events, exactly one ID3/v2 for failing node with DeviceStartStatus 0xC00000B9 | The failure is recorded at the HIDCLASS layer, not merely inferred from Settings. |
| Binary in failing event | ReportDescriptorLength=38; EventBinary=38 bytes | A 38-byte descriptor was submitted to HIDCLASS for this node. It is not yet independently verified as the full over-the-air GATT 0x2A4B map. |
| Linux prior working experiment | BlueZ without input/hog plugin received button 0xFB and touch 0xFC after input-enable 0xAF | The remote has working input reports with a permissive BLE central; DOES NOT guarantee Windows HOGP support. |

## Exact offline descriptor structural facts

The user's private original descriptor (38 bytes, SHA-256 6f9795ae87c7bcfcba0d50c38cbde6abf5163f78e1299cc23dc110795bc57f49) has a vendor-defined Application Collection; Feature Report ID 0xB8 with ReportSize 0x32 (50 bits) and ReportCount 6; then Input Report ID 0x04, 17 x 8-bit, with Buffered Bytes set. It closes the collection correctly. Basic item framing is valid.

CRITICAL: 50 * 6 = 300 bits, which has remainder 4 modulo 8. The Feature payload is NOT byte-aligned. It needs four CONSTANT padding bits to reach 304 bits; this does not alter its rounded payload byte count (38 bytes).

CRITICAL: the Local Usage before the Feature main item does not carry over to the later Input main item. Input is Data/Variable (nonconstant), with no fresh Local Usage declaration. An explicit Usage Minimum/Maximum covering the 17 variable fields is one defensible *hypothesis fix*; a single Usage is NOT equivalent (the independent parser inferred 1 byte rather than 17).

Microsoft's HID troubleshooting guide specifically enumerates (1) 'Report wasn't byte aligned' and (2) 'A nonconstant main item was declared without a corresponding usage' as Code 10 descriptor parser errors. This article's examples are for HID-over-I2C; shared HIDCLASS parser behavior for this particular BLE device still needs direct confirmation. See https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/troubleshoot-hid-over-i2c-device-issues

Do not mistake a clean generic HID parser run for Windows acceptance. A separate Python HID parser constructed even the unaligned/missing-Usage synthetic original. The previous 50x6 -> 25x12 experiment retained the 300-bit misalignment, so it was incapable of testing the alignment hypothesis.

## Ranked hypotheses and falsification tests

| Priority | Hypothesis | Why plausible | What would disprove/confirm it |
| --- | --- | --- | --- |
| H1 - HIGH | HIDCLASS rejects 300-bit Feature as not byte-aligned | Proven 300%8=4; explicit Microsoft Code 10 error category | Native WPP parser offset/message in Feature section; or Windows native isolated parser accepts padded but rejects original, all else equal. |
| H2 - HIGH | HIDCLASS rejects missing Usage for Data/Variable Input | Proven Local Usage reset; explicit Microsoft Code 10 error category | Native WPP points at Input; or isolated parser starts when Usage Min/Max added but not otherwise. |
| H3 - MEDIUM | HIDCLASS 38-byte map or Report Reference layout does not reflect full GATT HID reports | Exact captured map declares 0x04 and 0xB8; prior Linux input IDs include 0xFB, 0xFC | One controlled raw LE GATT 0x2A4B + 0x2908 map (unmodified) matched against ETW map and report characteristic declarations. |
| H4 - LOW-MEDIUM | Windows BLE Report Map read/truncation/cache/auth behavior | Nordic has documented older Windows HOGP Report Map truncation caused by MTU handling and Driver Error; not evidence for this machine | Compare GATT raw 0x2A4B hash+length from independent trusted BLE central to 38-byte ETW; inspect BLE/ATT trace only if mismatch. |
| H5 - LOW | 50-bit field width or Buffered Bytes semantics unsupported by Windows | Unusual declarations, but no direct parser trace evidence | Isolated WDK driver test varying these alone after addressing H1/H2. |

## Scientific experimental matrix — now implemented

tools/protocol/byte_alignment_matrix.py operates OFFLINE on the owner's local 38-byte descriptor, never writes to the remote or changes any driver. It derives eight controlled descriptors: original; Usage range only; 4-bit padding only; Usage+padding; width 25x12+padding; Usage+width+padding; Usage+padding+Buffered Bytes off; all four changes. It validates total Feature bits (300 or 304), unchanged Input 136 bits, structural framing, and local Usage state.

Insert Constant Feature padding (Report Size 4, Report Count 1, Feature Constant): 75 04 95 01 B1 01, *after* the original Feature main item and *before* the next Report ID. This extends descriptor bytes by six without changing the rounded Feature report payload length. Insert local Usage range 19 01 29 11 before the Input main item as an independent test. Neither edit is proven safe to deploy to the real device, particularly if the remote's actual GATT Report References are inconsistent.

CI tests are synthetic report-geometry fixtures, not the user's raw remote bytes. An independent pure-Python HID parser constructs all synthetic variants and does not reproduce the native failure. Therefore a native parser oracle is essential.

## Phase gates: what to do, when to stop

### Gate 1 — done: capture HIDCLASS failure and report bytes

Reproducible result: exactly correlated DeviceStartStatus 0xC00000B9 with 38-byte ETW descriptor. Stop re-pairing/restarting to 'see if it changes.'

### Gate 2 — required: obtain *native* parser error reason without altering the hardware

Option 2A (preferred): Use Microsoft's documented BusesTrace/WPP HIDCLASS capture workflow in a bounded, local-only session around the one already validated target-only PnP restart. Use WPA symbols to inspect only the failing target's parser message and byte offset. Manifested Event 3 v2 supplied status+bytes but NOT the parser error reason. The Microsoft example for HID-over-I2C specifically states that HIDCLASS WPP can include the error byte offset; don't promise that BLE trace will necessarily expose it. See https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/troubleshoot-hid-over-i2c-device-issues . Do not publish raw ETL, Windows instance paths or full trace messages.

Option 2B (controlled alternative): Use the official WDK vhidmini2 HID minidriver sample on a disposable Windows VM. Feed one of the isolated descriptors at a time and let *Windows' actual HIDCLASS/hidparse* accept or reject it. Verify normal baseline first; compare original vs padding-only vs Usage-only vs both. Do NOT enable test signing, modify HVCI/security, or install test drivers on the primary Windows host. Kernel/user HID minidriver sample: https://learn.microsoft.com/en-us/windows-hardware/drivers/wdf/creating-umdf-hid-minidrivers

PASS criteria: a reproducible, specific Windows parser message/offset or an isolated Windows-native accept/reject matrix. FAIL/STOP criteria: only generic Code 10, generic independent parsers, or HID GATT AccessDenied without a discriminator. Do not repeat those experiments.

### Gate 3 — determine whether this is an entire HOGP layout issue

If Windows parser acceptance is resolved, compare 0x2A4B over-the-air Report Map to the ETW 38-byte hash. Also collect for every Report characteristic its 0x2908 Report Reference (ID/type) and input enable Feature mapping, from ONE trusted independent BLE-central session if necessary. Previous BlueZ successful input IDs 0xFB/0xFC versus ETW 0x04/0xB8 suggest a gap that cannot be resolved by changing bytes at random. The Bluetooth HID-over-GATT profile defines this report-ID/characteristic relationship; the Windows WinRT GATT denial does NOT prove the remote lacks the characteristics. See https://www.bluetooth.com/specifications/specs/hid-over-gatt-profile-hogp/

A dated Nordic field report describes Windows 10 reading only an early Report Map chunk under a particular MTU implementation; modern Windows and Apple Siri Remote MUST NOT be assumed to have this problem. See https://devzone.nordicsemi.com/f/nordic-q-a/40874/windows-10-driver-error-with-ble-hid-devices

PASS criteria: GATT 0x2A4B bytes and 0x2908 ID/types or proof that access is unavailable on stock Windows. Do not reset Windows BLE bonding merely to collect another generic failed GATT read.

### Gate 4 — choose implementation, not another patch loop

| Route | Does Siri Remote work on Windows without a custom kernel driver? | Trade-off |
| --- | --- | --- |
| Stock Windows HOGP | ONLY if firmware/service layout already compatible or can be externally corrected | Best integration, but Apple firmware cannot reasonably be patched; current Code 10 prevents native HID input. |
| Target-specific Windows lower HID filter/transport minidriver | No — requires signed Windows driver | Can theoretically override a malformed HID descriptor but brings signing, hardware-specific PnP matching, deployment/security and potential report translation work. Don't build before Gate 2+3. OSR specialists confirm this is possible but complex: https://community.osr.com/t/workarounds-for-bad-hid-report-descriptors/59724 |
| Pure WinRT app on existing HID 0x1812 | Not presently demonstrated | The tested HID characteristic enumeration returned ACCESS_DENIED. Re-running it is not a workable product plan. |
| Independent BLE central + Windows user-mode receiver (preferred FUNCTIONAL fallback) | YES for the Windows RECEIVER; BLE pairing resides on a separate host/controller | Prior Linux BlueZ session already proved input 0xFB/0xFC reception after 0xAF enable. Can feed a narrowly scoped IPC protocol into a native Windows keyboard/mouse user-mode app. Needs another Bluetooth controller/host and a deliberate single new pairing. |
| ESP32 BLE central + USB serial bridge + native Windows app | Potentially; firmware engineering and BLE pairing still unproven on this board | Avoids a Windows kernel driver and avoids sharing Windows Intel AX200 with WSL; the ESP32-S3 native USB is optional — an ESP32 WROOM USB-UART serial bridge can send data to Windows COM as a prototype. |
| Windows Virtual HID Framework (VHF) | No — VHF source is kernel-mode KMDF/WDM | Useful only if a real virtual HID keyboard/mouse is required (e.g., access by arbitrary software). The simpler Windows user-mode SendInput mapping may suffice. https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/virtual-hid-framework--vhf- |

**Recommendation:** Keep native-Windows root-cause research and end-user functionality as separate milestones. If native Gate 2 reveals one specific parser rule and Gate 3 confirms internally consistent GATT references, evaluate a target-specific signed driver. Otherwise ship a Windows receiver + independently controlled BLE transport first. Do NOT delete/change the built-in Intel AX200 driver, patch signed system binaries, blanket-disable Windows HID drivers, or switch off Windows driver protections.

## Acceptance criteria for usable Windows input

Button presses and releases correctly decoded, no stuck key on BLE disconnect; mouse/touch coordinates and gesture semantics validated against known Linux fixtures; reconnect after idle; no raw mic capture by default; bounded resource use; no interference with other Bluetooth devices; Windows installer or executable avoids administrator privileges except for driver-specific experimental track. Tests should include reconnect and repeatability, not only 'connected' status.

## References

1. https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/troubleshoot-hid-over-i2c-device-issues
2. https://learn.microsoft.com/en-us/windows-hardware/drivers/wdf/creating-umdf-hid-minidrivers
3. https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/virtual-hid-framework--vhf-
4. https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-low-energy-overview
5. https://www.bluetooth.com/specifications/specs/hid-over-gatt-profile-hogp/
6. https://devzone.nordicsemi.com/f/nordic-q-a/40874/windows-10-driver-error-with-ble-hid-devices
7. https://community.osr.com/t/workarounds-for-bad-hid-report-descriptors/59724
8. https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/hid-transports


## 2026-10-17 WPR verbose HIDCLASS trace negative control: no matching providers

The owner's Windows 11 build 26200 successfully ran the pinned Microsoft
InputOnlyWithVerboseWppProfile through `wpr -start`, restarted **one uniquely
correlated Siri Remote HID child** (`pnputil` exit code 0), and stopped WPR
successfully. The resulting ETL was 10.38 MiB; HID child remains `mshidumdf`
Code 10 / `0xC00000B9`. However WPA showed **no WPP Trace graph**.

Reading the copied ETL from the restored user's normal TEMP, Get-WinEvent
returned 93,075 events with **all ProviderName empty**, which means the naive
zero-provider counts were not trustworthy. A follow-up read-only `tracerpt`
provider GUID summary processed **93,076 events**, **zero lost**, recognized
**22 GUID/event-table rows**, and found **zero exact provider GUID rows** for:

- HIDCLASS WPP: `47c779cd-4efd-49d7-9b10-9f16e5c25d06`.
- HIDCLASS manifested ETW: `6465da78-e7a0-4f39-b084-8f53c7c30dc6`.

The WPR trace did not demonstrably record either target provider even though
the static pinned XML lists both in `InputOnlyWithVerboseWppProfile`.
Thus **ETL file success and no dropped events are not evidence of successful
HIDCLASS WPP provider activation or emitted parser messages**. A pre-existing
unrelated `logman` ETL DID contain an exact correlated HIDCLASS manifested
Event ID 3 v2 with `DeviceStartStatus=0xC00000B9`, so HIDCLASS emits such
events under at least one collection configuration on this host.

**GATE DECISION:** stop investigating WPA layouts and do not repeat the same
WPR capture. Directly compare parser acceptance with a virtual HID device
in an isolated Windows **test VM** (no driver/signing/security changes to
the real host). Before any new WPR attempt, there would have to be a
demonstrably different provider-enablement mechanism plus a live collector
`wpr -status collectors -details` preflight; simply restarting the remote
again is not a useful experiment.

Microsoft official starting point:
https://github.com/microsoft/Windows-driver-samples/tree/main/hid/vhidmini2
(UMDF 2 sample; its source can load a report descriptor from a registry
test setting rather than only the compiled default descriptor). Follow
sample instructions in disposable VM and prove sample default starts
successfully before evaluating one A2854 variant per boot/test. Compare:
`original`, `padding_only`, `usage_range_only`, `usage_plus_padding`.
Retain Code 10/NTSTATUS and success per variant. One change at a time,
no live Bluetooth pairing/firmware involvement.

References: [Microsoft UMDF 2 virtual HID minidriver](https://github.com/microsoft/Windows-driver-samples/tree/main/hid/vhidmini2),
[WPR status collectors and provider list](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/wpr-command-line-options),
[HIDCLASS WPP parser trace example](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/troubleshoot-hid-over-i2c-device-issues).
