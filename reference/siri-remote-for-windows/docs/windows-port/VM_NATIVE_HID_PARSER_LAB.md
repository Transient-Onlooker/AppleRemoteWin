# A2854: Windows HID 파서 격리 검증 실험

## 원칙

이 테스트는 Siri Remote 또는 Windows Bluetooth 드라이버를 고치는 작업이 아니라, **Windows HID 파서가 네 가지 Report Descriptor를 어떻게 처리하는지** 분리해서 측정하는 과정이다. 실험용 드라이버는 **실제 노트북이 아닌 격리된 Windows 11 VM** 안에만 설치한다.

실제 A2854에서 얻은 38바이트 원본의 SHA-256: `6f9795ae87c7bcfcba0d50c38cbde6abf5163f78e1299cc23dc110795bc57f49`.

### 4개 대조군

| 대조군 | 내용 | Feature payload bits | Input bits |
|---|---|---:|---:|
| A_original | 원본 | 300 | 136 |
| B_usage_range | Variable Input에 Usage Minimum 1~Maximum 17 추가 | 300 | 136 |
| C_padding | Feature에 Constant 4비트 패딩 추가 | 304 | 136 |
| D_usage_padding | Usage 및 4비트 패딩 모두 | 304 | 136 |

**핵심:** B/C/D는 기존 실제 리모컨 펌웨어를 수정하거나 BLE 페어링을 건드리지 않는다. VM 안에서 빌드할 **소스 파일만** 변경한다.

## 1단계: 실제 Windows 호스트에서 할 일 (설치/설정 변경 없음)

사용자가 원래 프로젝트에서 아래 명령어를 관리자 권한이 아닌 일반 PowerShell로 실행해도 된다.

~~~powershell
cd "C:\Users\무조림.000\Desktop\코딩\01_working\siri-remote-for-windows"
git fetch origin main

# PC에 Windows 테스트 VM을 실행할 수 있는 환경이 있는지 읽기 전용 점검
git show origin/main:tools/windows-probe/a2854_vm_host_check.ps1 |
    Set-Content "$env:TEMP\a2854_vm_host_check.ps1" -Encoding UTF8
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\a2854_vm_host_check.ps1"

# 개인 38바이트 원본을 대조하고, Microsoft 공식 UMDF2 샘플 C 소스의
# 네 가지 수정본만 로컬 TEMP 폴더에 생성
git show origin/main:tools/windows-probe/a2854_vm_prepare.ps1 |
    Set-Content "$env:TEMP\a2854_vm_prepare.ps1" -Encoding UTF8
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\a2854_vm_prepare.ps1"
~~~

준비 스크립트는 Microsoft 공식 `Windows-driver-samples` 커밋 `2dc3fd3a0cc84a2933f2194e7ec0871584979071`의 `hid/vhidmini2/driver/vhidmini.c` **소스 파일 하나만** 내려받는다. 실험 생성기는 해당 샘플의 report descriptor 배열을 교체하고, 레지스트리 디스크립터를 읽다가 실패 시 기본 배열로 돌아가는 코드를 **테스트용 복사본에서만 비활성화**한다. 따라서 소스에 적힌 디스크립터와 런타임이 사용하는 디스크립터가 어긋날 가능성을 줄인다.

출력 폴더는 TEMP 아래 `a2854_vm_source_날짜_프로세스ID`. 네 폴더에 각각 `vhidmini.c`가 생성되고 `manifest.json`에 SHA-256과 구성/크기가 정리된다. 이 폴더에는 A2854 디스크립터 원본 일부가 포함되므로 **공개 저장소에 커밋하거나 업로드하지 않는다**.

## 2단계: VM을 먼저 준비한 후 수행

1. Windows 11 **64비트 VM**을 생성한다. Hyper-V를 사용할 수 있으면 Hyper-V, 그렇지 않으면 별도 VM 제품을 선택한다. 물리 Bluetooth 패스스루는 **필요 없다**. Windows 11 게스트 요구 사항(TPM 등)은 사용하는 가상화 도구의 공식 절차대로 구성한다. 메인 PC에서는 실험용 드라이버 설치, 커널 테스트 모드 설정, Secure Boot 변경을 하지 않는다.
2. **VM 체크포인트/스냅샷을 생성**하고, 게스트 안에 Visual Studio C++ 빌드 환경 및 현재 호환되는 WDK를 설치한다. 코드 서명·테스트 모드 작업이 필요하다면 **VM 안에서만** 수행한다.
3. [Microsoft 공식 vhidmini2 UMDF 2 샘플](https://github.com/microsoft/Windows-driver-samples/tree/main/hid/vhidmini2)을 먼저 **수정하지 않고** x64로 빌드하여 VM에서 정상 시작하는지 검증한다. VM에서 기본 샘플이 시작하지 못한다면 이 단계에서 중단한다. 기본 샘플이 안 될 때 A2854 디스크립터 탓을 하지 않는다.
4. 수정한 C 소스 중 A 하나만 샘플 `hid/vhidmini2/driver/vhidmini.c`의 **VM 내 사본**에 교체하고 UMDF2 드라이버를 다시 빌드한다. INF에서 지정하는 별도 루트 디바이스 `root\VhidminiUm`만 사용한다. 예컨대 WDK의 DevGen으로 루트 테스트 디바이스를 만든 뒤 **VM 안에서만** PnPUtil로 테스트 드라이버를 설치한다.
5. 설치된 실제 테스트 디바이스에서 `ProblemCode` 및 `ProblemStatus`를 확인한다. 성공 시 실제 적용된 Report Descriptor가 A의 SHA-256/길이와 일치하는지 HIDCLASS 추적 또는 빌드 증거로 확인한 뒤에만 'Windows 파서에서 A 성공'이라고 판단한다.
6. 스냅샷을 되돌린 다음 B, C, D 각각 반복한다. 빌드 산출물/디바이스 상태를 매 케이스 구분한다. **원본 샘플이 자동으로 선택됐거나 descriptor 확인이 불가능한 시험은 '미확정'으로 처리한다.**

공식 샘플 INF는 Windows build **22000 이상**의 `MsHidUmdf.inf`를 사용하므로 최소 Windows 11 게스트를 권장한다. [관련 INF](https://github.com/microsoft/Windows-driver-samples/blob/main/hid/vhidmini2/driver/umdf2/VhidminiUm.inx) 및 [공식 HID 미니드라이버 가이드](https://learn.microsoft.com/en-us/windows-hardware/drivers/wdf/creating-umdf-hid-minidrivers) 참고.

### VM 결과 기록표

| Variant | build | Installed exact root HID test driver | Code 10 | NTSTATUS | Active descriptor verified | Verdict |
|---|---|---|---|---|---|---|
| Official unmodified baseline | ? | ? | ? | ? | ? | ? |
| A_original | ? | ? | ? | ? | ? | ? |
| B_usage_range | ? | ? | ? | ? | ? | ? |
| C_padding | ? | ? | ? | ? | ? | ? |
| D_usage_padding | ? | ? | ? | ? | ? | ? |

**판정 기준**: 기본 샘플이 정상 시작하고 디스크립터 실제 적용이 입증된 경우에만 대조군 결과를 해석한다. A 실패/B 성공이면 Usage 가설, A 실패/C 성공이면 정렬 가설, D만 성공하면 두 조건의 조합 가능성이 높다. 네 변형이 전부 실패하면 샘플 실행·드라이버 설치·그 외 HID 의미 규칙도 검토한다. **VM에서 성공해도 Siri Remote의 BLE GATT Report Map 및 Report Reference 일치 여부는 별도로 확인해야 한다.**

### 현재 상태와 안전 제한

- 사전 가설: 실제 A2854는 Windows에서 `mshidumdf` Code 10 / `0xC00000B9`.
- 기존 WPR 10.38MiB ETL은 93,076개 이벤트를 포함하지만 HIDCLASS WPP/ETW GUID가 provider 요약에 보이지 않음. **같은 WPR 방식 반복 금지.**
- **이 문서와 생성기 자체로 Windows HIDCLASS 파서의 합격/불합격을 자동 판정하지 않는다.** 실제 Windows 게스트에서 UMDF2 빌드와 설치가 필요하다.
- 생성된 C 소스는 실험용이며 실제 Siri Remote/실사용 드라이버로 배포하거나 설치하지 않는다.

## 공식 자료

- [Microsoft vhidmini2 UMDF2](https://github.com/microsoft/Windows-driver-samples/tree/main/hid/vhidmini2)
- [Microsoft sample driver source](https://github.com/microsoft/Windows-driver-samples/blob/main/hid/vhidmini2/driver/vhidmini.c)
- [WdfDeviceOpenRegistryKey and hardware key](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/wdfdevice/nf-wdfdevice-wdfdeviceopenregistrykey)
- [Microsoft driver testing and WDK](https://learn.microsoft.com/en-us/windows-hardware/drivers/develop/testing-a-driver)
- [Microsoft PnPUtil](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-command-syntax)

## 2026-10-18 실제 호스트 사전검사 결과 및 VM 실행 결정

실제 Windows 11 사용자 PC가 수정된 read-only host checker를 실행했다. 결과:

- Windows 11 Home, `EditionID=Core`, build `26200`.
- AMD Ryzen 7 5800H, virtualization firmware enabled = `True`.
- HypervisorPresent = `True`; `VirtualMachinePlatform=Enabled`; `HypervisorPlatform=Disabled`.
- `SecondLevelAddressTranslationExtensions=False`라고 보고하지만 이 CPU의 하드웨어 SLAT 미지원 증거는 아님; WSL2/활성 하이퍼바이저가 함께 관측됨.
- Client Hyper-V PowerShell cmdlets absent; `Microsoft-Hyper-V-All` state unavailable on Home.
- RAM 15.4GiB physical. Only fixed drive C:, free **29.6GiB** of 455.8GiB.
- Private A2854 38-byte descriptor SHA-256 confirmed and all four UMDF2 C source variants generated successfully at a user-private TEMP folder. **Do not regenerate unless damaged.**

**Gate decision: DO NOT create/install Windows 11 driver-test VM on C: right now.** Official Windows 11 VM minimum is **64GB provisioned virtual storage**, RAM >=4GB, >=2vCPU, vTPM and Secure Boot. Even if thin/dynamic disks initially consume less than 64GB, a Windows 11 installation, Visual Studio/WDK, and four build outputs/snapshots on a 29.6GiB-free drive are not safe planning assumptions. The 15.4GiB physical RAM can support a minimal VM, but memory pressure must be considered.

### Next available execution paths

1. **Preferred local:** move VM disk and development artifacts to an external SSD or another internal drive with **at least 100–150GB free** (planning headroom, NOT Microsoft's minimum), use a supported Windows Home-compatible virtualization app (such as VMware Workstation Pro or Oracle VirtualBox), Windows 11 guest x64 with UEFI/Secure Boot/vTPM. If WSL2 hypervisor causes conflicts, choose hypervisor-compatibility mode supported by the chosen product rather than globally turning off WSL2. Verify VM can boot and snapshot FIRST. Current free personal/education/commercial VMware Workstation Pro status: https://www.vmware.com/docs/desktop-hypervisor-faqs . VirtualBox official Windows 11 guest support: https://docs.oracle.com/en/virtualization/virtualbox/7.2/user/Introduction.html
2. **Cloud Windows VM:** disposable remote Windows test host with 2+ vCPU, 8GiB RAM, ~100GB disk, permitted to enable test-signing/WDK driver installation. Check licensing/billing and provider driver-signing support. Build the driver source and keep original descriptor and WPP files private. This route avoids consuming the laptop's SSD.
3. **Pause native VM experiment:** original A2854 is unaffected, prior proof is preserved. Work independently on separate BLE-central + Windows user-mode app input transport for actual functionality. Do not assert the 38-byte descriptor's cause is proven until native HID parser oracle runs.

Official VM minimum: https://learn.microsoft.com/en-us/windows/whats-new/windows-11-requirements . If a Windows Enterprise evaluation image is used, consult eligibility/use rules; the official 90-day evaluation is intended for IT professionals and may require sign-in/registration: https://www.microsoft.com/ko-kr/evalcenter/evaluate-windows-11-enterprise . No ISO, Windows features, test certificates, or driver packages have yet been installed by these scripts.

**Next decision needs a location to store/run the Windows guest**, not another Bluetooth/ETW capture or another Python parser experiment.
