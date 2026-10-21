# A2854 Linux 최소 BLE 수신 실험 — VM/WDK/마우스 구현 없음

## 이번 단계의 성공 조건

**Linux/BlueZ에서 실제 물리 리모컨의 버튼 입력(Report ID 0xFB) 또는 터치 입력(0xFC)이 관측된다.**

연결 전 HCI/ATT만 보여도 1단계 성공이며, `ServicesResolved=False`일 때 HID 보고서를 아직 못 받는 것은 별개의 연결/서비스 발견 문제이다. 두 단계는 분리하여 기록한다.

실기기에서 이미 검증된 과거 경로: `usbipd` → WSL2 Ubuntu 22.04 → BlueZ `--noplugin=input,hog` → Rust `siri-remote events` → 0xFB/0xFC. 이 문서는 새로운 마우스/가상 드라이버를 생성하거나 Python HID 파서를 Windows에 설치하지 않는다.

## 1. Windows 관리자 PowerShell — AX200을 WSL로 임시 연결

**주의:** 같은 Intel AX200을 Windows와 WSL에서 동시에 사용할 수 없다. 연결 중 Windows Bluetooth 장치가 동작하지 않는다. 현재 사용 중인 Bluetooth 키보드/마우스가 있다면 유선 입력 장치를 준비한다.

~~~powershell
usbipd list
# 아래 1-3은 과거 사용자 PC에서의 번호다. 출력에서
# Intel Wireless Bluetooth / 8087:0029 장치와 현재 BUSID가 일치할 때만 실행.
usbipd bind --force --busid 1-3
usbipd attach --wsl --busid 1-3
usbipd list
wsl -d Ubuntu-22.04
~~~

`usbipd bind`는 관리자 권한이 필요하며 공유 설정을 유지한다. `usbipd attach`는 장치를 WSL에 넘기고 Windows에서 잠시 사용할 수 없게 한다. 설치판 usbipd 명령이 다른 경우 `usbipd --help` 확인.

## 2. WSL Ubuntu — 상태 확인 (기본적으로 읽기 전용)

~~~bash
lsusb -d 8087:0029
bluetoothctl list
sudo systemctl is-active bluetooth
systemctl show bluetooth -p ExecStart
command -v btmon
command -v timeout
~~~

기대: Intel USB ID가 보이고 `bluetoothctl list`에 컨트롤러가 하나 있다. `systemctl show ... ExecStart`에 `--noplugin=input,hog`가 적용돼 있으면 **기존에 검증했던 BlueZ GATT 직접 접근 구성**이다. 이 옵션이 없더라도 먼저 관측 결과를 남긴 뒤 수정 여부를 판단한다. 이 단계에서 `bluetoothctl remove`, `pair`, `trust` 또는 BlueZ 설정 파일 교체를 하지 않는다.

## 3. HCI/ATT 원시 수신 관측 — WSL 터미널 두 개

WSL 터미널 A:

~~~bash
sudo btmon -i hci0
~~~

이것은 **수신 관측 전용**이며 연결이나 HID 알림을 스스로 활성화하지는 않는다. 모니터에는 주변 기기의 식별주소와 패킷 내용이 나타날 수 있으므로 로그 전체를 공유하지 않는다.

WSL 터미널 B (이전에 빌드해 둔 Rust 바이너리가 있다면 해당 저장소 폴더에서):

~~~bash
ls -l ./target/release/siri-remote
timeout 90s ./target/release/siri-remote events --scan-seconds 5
~~~

물리 Siri Remote의 **Right / Back / Volume Up / 터치 패드**를 각각 눌렀다 놓아본다. `timeout`은 무한 재연결을 방지한다. 바이너리가 없으면 여기서 중지하고, 과거 WSL 오디오 헤더 호환성 빌드 이슈를 확인한다. **Windows에서 만든 `target`/DLL을 Linux에서 실행하는 것이 아님**에 주의한다.

`events`는 과거와 같은 BlueZ D-Bus GATT 코드로, HID Report UUID 0x2A4D 중 Report Reference 0x2908의 Input ID들을 열거하고 `StartNotify`를 호출한다. 알림 활성화를 위해 Feature Report 0xF0 쪽으로 `0xAF` 쓰기를 시도할 수 있으므로 `btmon`과 달리 순수 read-only 작업이 아니다. 단, 자동 리모컨 제거/펌웨어 플래시/Windows 마우스 입력은 하지 않는다.

**확인하고 싶은 출력:**

~~~text
input report_id=0xFB raw=00 04    # Right 눌림의 과거 샘플
input report_id=0xFB raw=00 00    # 버튼 해제
input report_id=0xFC raw=...      # 터치/호버 보고서
~~~

숫자는 예전 기록에서 발췌한 예시이며 새 테스트에서 반드시 똑같이 나타나는 것은 아니다. `0xFC`는 패킷 유효성·길이까지 확인하되 압력값을 물리 힘이나 절대 높이로 해석하지 않는다.

BlueZ `ServicesResolved=False`가 지속되거나 `input-hog profile accept failed`, `ATT 0x82`, `AuthenticationFailed` 등이 나온다면 리모컨/HID 디스크립터 문제라고 바로 판단하지 않는다. 먼저 A의 HCI 로그에서 `LE Enhanced Connection Complete`, ATT 서비스 검색, 암호화/인증 또는 종료 이벤트 중 무엇이 발생했는지 확인한다. Windows에서 최근 재페어링한 이력 때문에 기존 Linux bond가 유효하지 않을 가능성도 있으나, 증거 없이 페어링을 초기화하지 않는다.

## 4. 실험 종료 후 Windows Bluetooth 복원

Windows 관리자 PowerShell에서, **실제 AX200의 BUSID를 다시 확인**하고:

~~~powershell
usbipd list
usbipd detach --busid 1-3
usbipd unbind --busid 1-3
usbipd list
~~~

컨트롤러가 Windows로 돌아왔는지 Bluetooth 설정에서 확인한다. 과거 BUSID가 아닌 경우 일치하는 값으로 대체한다.

## 결과 공유 형식 (개인 식별정보 제외)

- Windows usbipd 장치 상태: `Shared`, `Attached`, `Not shared` 가운데 어느 상태인가.
- WSL: `lsusb`에서 8087:0029가 보이는가; BlueZ Controller가 보이는가; 서비스가 active인가.
- 기존 binary 존재 여부 / `events` 연결·특성 열거 여부.
- HID Input ID 0xFB, 0xFC의 메시지 건수와 버튼/터치 동작 대응.
- 실패 시: BlueZ에서 `ServicesResolved`가 `True` 또는 `False`였는지, `btmon`에 **LE 연결 완료 / ATT 검색 / 암호화 실패 / 연결 종료** 중 어느 단계가 나타났는지.

**개인정보:** `btmon` 전체 로그, MAC 주소, 실기기 시리얼, 주변 기기 BLE 광고, 음성 오디오 Report 0xFA, Rust 실행 로그의 전체 `selected_address`를 공개하지 않는다. 통제된 0xFB/0xFC raw payload만 필요 시 선별 공유한다.

참고: [BlueZ btmon 매뉴얼](https://github.com/bluez/bluez/wiki/btmon), [usbipd-win WSL 지원](https://github.com/dorssel/usbipd-win/wiki/WSL-support), [기존 관측값](OBSERVED_REPORT_FIXTURES.md), [BlueZ 재현 메모](WSL_COMPATIBILITY_NOTES.md).
