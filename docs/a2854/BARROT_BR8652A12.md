# Axxen / Barrot BR8652A12 USB Bluetooth dongle — hardware investigation

**Status (2026-10-21): PHYSICAL ADAPTER ENUMERATED ON WINDOWS; BARROT STOCK DRIVER FAILS CODE 31. Native WinUSB/HCI operation remains untested.**

The owner reports purchasing the available **Daiso Axxen Bluetooth 5.3** USB adapter after the older Realtek 5.0 product was no longer available, and identifies its chipset as **Barrot / BRLink BR8652A12**. This is **not** the BR8651 chip mentioned as a guess in earlier notes. The claim is owner-provided product/chip identification, **not yet confirmed by USB descriptor or HCI controller response**.

## Vendor and third-party evidence

| Evidence | What it establishes | What it does **not** establish |
| --- | --- | --- |
| Barrot's [Bluetooth chip catalogue (PDF)](https://www.barrot.com.cn/uploads/files/b397caf7c62556f241476fd31b5b3b02.pdf), page 2 | BR8652A12 is a dual-mode Bluetooth controller with UART/USB host interface, full-speed USB and Windows driver coverage in the historical table | Does not identify this specific Axxen dongle's USB ID or prove WinUSB works. The historical table lists Bluetooth **5.0**, so spec versions vary between documentation |
| [Shenzhen HT-Blue support page](https://ht-blue.com/service.aspx?id=14) | Lists BR8652A12 as **Bluetooth 5.3 / LMP12.x** and a BRLink driver package; Windows 8.1/10/11 are described as driver-free | Another vendor's firmware/driver package is **not** automatically safe or necessary for the purchased device |
| [Jiangxi BTA-V53 user manual](https://manuals.plus/jiangxi/bta-v53-bluetooth-usb-adapter-manual) | A separate USB dongle product explicitly states BR8652A12 and Bluetooth 5.3; Windows 8/10/11 plug-and-play | This is **not** a report that the Axxen unit shares the same USB VID:PID/firmware |
| [Axxen BD20/BD21 support notice](https://m.axxen.co.kr/?act=board&bbs_code=faq&bbs_mode=view&bbs_seq=89&page=1) | Manufacturer says the listed BD20/21 units can use Windows 10/11 built-in support | Confirm the exact Axxen package/model before assuming it is BD20/BD21 |
| [Barrot HCI stream Linux quirk](https://www.spinics.net/lists/linux-bluetooth/msg122600.html) | Some *other* Barrot USB devices, **33FA:0010 / 33FA:0012**, append a stray byte to an HCI Read Local Extended Features response, causing initialization failures without a workaround | Does **not** prove BR8652A12 shares the bug, nor prove this unit uses any particular USB ID |
| [Barrot HCI reverse-engineering project](https://github.com/zhuzhuzihan/brtusb) | Prior art for investigating transport and initialization quirks in Barrot USB HCI controllers | No confirmed native Windows A2854 compatibility for the purchased dongle |

### Important version discrepancy

Barrot's older catalogue lists **BR8652A12 = Bluetooth 5.0 Dual Mode**; other documents identify BR8652A12 products as **Bluetooth 5.3 / LMP 12.x**. Treat this as documentation/firmware/version ambiguity, not proof of counterfeit hardware. Resolve by reading HCI `Read Local Version Information` **after** safe controller access is established. Bluetooth 5.3 is not required for A2854 BLE HID testing.

## 2026-10-21 native Windows physical device results (local agent)

User's local AI agent inspected the actual Axxen USB dongle on Windows 11 with **read-only** PnP commands and reported:

| Attribute | Measured result |
| --- | --- |
| External dongle USB ID | **33FA:0001**, USB revision **8891** |
| USB manufacturer/product strings | **Barrot Technology Co.,Ltd.** / **BARROT Bluetooth Adapter** |
| USB compatible ID | **`USB\\Class_E0&SubClass_01&Prot_01`** (Bluetooth HCI class; endpoint details not yet measured) |
| Windows driver | Service **BTHUSB**, installed INF **`oem80.inf`** (original `brbtusb.inf`), driver version **21.46.25.278** |
| Device status | **Error**, Windows **Code 31 / `CM_PROB_FAILED_ADD`**, `ProblemStatus=0xC0000001` |
| USB topology | Under external **2109:2815** USB hub; USB endpoints/packet sizes/intervals not collected |
| Driver ranking | `oem80.inf` ranked ahead of Windows inbox `bth.inf` |
| Built-in Intel AX200 | USB **8087:0029**, `BTHUSB` / `oem116.inf` / **23.160.0.9**, **OK**, Code 0 |
| C# code validation | .NET SDK **10.0.402**, **90/90 unit tests passed**, A2854.Probe Release build **0 warnings, 0 errors** |
| Git HEAD at time of hardware report | `609395486373e70aa061d9c55d34256e1af711b7` |

The reported 90/90 test result verifies **code as of commit 6093954**, before later 33FA USB transport-specific changes. It does not establish native A2854 pairing/input.

**Interpretation:** Code 31 is a **failure to start the Windows Barrot driver/device node**; it does **not** prove the USB device hardware is defective. The USB Class E0/01/01 compatible ID supports trying the standard HCI USB (H:2) transport after the *external device only* is bound to WinUSB, but it does not prove the missing interrupt/bulk endpoints or firmware readiness. Microsoft Update Catalog [lists the same Barrot 21.46.25.278 package for 33FA:0001](https://www.catalog.update.microsoft.com/Search.aspx?q=USB%5CVID_33FA%26PID_0001). This is a source-backed driver identity check, not a fix for Code 31.

### BR8652A12 USB HCI command quirk — code prepared, not hardware proven

The [brtusb reverse-engineered Linux driver](https://github.com/zhuzhuzihan/brtusb/blob/main/linux/brtusb.c) explicitly recognizes **33FA:0001** and uses **HCI EP0 `bmRequestType=0x21`** (class/interface, request 0), whereas the original Realtek code used 0x20 (class/device). This is relevant **only after WinUSB access**, not an explanation for Windows Code 31.

The C# fork adds `UsbHciTransportQuirks.CommandRequestType` with a narrow VID/PID rule for **33FA:0001/0010**, leaves all other adapters on the original 0x20 path and adds deterministic unit tests. The A2854 console accepts **`--usb 33FA:0001`** to ensure it selects the intended dongle and logs its EP0 setup type. Changes require fresh local Windows build/tests and actual HCI control response validation.

### Safe next experiment

1. Capture **only the dongle** device setup failure context (`pnputil /enum-devices /instanceid ... /drivers /properties` and SetupAPI device-install log excerpts); do not dump unique instance paths into the repository.
2. **Export `oem80.inf` via `pnputil /export-driver oem80.inf <backup-directory>`**, verify the backup is readable, and record rollback choices. This writes a local backup but **does not change any driver binding**. Do not delete vendor INF or assume driver restoration succeeds without validation.
3. Optionally plug the dongle directly into a laptop USB-A port instead of the **2109:2815** hub and recheck Code 31; no driver changes. A failure behind a hub does not imply the hub caused it.
4. If the external dongle is still Code 31, plan an explicitly **reversible WinUSB binding for 33FA:0001 only**, then HCI Reset/Read Local Version before engaging BLE scan and pairing. Preserve built-in **8087:0029**.
5. Never apply the unrelated Realtek firmware patch to Barrot hardware. If HCI fails, report the exact timeout/status and read-only descriptors before controller-specific workarounds.

## Windows-native project implications

- Existing `src/AppleRemote.Windows/UsbHci.cs` opens any eligible **WinUSB-bound** USB Bluetooth HCI interface 0 with class/subclass/protocol `E0/01/01`, independent of VID/PID, and discovers interrupt/bulk endpoints. This means the Barrot VID/PID need not be hard-coded *if* USB descriptors and WinUSB binding are compatible.
- Existing `src/AppleRemote.Core/Realtek.cs` does a generic HCI Reset / Read Local Version before applying an **RTL8761B-specific** ROM patch; the Realtek patch condition is guarded by Realtek manufacturer/version. **Never force Realtek firmware onto Barrot hardware.**
- `tools/A2854.Probe` cannot use the normal Windows `BTHUSB` driver via `UsbHci.Open()`. An actual eligible adapter assigned **WinUSB** is required. This stage has not been reached on BR8652A12.
- Some Barrot controllers have HCI event-length or initialization quirks (e.g. another model's `Read Local Extended Features` extra byte). Diagnose actual HCI logs and USB ID before introducing controller-specific workarounds.
- During initial diagnostics **do not disable or replace the built-in Intel AX200/9560 driver**, install unsigned vendor packages, change registry, enable test-signing, or overwrite the dongle firmware. Stock Windows enumeration and driver/property reads are sufficient for the next step.

## Next local Windows preflight (read-only)

1. Plug the Axxen dongle into a USB-A port on the **Dell Windows 11** machine. Enumerate present USB/PnP devices, distinguishing built-in Intel `8087:0029` from the newly attached dongle; inspect **USB VID:PID**, friendly name, hardware/compatible IDs, PnP class, driver service and INF/provider.
2. Identify *USB interfaces*, not merely the brand name; determine whether the adapter exposes standard Bluetooth USB HCI class `E0/01/01` and control/interrupt/bulk endpoints. Windows PnP compatible IDs may help; if descriptor detail requires additional tools, ask before installing anything.
3. Run the current C# `dotnet test tests/AppleRemote.Tests -c Release` and `dotnet build tools/A2854.Probe/A2854.Probe.csproj -c Release`; simple build/test bugs may be resolved by the local agent.
4. Return only **redacted** VID/PID, USB interface/endpoint facts, driver information and test results. The USB **VID/PID is public model information** and can be shared; unique instance-path suffixes, serial numbers, MAC addresses and pairing secrets must stay private.
5. Only after the transport interface is verified, plan how to **restore the external dongle's original driver** and consider a scoped WinUSB binding. Do not attempt driver replacement in this preflight.

### Decision gate

**If** the dongle is WinUSB-capable: test HCI Open → Reset/Read Local Version → LE scan → connect → SMP encryption → HID GATT / Report Reference → `0xF0=0xAF` → physical `0xFB`/`0xFC` input.

**If not**: do not infer that the remote is incompatible. Report the actual blocked layer and weigh a different USB controller or a separate backend.

This document distinguishes the purchased device, external research, code-read facts and all still unverified hypotheses.