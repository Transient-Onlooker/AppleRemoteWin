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

## 2026-10-21 subsequent Windows release checks and restoration evidence

New local-agent report at commit **`7c6d0cb8422468a3f6954a99612e933073f8f81b`**:

- `.NET SDK 10.0.402`, **96/96 Release tests passed** and `A2854.Probe` Release build **0 warnings / 0 errors**. These reflect the previously committed Barrot 0x21 and explicit USB targeting changes. The **new WinUSB function GUID enumeration fix** committed later still requires a fresh Windows build.
- SetupAPI confirms **Code 31 / `0xC0000001` occurred under Windows inbox `bth.inf` before `oem80.inf` was installed**. The same failure persisted with Barrot `oem80.inf`. This evidence means the `brbtusb` lower filter is **not shown to be the sole cause**. PnP event 400 = configured; event 411 = failed to start. Device setup logs do not establish the deeper reason.
- Vendor INF references `bth.inf` / `BthUsb.NT` and attaches the `brbtusb.sys` KMDF 1.15 lower filter. Package installation itself succeeded; service status STOPPED, Win32 1077. This does not establish whether USB HCI works independently of BTHUSB.
- Driver `oem80.inf` was **successfully exported and checked** locally: `Documents/AppleRemoteWin-LocalBackups/Barrot-33FA-0001-oem80-20261021-223555/` (INF, CAT, SYS). Never publish absolute account-specific paths or driver archives containing sensitive material. The exported package and remaining driver-store copy make rollback plausible, but **actual WinUSB→vendor restoration is not yet tested**.
- USBTreeView 4.7.4 captured **Full-Speed** operation under the `2109:2815` hub (the USB 2.0 descriptor version is not the operating speed). Configuration 1 exposes **interface 0 `E0/01/01`**, **interrupt IN `0x81` (16 bytes, 1 ms)**, **bulk OUT `0x02` (64 bytes)**, **bulk IN `0x82` (64 bytes)**. Interface 1 provides isochronous endpoints. These match `UsbHci.FindPipes` selection.
- Exactly **one** `33FA:0001` dongle, built-in Intel `8087:0029` remains healthy. No USB driver/registry/WinUSB/paired-device changes were performed.

### Next safe decision: direct port, then Microsoft-signed inbox WinUSB selection if offered

1. **Move only the Barrot dongle** from the `2109:2815` hub to a direct laptop USB-A port and re-read Code 31, PnP driver, USB speed and endpoints. Do not disturb the Intel Bluetooth driver. USB direct attach is a cheap falsification test; Code 31 persistence does not imply hardware failure.
2. Before custom-driver installation, check for a Windows **Microsoft-signed built-in “WinUsb Device”** option in Device Manager → Update driver → Browse my computer → Let me pick → **Universal Serial Bus devices** (class availability may vary). [Microsoft documents this as an official manual installation path](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/winusb-installation). If absent, **stop** rather than using an unsigned INF or an arbitrary download.
3. If the Microsoft-signed built-in option genuinely exists, requires no new root certificate and can be assigned **only to the identified `33FA:0001` devnode**, it may be used after verifying the backup and a rollback route. Microsoft notes that this **does not automatically provide a user-mode device interface GUID**. The WinUSB device's **Device Parameters** must expose `DeviceInterfaceGUIDs` as REG_MULTI_SZ containing **`{6B3E8C52-9D41-4F6A-8A1E-5C0D7B2F4E19}`** (the GUID our `WinUsbDriver.BuildInf` already uses), then reenumerate the **external adapter only**. Do not reset ACLs/take ownership to force registry writes; report denial.
4. The experimental C# `UsbHci` was fixed to enumerate its **registered WinUSB function-interface GUID** before the earlier generic USB-device GUID. This was important: matching USB class descriptors alone would not let `CreateFile` find the WinUSB function device. [Microsoft's WinUSB docs explain interface GUID enumeration](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/winusb-installation).
5. **Do not invoke `WinUsbDriver.Install()` or Zadig as an automatic fallback in this round**: the current application installer creates a self-signed code-signing certificate, imports it to machine-wide `Root` and `TrustedPublisher`, and force-binds by hardware ID. Even with one observed device that is a different risk profile. The original `Restore()` is likewise **not yet proven** safe for every driver-store state. Obtain fresh explicit authorization before the system-wide certificate method.
6. If a scoped inbox binding succeeds, verify Windows shows `WinUSB` on **33FA:0001** while Intel remains BTHUSB/OK. First run a **transport-only** probe (no BLE scan/pairing):

   ```powershell
   dotnet run -c Release --project tools/A2854.Probe/A2854.Probe.csproj -- --usb 33FA:0001 --hci-only
   ```

   This opens only the matched dongle, performs one HCI Reset and a Read Local Version query, and reports public HCI version/manufacturer fields. It never writes new bond keys. `--hci-only` is a new **source feature not yet Windows-build verified**. If successful, a separate full 30-second BLE run can be considered. If HCI reads fail, capture only error/status and restore the original binding from the device's saved driver list/backup.

Key official caveat: automatic inbox `winusb.inf` matching without manual selection only occurs if firmware advertises `USB\\MS_COMP_WINUSB` via Microsoft OS descriptors. The observed `E0/01/01` compatible ID **does not** establish this. [Microsoft explanation](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/automatic-installation-of-winusb).

## PowerShell-only driver binding (2026-10-21)

The fork now includes [`tools/A2854.Probe/InstallBarrotWinUsb.ps1`](../../tools/A2854.Probe/InstallBarrotWinUsb.ps1). It runs **the existing** `WinUsbDriver.Install()` from a temporary .NET 10 helper; it does not create or replace Microsoft's `winusb.sys`.

To preflight without changing anything (ordinary PowerShell, repo root):

```powershell
git pull --ff-only
& .\tools\A2854.Probe\InstallBarrotWinUsb.ps1
```

To **explicitly install**, open an **administrator PowerShell** at the repo root and run:

```powershell
& .\tools\A2854.Probe\InstallBarrotWinUsb.ps1 -Install
```

The script fails closed unless exactly one present `USB\\VID_33FA&PID_0001` exposes HCI class E0/01/01 and an existing INF/CAT/SYS export backup has valid Microsoft WHCP signatures and an INF matching the exact VID/PID. It requires typing the literal `INSTALL 33FA:0001` after warning about trust changes. Only that VID/PID is passed to the installer's forced device driver update; the script requires a single healthy internal `8087:0029` before installation and checks it again afterward.

**Tradeoff / explicit authorization:** `WinUsbDriver.Install` generates a self-signed code-signing certificate valid for **30 days** and imports it to machine-wide `LocalMachine\\My`, `Root`, and `TrustedPublisher`. It records the exact certificate thumbprint at `HKLM\\SOFTWARE\\AppleRemoteWin\\WinUsbDriver\\VID_33FA_PID_0001`. Restore removes only that thumbprint from the three stores; it never deletes every certificate with the same subject. This carries a broader trust-surface impact than an inbox-only manual binding; there is **no claim** of certificate-free installation. The installed package's actual behavior on the owner's Code 31 unit is **not validated** by documentation or unit tests. A self-signed catalog does not guarantee modern Windows code-integrity acceptance.

Microsoft sources: [WinUSB INF-based installation](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/winusb-installation), [PnPUtil driver ranking / force limitations](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-command-syntax), [Windows driver signing](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/windows-driver-signing-tutorial).

## 2026-10-21 script review and local validation

At `36ab933`, the local Windows agent found that the preflight's `$matches` variable collided case-insensitively with PowerShell's automatic `$Matches` regex-capture variable when validating the INF. It is now `$barrotDevices`. Backup validation now checks the exact hardware ID in the INF and valid Microsoft WHCP Authenticode signatures for INF/CAT/SYS; the install path also fails closed unless the built-in Intel adapter is uniquely present and healthy and the external node reaches WinUSB/PnP OK before HCI testing. The Probe identifies failure as USB WinUSB interface discovery/open, before any HCI command.

Local Release verification: **96/96 tests passed**, Probe build **0 warnings / 0 errors**, PowerShell parser and read-only script preflight passed. The preflight found exactly one external `33FA:0001` (still BTHUSB / Code 31) and one healthy Intel AX200. The saved Barrot INF/CAT/SYS signatures all validate. No driver binding or certificate-store changes have been performed. HCI-only currently stops at WinUSB interface discovery/open; HCI Reset and Read Local Version have not yet been sent.

## 2026-10-21 approved WinUSB experiment hardening

Before installation, the signing certificate lifetime was reduced to **30 days**. Its thumbprint is recorded in a VID/PID-specific HKLM state key before trust imports; rollback consults that value and removes only the matching thumbprint. A subject collision now fails closed rather than reusing an untracked certificate. Catalog signature status is checked, and failures report the current phase; if driver binding may have succeeded, the certificate is retained for safe rollback. The exact single VID/PID target is checked again by `WinUsbDriver.Install`. The install script revalidates the Barrot WHCP backup and healthy AX200 immediately before the change.

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

## 2026-10-22 milestone: WinUSB HCI on purchased 33FA:0001 — **real hardware SUCCESS**

The local Windows agent installed the fork's **WinUSB OEM package `oem86.inf`** on the one external Barrot USB dongle. The PnP device now presents as **`USB2.0-BT` / Started**, with the original Intel `8087:0029` still **Started**. This is the **first physically proven native Windows HCI-over-USB communication** for the project, rather than simply a driver-enumeration success.

The agent executed a previously built Release diagnostic executable with `--no-build --usb 33FA:0001 --hci-only` (the latest source did **not** build successfully during this run) and observed:

```text
USB controller: USB2.0-BT (33FA:0001); HCI EP0 request=0x21.
HCI Reset OK
Read Local Version OK:
  HCI=0x09, revision=0x0202
  LMP=0x09, manufacturer=0x08E7, subversion=0x0002
HCI-only complete: no BLE scan or pairing attempted.
```

**Interpretation:**
- USB WinUSB discovery/open, interface 0 endpoints, HCI EP0 class/interface request **0x21**, interrupt-event receive, `Reset` and `Read Local Version` have now been demonstrated on the **physical device**.
- Bluetooth SIG [Assigned Numbers](https://www.bluetooth.com/wp-content/uploads/Files/Specification/HTML/Assigned_Numbers/out/en/index-en.html) maps HCI/LMP version `0x09` to **Bluetooth 5.0**, and manufacturer `0x08E7` to **Barrot Technology Co.,Ltd.**. This is a measured **reported controller stack version**, differing from the dongle's retail 5.3 label; do not infer chipset identity or BLE radio capabilities beyond measured HCI values.
- **LE feature bit support, BLE scan/advertising, device connection, pairing, GATT, unlock and 0xFB/0xFC input are still untested.** HCI-only success cannot be described as Windows Siri Remote success.
- The agent confirms the **experimental signing certificate** was installed in LocalMachine `My`, `Root` and `TrustedPublisher`, with matching recorded thumbprint. Certificate expiry: **2026-11-21**. Limit experiments to the intended adapter. The restore path has been implemented and documented but **has not been exercised on this physical dongle**.
- **Build/test regression still unresolved:** this run's plain `dotnet run -c Release` build failed; `dotnet test -c Release` did not return and was stopped. The earlier **96/96** pass is historical and cannot be reported as a new pass at HEAD `8256a5aaa8c12b63cb697df66edad7491a6dd4e4`. The report does not include full compiler/restore logs, so there is **no grounded root-cause determination yet**. Request exact first build failure plus test hang stage before attempting speculative fixes. Do **not** overwrite the known-good binaries when evaluating source changes.
- Local Git at the end of that experiment: branch `feat/a2854-support`, HEAD `8256a5aaa8c12b63cb697df66edad7491a6dd4e4`, clean. The new `--scan-only` code was subsequently added by ChatGPT and remains **unbuilt/untested** until the next local test.

**Next order of operations:** diagnose Release build and `dotnet test` hang → produce a new clean build → run the privacy-safe **`--scan-only`** probe for 30 seconds against `--usb 33FA:0001` → only after confirmed LE scanning consider A2854 connection/SMP/GATT. `--scan-only` does not connect, pair, touch `a2854-probe-bonds.json`, or log BLE addresses/serials. If no advertising packets appear, report HCI LE command successes/failures and controller features, and inspect radio/pairing mode before changing any BLE algorithm.

## 2026-10-22 milestone: native Windows BLE advertisements received (no connection)

The owner/local Windows agent confirmed the **previously committed `--scan-only` diagnostic built successfully in Release**, with zero warnings/errors. The former `MSB3491 Access denied` originated from sandboxed writes under `obj`, **not** a C# compiler or NuGet source failure; an executable/writeable build environment resolved it. After clearing stale MSBuild build servers and running tests in a single node with a 60-second hang monitor, **96/96 tests passed** at HEAD `4820fc17942b2d53dee168917db52a2c7659e383`. Previous Release probe binaries were backed up locally.

The physical **WinUSB Barrot `33FA:0001`** completed LE initialization, reported `Extended Advertising=True`, and **received actual BLE advertisements**. A first scan found 444 events (11 Apple company-ID packets, 0 HID `0x1812`); the second 30-second scan, performed with appropriate remote wake/pairing-advertisement timing, returned:

```text
packets=770
apple_ad_packets=178
hid1812_ad_packets=155
both_in_one_packet=155
```

This is **155 advertising packets containing Apple manufacturer company ID `0x004C` AND 16-bit HID service `0x1812` in the SAME packet**. That matches the *broad* A2854 pairing-advertisement behavior observed on Linux, and is far more discriminating than Apple-only packets. **It is not proof of a specific A2854 identity**, because an Apple HID-capable device can advertise the same fields. There has been **no LE connection, SMP exchange, GATT discovery, pairing, bond write, 0xFB button reception or 0xFC touch reception on Windows**.

### Additional privacy-safe identification (implemented after this measurement; hardware NOT retested)

The project already holds **two different** manufacturer header variants for the physical model (do not include any following identity-address bytes in public logs):

- Rust upstream README/reference fingerprint: Apple `0x004C`, manufacturer bytes start with `07 0D 02 15 03 02`. A suffix may vary between devices.
- Actual old HCI trace reused by `FakeRemote.PairingAdvertisement` in `SiriRemoteBleTests.cs`: Apple `0x004C`, manufacturer bytes start with `8A 07 6D 02 02`. Do **not** reject this proven variant by requiring the README version.
- Historical Windows paired device name appeared as **`DL1…`**; `Siri Remote` is another possible name hint, but **BLE advertisements can omit their names**, and `DL1…` is not a cryptographic identity.

`src/AppleRemote.Core/A2854Advertisement.cs` now provides a conservative **`StrongCandidate` = Apple company 0x004C + HID 0x1812 + either documented manufacturer-prefix variant within one advertisement packet**. `tools/A2854.Probe --scan-only` reports only aggregate counters `strong_candidates`, `readme_prefix`, `captured_prefix`, and `name_hints`, with no BLE address, identity bytes, full advertising data, or per-device stable ID. New deterministic tests cover the captured variant, reference variant, iPhone-only advertisements, Apple+HID false positives, and a name-only hint. **These new changes have not been compiled or physically tested yet**; the already confirmed 96/96 pass is at the *previous* HEAD. An absent known prefix does not prove the target is absent (additional vendor variants or separated adv/scan-response events may exist).

**Immediate next test:** `dotnet test tests/AppleRemote.Tests -c Release` and `dotnet build tools/A2854.Probe/A2854.Probe.csproj -c Release`, then `dotnet run -c Release --project tools/A2854.Probe/A2854.Probe.csproj -- --usb 33FA:0001 --scan-only --seconds 30`. Start scan **before** waking/advertising the actual A2854, to reduce the chance of missing its limited-duration advertising mode. Hold off on LE connection and SMP pairing until the user explicitly authorizes the next stage. Keep the existing WinUSB driver/certificate installation and Intel controller untouched.

