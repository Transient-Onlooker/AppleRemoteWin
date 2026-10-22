# HID capture assessment and short cleanup trial preparation

> **Subsequent hardware result:** after separate owner approval, the verified `a40c25c` Release binary completed the eight-second trial: **33 FB / 360 FC frames**, zero malformed, both CCCD disable responses, HCI Disconnect Command Status OK, Disconnection Complete **0x16**, exit **0**. See [actual sanitized output](HID_INPUT_HARDWARE_2026-10-23.md#third-approved-trial-eight-second-capture-and-normal-cleanup). The software-only preparation record below describes the earlier validation phase; its pending-test statements are historical.

Software-only follow-up on 2026-10-23, starting from clean `feat/a2854-support` HEAD `a6f5d18539b3c17694baf6f4c83fc1f7b42b3a03`, fast-forwarded from the previous local documentation checkpoint. No real connection, GATT write, pairing or input subscription was run during this validation.

The earlier [physical trial](HID_INPUT_HARDWARE_2026-10-23.md) still stands as **59 FB button frames, 720 FC touch frames, zero malformed frames**, followed by peer disconnect `13` and historical CLI exit 1. This follow-up changes software classification; it does not retrospectively change that recorded exit status or claim a new hardware success.

## Defects and corrections

- The initial assessment tests failed before entering their bodies: xUnit's integer `InlineData` could not bind to nullable `byte`. Accept nullable integers in the test and explicitly convert to the production API's byte type. Both negative counters and additional unexpected reasons are covered.
- A peer disconnect could arrive after the Probe's `IsCompleted` check, during HCI Disconnect, or between Command Status and Disconnection Complete. A shared, tested completion helper now keeps Command Status and the matching termination event separate. A rejected command alone never proves disconnection; a genuine peer-ended `13` can still classify previously captured input correctly. Mismatched handles, missing termination events and user cancellation cannot become successful host cleanup.
- Record each CCCD disable independently, only after its own valid ATT Write Response. Preserve the existing throwing `DisableAsync` API while exposing the detailed result to the diagnostic. A rejected first CCCD does not prevent an attempt on the second; a timeout closes the ATT client and prevents an ambiguous late response from verifying a later request.
- Reject ATT Write Response `13` with extra payload bytes. An invalid response closes the GATT client before any further requests, rather than trusting the next response as the other CCCD's acknowledgment.
- Validate mapped FB/FC payload format before decoding. Known malformed reports keep their report ID for malformed counters but do not qualify as valid input; unknown handles remain excluded. Final capture counts are frozen after detaching the notification handler.

## Outcome and cleanup remain separate

| Evidence | Capture / command result |
| --- | --- |
| Valid FB and FC, peer reason `13` | BothReportsCaptured, exit 0 for the input milestone; local `16` is **not** claimed |
| Only one valid report stream | PartialReportsCaptured, exit 1 |
| No valid report streams | NoReportsCaptured, exit 1 |
| Reasons `08`, `3E`, or another unexpected peer reason | UnexpectedLinkTermination, exit 1 even if earlier input exists |
| Observation ends, both CCCD disables acknowledged, Disconnect Command Status succeeds, matching reason `16` | BothReportsCaptured plus verified host cleanup, exit 0 if both input streams were valid |
| Host reason `16` but either CCCD disable lacks its response | Capture may be complete, but complete diagnostic exits 1 |
| Missing Disconnection Complete / mismatched handle / user cancellation | Failure; no inferred `16` |

A `13` arriving while the host command is in progress remains a peer-ended result even if Command Status succeeds. If Command Status fails or times out but a matching peer-ended event is actually available, the command failure is recorded separately; the input milestone does not become proof of local cleanup. If no event arrives, the bounded wait fails. No arbitrary connection error is converted into capture success.

`A2854HidCaptureAssessment.Evaluate` accepts **valid** frame counts, not raw packet totals. Malformed reports cannot supply a missing stream. Extra malformed packets remain visible in diagnostic counters even when both valid streams were also received. A pure assessment with reason `null` is used only for an otherwise verified local completion; `16` arriving without successful host Command Status is not passed off as local success.

## Verification scope

Final native Windows verification: **218/218 Release tests passed, 0 failed, 0 skipped**. Full solution and A2854.Probe Release builds each completed with **0 warnings and 0 errors**. This includes the 12 original capture cases, 12 assessment cases and 27 completion/response-validation cases, plus the other 167 existing tests. The final test run took about 10 seconds.

```powershell
dotnet build AppleRemote.sln -c Release --no-restore --nologo -v minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false
dotnet build tools/A2854.Probe/A2854.Probe.csproj -c Release --no-restore --nologo -v minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false
dotnet test tests/AppleRemote.Tests/AppleRemote.Tests.csproj -c Release --no-build --no-restore --nologo --blame-hang-timeout 60s --diag <local-diagnostic-path> -v minimal
```

Build-server reuse was disabled through `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_USE_MSBUILD_SERVER=0`; final testhost startup was bounded by `VSTEST_CONNECTION_TIMEOUT=45`. These were process-local validation environment settings, not repository or system configuration changes.

Four CLI safety checks passed on the final Release DLL with `--seconds 8`: missing HID-write confirmation, wrong Intel USB ID, mixed modes, and confirmation attached to scan mode. All exited 2 before USB opening. The actual approved-form command below was **not** run. Final process inspection found no remaining `rg`, `dotnet`, `testhost`, `MSBuild` or `VBCSCompiler` processes; the single review subagent was closed.

Fake-HCI completion tests exercise peer events before/after Command Status, command rejection racing with peer termination, a missing Command Status, missing termination events, cancellation before and during the command, mismatched handles, individual CCCD acknowledgments/rejections, missing and late responses, malformed Write Responses, and malformed input payloads. Existing first-generation, protected-bond, GATT, decoder, SMP and transport regressions remain in the full suite.

One intermediate full-suite run passed 216/216 tests. A later run was aborted by VSTest after it could not connect to testhost within its 90-second startup limit; this was a test-runner startup failure, not a reported failing test case. A local variable name collision introduced during the final malformed-response hardening was corrected before final validation. Diagnostic files from the final run are kept outside the repository in the current user's local application-data diagnostics directory; they are not committed or uploaded.

## Prepared hardware command — not executed

After **separate explicit owner approval**, use the verified Release build once:

```powershell
dotnet run -c Release --no-build --project tools/A2854.Probe/A2854.Probe.csproj -- --usb 33FA:0001 --hid-input-only --confirm-hid-writes --seconds 8
```

Wake with an ordinary button, not Back + Volume Up. During the short window, press a button and move the touchpad so both streams can be assessed. The test must independently record:

1. Encrypted reconnect using the retained CurrentUser DPAPI bond.
2. Valid FB and FC input counts, malformed counters and assessment.
3. `CCCD disable ATT Write Responses: buttons=True, touch=True`.
4. `HCI Disconnect Command Status OK` and matching Disconnection Complete reason `16`.
5. The separate final flags for both CCCD responses and local host cleanup.

If peer `13` arrives again with both valid streams, capture succeeds but the local cleanup target may still be incomplete. Do not retry automatically, re-pair, delete/overwrite the bond, change Feature values, subscribe to FA microphone, synthesize Windows input or modify drivers/certificates. Intel AX200 remains outside this test.
