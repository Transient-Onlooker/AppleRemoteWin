# Siri Remote A2854 — Windows HID 집에서 실행할 실험 (2026-10-17)

**실험 단계 분리**: 안전한 정적 검증 → Microsoft WPP 추적 프로필 준비 → **명시적 선택 시에만** 실패한 Siri Remote HID 노드 재시작과 WPP 수집.

이번 수집은 Bluetooth 페어링을 초기화하거나 Windows 드라이버/레지스트리를 교체하지 않는다. 기존 Windows에서 확보한 38바이트 Report Descriptor를 그대로 사용한다.

## 확인된 사실 및 새 실험의 목적

- Windows 장치의 mshidumdf Code 10 / 0xC00000B9는 **HIDCLASS Event 3 v2의 정확한 장치 경로와 상호 일치**한다.
- 같은 이벤트의 38바이트 바이너리는 Feature ID 0xB8 (50 bits × 6 = **300 bits**) 및 Input ID 0x04 (17 × 8 = 136 bits)를 기술한다.
- Feature는 **4비트가 바이트 정렬되지 않고**, Input Data/Variable에 **직접 대응되는 Local Usage가 없다**.
- Microsoft는 HIDCLASS의 Code 10 디스크립터 오류 유형으로 'Report wasn't byte aligned' 및 'A nonconstant main item was declared without a corresponding usage'를 기술한다. 이 문서는 HID-over-I2C 사례이므로 BLE 장치의 실제 오류 위치를 아직 확정하지는 않는다.
- 이전 단순 Python 파서들은 두 결함을 가진 합성 디스크립터를 **모두 받아들였기 때문에**, 오프라인 테스트만으로는 Windows native parser의 실패 위치가 특정되지 않는다.

**실험 목표**: Microsoft의 HIDCLASS WPP 오류 메시지에 특정 보고서 디스크립터 offset이 나타나는지 확인. 못 얻으면 WDK VM에서 실제 Windows HID parser를 검증하는 단계로 변경한다.

## 복사하여 실행 — 관리자 PowerShell

아래 명령을 순서대로 실행한다. **3단계는 실제로 HID 장치 하나를 재시작하므로 명시적으로 선택한 경우에만 실행.**

~~~powershell
# 0. 저장소 경로. 미커밋 파일을 변경하지 않는다.
cd "C:\Users\무조림.000\Desktop\코딩\01_working\siri-remote-for-windows"
git fetch origin main

# 실행기 하나만 TEMP에 가져온다.
git show origin/main:tools/windows-probe/a2854_home_lab.ps1 |
    Set-Content "$env:TEMP\a2854_home_lab.ps1" -Encoding UTF8

# 1단계: read-only. 보고서 SHA-256, 정렬·Usage 변형, PnP/WPR 사전검사.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\a2854_home_lab.ps1" -Stage Safe

# 2단계: 공식 Microsoft 버전 고정 .wprp XML을 TEMP로 내려받는다.
# 시스템 드라이버, 레지스트리, 페어링 변경 없음.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\a2854_home_lab.ps1" -Stage PrepareWpp

# 3단계: 동의할 때만! 정확히 하나의 기존 Code-10 HID 자식을 재시작하고
# Microsoft HIDCLASS WPP를 12초 동안 수집한다.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\a2854_home_lab.ps1" -Stage CaptureWpp
~~~

오프라인 변형 바이너리 8개까지 로컬 TEMP 아래 새 폴더에 생성하고 싶다면:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\a2854_home_lab.ps1" -Stage Safe -WriteVariants
~~~

별도 Python 파서 추가 검사를 켜려면 다음을 한 번 설치하면 된다. 없어도 1단계 자체 검사 가능:

~~~powershell
py -3 -m pip install "hid-parser==0.1.0"
~~~

실행기는 origin/main의 필요한 파일만 TEMP로 가져오며 **git pull/reset, 다른 소스코드 덮어쓰기, 페어링 제거를 하지 않는다**. 확인된 원본 38바이트 SHA-256은 '6f9795ae87c7bcfcba0d50c38cbde6abf5163f78e1299cc23dc110795bc57f49'로 검증하며 해시가 다르면 중단한다.


## Actual A2854 host experiment — confirmed 2026-10-17

The owner ran all three phases in administrator Windows PowerShell on Windows build 26200. This is actual hardware evidence, **not** a CI simulation.

- **Safe**: one Siri Remote-specific HID child still had \`mshidumdf\` Code 10 / \`0xC00000B9\`; the previously extracted 38-byte local descriptor matched its exact expected SHA-256.
- Original static experiment: Feature report **300 bits**, Input **136 bits**, \`unaligned=1\`, \`missing_input_usage=1\`.
- Adding a local Usage range alone: \`unaligned=1, missing_input_usage=0\`.
- Adding 4-bit constant Feature padding alone: \`unaligned=0, missing_input_usage=1\`, Feature payload **304 bits**, still **38 bytes on wire** when rounded.
- Usage + padding: \`unaligned=0, missing_input_usage=0\`. This **does not mean Windows would start it**.
- The optional hid-parser availability check emitted a Python \`NameError\` because Windows PowerShell 5.1 stripped quoting from the Python \`-c\` inline expression. **Diagnostic script bug only**; the preceding offline analysis succeeded. Fixed in GitHub commit \`8380b638\` using a Python package list rather than nested inline quoting. No additional capture needed.
- **PrepareWpp**: pinned official Microsoft input WPP XML downloaded, validated and saved locally.
- **CaptureWpp**: exactly one uniquely matched Siri Remote Code-10 HID child restarted, \`pnputil\` exit code **0**; after restart the child **still** had Code 10 / \`0xC00000B9\`. **WPR stopped and wrote a 10.38 MiB local ETL**, proving the bounded trace collection itself succeeded. This is **not yet a confirmation that the trace contains a readable parser byte-offset message**.

**Immediate next task is ANALYSIS, not collection.** Do NOT repeat WPR or re-pair. Read the existing trace using WPA. Helper in GitHub: \`tools/windows-probe/a2854_wpp_review.ps1\` locates the latest local ETL and opens it in WPA with \`-OpenWpa\`; it never prints raw ETL messages or identifies devices in CLI output.

~~~powershell
cd "C:\Users\무조림.000\Desktop\코딩\01_working\siri-remote-for-windows"
git fetch origin main
git show origin/main:tools/windows-probe/a2854_wpp_review.ps1 |
    Set-Content "$env:TEMP\a2854_wpp_review.ps1" -Encoding UTF8
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\a2854_wpp_review.ps1" -OpenWpa
~~~

In WPA: **System Activity > WPP Trace**, open **Trace > Load Symbols**, use the View Editor to display **GUID Name**, Level, and Message. The official pinned WPRP defines **HidClassWppVerbose** provider GUID \`47c779cd-4efd-49d7-9b10-9f16e5c25d06\`. Focus on **Error** level entries around the targeted PnP restart. **Record only** an error category, relevant byte offset, and status, with anything identifying devices, users, computer names, timestamps or other devices redacted. The correct diagnosis may be **no decoded HIDCLASS WPP parser messages**; this alone does not negate the previously proven manifested HIDCLASS \`0xC00000B9\`.

The full trace is input-sensitive and must stay on the owner's PC. Do not post it or an unredacted WPA screenshot publicly. If WPA produces no WPP decoded message, STOP; the next controlled test should be an isolated **WDK HID virtual minidriver on a throwaway Windows VM**, comparing native Windows parser acceptance of the original, Usage-only, padding-only, and combined descriptors.

## 결과 해석과 중단 조건

| 결과 | 의미 | 다음 조치 |
| --- | --- | --- |
| Original 38-byte descriptor SHA-256 matches | 의도한 바이너리 확인 | 계속 |
| original: unaligned=1, missing_input_usage=1 | 300비트 미정렬 및 local Usage 누락 | 수정 후보 비교 |
| usage_plus_padding: unaligned=0, missing_input_usage=0 | 정적 후보 문제 2개 제거 | Windows 호환성 통과로 해석 금지 |
| Exactly one matching failed Siri Remote HID child: yes | 정확히 하나의 대상 노드 | CaptureWpp 선택 가능 |
| Microsoft XML validated | 공식 HIDCLASS WPP/ETW 입력 프로필 확인 | WPR 캡처 |
| WPR start failed; NO PnP restart was attempted | WPR 환경/충돌 문제, 장치는 미재시작 | WPR 사전 문제만 해결 |
| Target-only pnputil exit code: 0 | 실패한 하위 노드 하나만 재시작 | Code 10 이후 상태 확인 |
| Private WPP ETL saved in local TEMP: yes | WPP 추적 증거 확보 | WPA에서 HIDCLASS Error/offset 조회 |
| WPR stop failed | 기존 추적 세션 종료 불확실 | 재실행하지 말고 'wpr -status' 확인 |

원본 바이트/사설 GATT 접근 권한과 이 실험은 **독립**이다. WPP 상세 오류가 실제로 발견되지 않으면 똑같은 추적을 반복하지 말고 **Windows VM + 공식 WDK HID 샘플을 이용한 원본/패딩/Usage/복합 native parser A/B 실험**으로 넘어간다.

## WPP 분석 — Windows Performance Analyzer

WPA는 Windows ADK의 Windows Performance Toolkit 구성요소에서 제공된다.

1. WPR에 의해 사용자 TEMP에 저장된 **가장 최근 a2854_wpp_*.etl**을 WPA로 연다.
2. System Activity → WPP Trace 그래프의 표를 열고 **Load Symbols**를 수행한다.
3. 열 편집기의 GUID Name 열에서 HIDCLASS 관련 provider를 찾고 Error 메시지로 필터링한다.
4. Descriptor parse error 문구와 byte offset이 있으면 **번호와 오류 종류만 기록**한다. 주변의 DeviceInstancePath, BT 주소, 사용자 경로는 지운다.
5. Generic Events의 Microsoft-Windows-Input-HIDCLASS ID 3 v2도 확인할 수 있지만, 이전 manifested ETW는 Status만 보여 주었고 정확한 원인 위치는 제공하지 않았다.

심볼 서버 예시: srv*C:\Symbols*https://msdl.microsoft.com/download/symbols

**개인정보 주의:** Input WPP trace는 Siri Remote만 기록하는 것이 아니므로 다른 키보드 등의 정보가 섞일 수 있다. 추적 중 비밀번호/개인정보 입력을 피하고, 원시 ETL, Report Map .bin, 장치 ID, XML 전체를 공개하거나 업로드하지 않는다.

## 왜 BusesTrace.cmd 전체를 실행하지 않는가?

Microsoft 공식 BusesTrace.cmd는 WPR 수집 이외에도 UMDF 진단 관련 레지스트리 설정과 로그 수집을 수행할 수 있다. 이번 코드에서는 Microsoft 공식 저장소의 커밋 f9c6f70775276c1b82c3541317820b489e59a9f9의 **BusesAllProfile.wprp XML만** 고정 URL로 받아 검증한 뒤, WPR의 InputOnlyWithVerboseWppProfile을 **memory mode로 12초간** 실행한다. BusesTrace.cmd 자체는 실행하지 않는다. 실제 Windows 설치 환경에 따라 이 XML의 일부 provider가 지원되지 않을 수 있으며, 그 경우 캡처 실패는 Siri Remote의 문제라는 증거가 아니다.

파일:
- tools/windows-probe/a2854_home_lab.ps1 — 사용자용 3단계 실행기.
- tools/windows-probe/a2854_wpp.ps1 — PnP 고유성 및 Code 10 확인, 공식 WPR 프로필 준비, 제한된 WPP 캡처.
- tools/protocol/byte_alignment_matrix.py — 실제 원본의 정렬/Usage 독립변형 8개 검사.
- tools/protocol/test_byte_alignment_matrix.py — 합성 fixture 회귀 검증. CI에는 실제 리모컨/비공개 38바이트가 없다.

## 근거

- [Microsoft HIDCLASS 디스크립터 파싱 오류 및 WPP 분석](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/troubleshoot-hid-over-i2c-device-issues)
- [Microsoft 공식 BusesAllProfile.wprp 원본](https://github.com/microsoft/busiotools/blob/f9c6f70775276c1b82c3541317820b489e59a9f9/usb/tracing/BusesAllProfile.wprp)
- [Microsoft WPR CLI 문서](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/wpr-command-line-options)
- [HidP_GetCollectionDescription 문서](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpddi/nf-hidpddi-hidp_getcollectiondescription)


## 2026-10-17: WPA shows Generic Events, but HIDCLASS WPP graph missing

The owner opened the 10.38 MiB \`a2854_wpp_*.etl\` in WPA; Graph Explorer search for WPP Trace returned no matching graph and the visible Generic Events table showed only ImageId/EventMetadata/EventTrace/WinSATAssessment aggregate rows.

A first PowerShell attempt to inspect the same ETL used \`Get-WinEvent -Path ... -Oldest\` and returned exactly **93,075 records**, **but all 93,075 displayed ProviderName values were empty**. Consequently grouping ProviderName yielded one blank row and comparing the provider ID with the two expected HIDCLASS GUIDs returned 0 each. **These zeroes alone do NOT prove the ETL contains no HIDCLASS events**: WPP event payload/metadata decoding differs from normal manifested ETW and commonly needs PDB/TMF. See [Microsoft event metadata overview](https://learn.microsoft.com/en-us/windows/win32/etw/event-metadata-overview).

**Next read-only discriminating experiment:** use \`tracerpt\` to summarize raw ETL provider GUIDs and counts. \`tracerpt\` is included with Windows and produces an aggregate event count per provider even where \`Get-WinEvent\` cannot decode a provider name. This experiment must never require a new trace/re-pair:

~~~powershell
cd "C:\Users\무조림.000\Desktop\코딩\01_working\siri-remote-for-windows"
git fetch origin main
git show origin/main:tools/windows-probe/a2854_etl_provider_audit.ps1 |
    Set-Content "$env:TEMP\a2854_etl_provider_audit.ps1" -Encoding UTF8
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\a2854_etl_provider_audit.ps1"
~~~

The script prints **ONLY** total event counts and aggregated matches against:
- HIDCLASS WPP GUID: 47c779cd-4efd-49d7-9b10-9f16e5c25d06
- HIDCLASS manifested ETW GUID: 6465DA78-E7A0-4F39-B084-8F53C7C30DC6

**Important privacy qualification:** Windows tracerpt may produce a full raw XML event dump; the script directs this to a uniquely named private TEMP subdirectory and deletes it using best-effort cleanup after counting. The saved original ETL is untouched. Do not share these raw files, but console summary counts are suitable for discussion. A matching GUID in the raw event summary proves events exist but does not guarantee message decoding in WPA; no matches makes the capture/provider-enablement path the next area for investigation (not an excuse to blindly repeat the capture).

The owner restored the local shell TEMP/TMP variables from a former \`C:\Isaac\tmp\` AI training scratch location to normal user \`C:\Users\무조림.000\AppData\Local\Temp\`, and copied the 10.38MiB WPP ETL and 38-byte HID descriptor into the normal TEMP. No fresh trace capture was necessary.

