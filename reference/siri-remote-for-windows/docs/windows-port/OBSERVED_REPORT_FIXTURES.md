# 실제 A2854 HID 회귀용 샘플 (익명화)

**출처:** 2026-10-17 사용자 WSL2 Ubuntu 22.04/BlueZ 5.64 장치 실험 중 `siri-remote events` 출력. 이 표의 hex는 `events`가 보고한 `raw=` 필드이고, Bluetooth 무선 패킷 전체가 아니다. Windows 네이티브 접근 성공을 의미하지 않는다. BLE 주소/시리얼/광고 식별자는 제외.

## 0xFB 버튼 보고서

| raw (hex) | 실제 로그 해석 |
| --- | --- |
| `00 04` | Right pressed |
| `00 00` (after Right) | Right released |
| `00 08` | Down pressed |
| `01 00` | TV pressed |
| `40 00` | Back pressed |
| `00 01` | Play/Pause pressed |
| `80 00` | Mute pressed |
| `04 00` | Volume Down pressed |
| `02 00` | Volume Up pressed |

`00 00`은 이전 상태가 있어야 어떤 버튼의 release인지 알 수 있다.

## 0xFC 터치 보고서

| raw (hex) | upstream decoder가 출력한 해석 |
| --- | --- |
| `32 14 fc 01 c9 9e fb 51 44 01 66` | fingers=1, x=-311, y=-71, pressure=1, hover=true, angle=90°, major=81, minor=68 |
| `32 36 fc 00 c9 9e fb 00 00 00 67` | touch released |
| `32 92 d6 01 66 dc d5 60 1c 09 ac` | fingers=1, x=-922, y=-675, pressure=9, hover=false, angle=135°, major=96, minor=28 |
| `32 d6 d6 01 80 cc d5 93 39 0e ac` | fingers=1, x=-896, y=-676, pressure=14, hover=false, angle=135°, major=147, minor=57 |

`pressure > 0`은 실제 누른 힘의 확정 증거가 아니다. `hover=true`와 `pressure=1`이 동시에 관측되었다.

## 주의

- 이 표는 입력 예시이지 완전한 바이트별 사양서가 아니다. 실제 파서는 `src/decoder.rs`의 동작과 함께 검증할 것.
- `0xFA` 마이크는 알림 구독만 확인했으며 녹음/Opus 복호화 성공은 확인하지 않았다.
- Feature `0xF0`에 `0xAF`를 보내 HID 이벤트를 활성화했고, `0xF1`은 ATT 0x82 오류가 있었지만 이벤트 수신은 동작했다.
