# Windows 직접 BLE 입력 수신 — A2854 0xFB/0xFC

**목표:** Linux/BlueZ에서 이미 수신한 Siri Remote A2854의 HID input `0xFB`(버튼), `0xFC`(터치)를 **Windows 자체 블루투스 장치로 읽어** 콘솔에 표시한다. 마우스 입력 변환, WSL, 가상 머신, WDK, 가상 HID 드라이버 설치 없음.

## 알아야 할 Windows 제약

- 사용자 PC 실측: Windows WinRT에서 페어링/7개 서비스 발견/HID 서비스 `0x1812`까지 성공. 그 다음 HID 특성 열거가 `ACCESS_DENIED`였음.
- Windows는 OS가 소유하는 일부 BLE 서비스를 다른 앱에 공개하지 않을 수 있고, HID `0x1812`가 대표적 사례. [Chromium WinRT 구현](https://chromium.googlesource.com/chromium/src/+/HEAD/device/bluetooth/bluetooth_gatt_discoverer_winrt.cc)은 HID-over-GATT의 보호된 서비스 접근을 특별 처리함.
- Python [Bleak WinRT 소스](https://github.com/hbldh/bleak/blob/develop/bleak/backends/winrt/client.py)는 HID/본드 관리 같은 OS 소유 서비스의 ACCESS_DENIED를 발견하면 해당 서비스를 건너뛴다. 즉 같은 API에 기반한 Bleak만 사용해 이 정책을 피할 수 있다고 기대하면 안 된다.
- 따라서 **이 실험은 우회 프로그램이 아니라 가능/불가능 경계를 실제 PC에서 증명하는 도구**다. `ACCESS_DENIED`면 파싱·CCCD·입력 데이터 문제가 아니라 **그보다 앞선 HID 특성 접근 제약**이다.

## Windows 기본 PowerShell에서 실행

이 스크립트는 현재 Git 작업트리 대신 `origin/main`에서 한 파일만 TEMP로 가져오므로 사용자의 로컬 미커밋 변경을 건드리지 않는다.

~~~powershell
cd "C:\Users\무조림.000\Desktop\코딩\01_working\siri-remote-for-windows"
git fetch origin main
git show origin/main:tools/windows-probe/a2854_winrt_input.py |
    Set-Content "$env:TEMP\a2854_winrt_input.py" -Encoding UTF8
# gatt_probe.py는 이전에 이미 사용한 동일한 API 선택 도우미
git show origin/main:tools/windows-probe/gatt_probe.py |
    Set-Content "$env:TEMP\gatt_probe.py" -Encoding UTF8
git show origin/main:tools/windows-probe/adv_probe.py |
    Set-Content "$env:TEMP\adv_probe.py" -Encoding UTF8

# 설치돼 있지 않은 경우에만 (Python용 Windows BLE WinRT 바인딩)
py -3 -m pip install "bleak>=2,<4"

# 먼저 페어링된 DL1 장치 하나만 찾아서 25초 동안 입력 알림 구독 시도
py -3 "$env:TEMP\a2854_winrt_input.py" --seconds 25 --scan-before 10 --attempts 3
~~~

스크립트는 **이전에 페어링된 DL1 리모컨만** 사용한다. `GetGattServices(UNCACHED)`가 연결에 실패하면 리모컨을 한 번 눌러 깨우고 결과를 확인한다. 자동 재페어링/펌웨어 접근/드라이버 변경은 하지 않는다. 원시 Bluetooth 주소, 장치 instance path, 주변 기기 데이터를 출력하지 않는다.

성공적인 접근 순서:

1. Windows 페어링 기록에서 DL1 하나 선택 (유일하지 않으면 거부).
2. WinRT `BluetoothLEDevice.from_id_async`로 실제 연결을 시도.
3. HID `0x1812`의 GATT 특성 접근이 허용되는지 확인. `ACCESS_DENIED`면 즉시 중단.
4. `0x2A4D` 특성이 여러 개 있어도, 각자의 `0x2908` Report Reference `(ID,type)`을 읽어 **Input ID FB/FC만 식별**.
5. 해당 두 특성만 `ValueChanged` + CCCD `Notify` 등록(필수 GATT 표준 동작), 25초 동안 `raw=` 수신.
6. 종료 시 이벤트 핸들러와 CCCD 구독 해제.

예시 (실제 성공 여부는 실기기 검증 필요):

~~~text
Phase 1: uncached GATT service discovery
  Services status: SUCCESS
Phase 2: enumerate 0x2A4D HID Report characteristics
  HID access: ACCESS_GRANTED; Report characteristics=8
  HID report ref: ID=0xFB, type=1
  HID report ref: ID=0xFC, type=1
Phase 3: subscribe to verified input reports (2 targets)
  0xFB CCCD subscribe: SUCCESS
  0xFC CCCD subscribe: SUCCESS
  input 0xFB: len=2 raw=00 04
  input 0xFC: len=11 raw=...
Received: buttons(0xFB)=1, touch(0xFC)=1
~~~

만약 구독은 성공하지만 버튼·터치를 눌러도 이벤트가 없다면 **이전에 Linux에서 사용한 리모컨 입력 활성화 `0xAF`를 Report ID `0xF0`으로 한번 보내는 선택적 모드**가 있다. 읽기 전용을 벗어나 **해당 리모컨의 Feature 특성에 한 바이트를 쓰는 동작**이므로 기본값은 OFF.

~~~powershell
py -3 "$env:TEMP\a2854_winrt_input.py" --seconds 25 --enable-input
~~~

이 옵션 역시 `0xF0`이 `0x2908`에서 확인된 경우에만 사용한다. HID `ACCESS_DENIED` 상황에서는 아예 쓰기 단계에 도달하지 못한다. `0xFA` 마이크 스트림은 수집하지 않는다.

## 실패 결과 해석

| 출력 | 결론 |
| --- | --- |
| `WINDOWS_PROTECTED_HID_SERVICE` | Windows WinRT가 HID 특성에 접근 허용하지 않음. 같은 Bleak/WinRT 경로 반복 불필요 |
| `UNREACHABLE` | BLE 연결/검색 실패; HID 정책 문제인지는 아직 미확인 |
| `Report Reference ... unavailable` | 입력 특성 ID 매핑 실패; 임의 UUID로 전체 구독하지 않음 |
| `CCCD subscribe: ACCESS_DENIED` | 특성 열거는 가능했지만 알림 구독 단계에서 Windows/원격 기기가 거부 |
| `0xFB` 또는 `0xFC` 수신 | Windows 자체에서 실제 리모컨 저수준 GATT 데이터 수신 성공 |

**대안의 경계:** Windows WinRT에서 HID 서비스가 시스템 전용이라면, 일반 Python/Bleak/WinRT 사용자 프로그램만으로는 Linux의 BlueZ `--noplugin=input,hog`와 같은 임의 HID 서비스 소유권 재배분이 불가능하다. 그때는 Windows HID 디바이스 접근을 복구하는 길과, 별도 BLE central(예: ESP32나 전용 USB 동글)로 수신해 Windows에 전달하는 경로를 비교해야 한다. 이것은 Windows 자체 AX200 GATT 직접 구독과 다른 구조임을 명시한다.

## 출처

- [Microsoft WinRT GATT 클라이언트 및 notifications](https://learn.microsoft.com/ko-kr/windows/uwp/devices-sensors/gatt-client)
- [Bleak Windows WinRT HID GATT 제약](https://bleak.readthedocs.io/en/latest/_modules/bleak/backends/winrt/client.html)
- [Chromium WinRT protected HID GATT](https://chromium.googlesource.com/chromium/src/+/HEAD/device/bluetooth/bluetooth_gatt_discoverer_winrt.cc)
- [이전 원본 Report 샘플](OBSERVED_REPORT_FIXTURES.md)

## 2026-10-18 on-host first raw-input attempt

The user ran the initial direct WinRT tool with Bleak **3.0.2** and Python **3.13**; there were 2 paired BLE records, exactly 1 DL1 prefix match; `FromIdAsync` succeeded, but the **first and only uncached GetGattServices returned UNREACHABLE**. No HID characteristic discovery, notifications or Report Reference reads occurred in that attempt. The pre-existing failure of the target `mshidumdf` HID child (Code 10, `0xC00000B9`) is separate, independently observed evidence. Do not treat `UNREACHABLE` as proof the descriptor failed or WinRT denied HID service access in that particular attempt.

To avoid repeatedly running single-shot tests, `a2854_winrt_input.py` now supports a **10s advertisement prescan** (via the existing non-writing `adv_probe.scan`) and at most **three fresh WinRT device objects**, retrying **only** service discovery `UNREACHABLE`, with a 5s gap. It must **not** automatically retry `ACCESS_DENIED`. Once GATT succeeds, it continues into Report Reference and possible 0xFB/0xFC notification subscription with no test HID driver installed. It never changes Bluetooth bonding or Windows drivers.

A passive advertisement scan may wake nothing and is **not proof** of a matching advertisement identity; it merely precedes the bounded connect attempts. BLE disconnection, address rotation, system HID service ownership and device sleep remain separate hypotheses.

If it reaches `WINDOWS_PROTECTED_HID_SERVICE` again, stop attempting direct WinRT access: Windows owns/restricts HID `0x1812` via its BLE HID stack. [Microsoft GattCommunicationStatus](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattcommunicationstatus), [Bleak's 3.0.2 HID service AccessDenied behavior](https://bleak.readthedocs.io/en/latest/_modules/bleak/backends/winrt/client.html).

## 2026-10-18 owner-run native Windows experiment: OS-owned HID service confirmed

User executed `a2854_winrt_input.py --scan-before 10 --attempts 3 --seconds 25` from native PowerShell, with Windows Python 3.13, Bleak 3.0.2. No user-side driver changes or VM.

Sanitized results:

~~~text
Paired BLE devices=2, name prefix matches=1
Passive scan: 19 matching advertisements; 5 unverified identities; Apple manufacturer marker only (not A2854 identity proof)
Attempt 1/3: GATT services UNREACHABLE
Attempt 2/3: GATT services UNREACHABLE
Attempt 3/3: GATT services SUCCESS
Exposed services=7; HID 0x1812=1
HID Report 0x2A4D characteristic enumeration: ACCESS_DENIED
WINDOWS_PROTECTED_HID_SERVICE; Report characteristics=0
No Report Reference discovery; no CCCD writes; no 0xAF input enable;
no 0xFB/0xFC notifications received or even subscribed.
~~~

**Evidence interpretation:** Windows can communicate with this paired remote at the GATT service discovery level. The direct WinRT API route is **blocked at the OS-owned HID service** characteristic enumeration stage; not a timeout-only failure and not a report-decoder failure. Bleak's WinRT backend has an explicit [ACCESS_DENIED skip for system-owned HID services](https://bleak.readthedocs.io/en/latest/_modules/bleak/backends/winrt/client.html). The older independent Windows PnP finding is that the exact DL1-descendant `mshidumdf` HID child has Code 10 / `0xC00000B9`. This is NOT proof of which descriptor item caused that failure, and DOES NOT prove all possible Windows Bluetooth data-access paths are impossible.

**Decision:** stop repeating `--scan-before`/`--attempts` GATT tests and do not suggest `--enable-input`: the feature write cannot occur without GATT characteristic access. Do not try bypassing system protection through undocumented privilege or patching live drivers. If built-in AX200 native Bluetooth and no external hardware are strict requirements, pursue Windows HID initialization as a separate root-cause task, or investigate what an existing OS Bluetooth trace already recorded with realistic limitations. If an external central is allowed, an ESP32 BLE central can receive GATT reports and stream bytes over USB serial to Windows, after implementing device bonding/security and the `0xF0/0xAF` enable handshake. Neither alternative is yet verified on hardware.
