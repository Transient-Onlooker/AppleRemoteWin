# A2854 HID input diagnostic: Windows software validation

Validated on 2026-10-23 (Asia/Seoul), starting from `e6750376ead29ae1da0bbe15003b790b009c9a2b` on `feat/a2854-support`. The local tree was clean before fast-forward. No live HID diagnostic was run: CCCD writes and F0 activation still require separate owner approval.

**Subsequent hardware update:** the owner approved one trial of validated Release `fde9554`. It timed out before LE connection completion; no ATT writes or input capture occurred. External HCI Reset cleanup succeeded. See [complete sanitized trial record](HID_INPUT_HARDWARE_2026-10-23.md). The validation results below describe the earlier software-only check.

## Results

| Check | Actual result |
| --- | --- |
| Full `AppleRemote.sln` Release build | Passed, 0 warnings, 0 errors |
| `tools/A2854.Probe/A2854.Probe.csproj` Release build | Passed, 0 warnings, 0 errors |
| Full Release test suite | 179 passed, 0 failed, 0 skipped |
| `A2854HidCaptureTests` within that suite | 12 passed; the other 167 existing tests also passed |
| CLI safety checks without USB access | 4 passed; each exited with code 2 before USB opening |
| External USB `33FA:0001`, read-only PnP check | Exactly one present; OK, WINUSB, `oem86.inf`, ProblemCode 0 |
| Internal USB `8087:0029`, read-only PnP check | Exactly one present; OK, BTHUSB, `oem116.inf`, ProblemCode 0 |
| Physical Windows button/touch notification capture | Not attempted; no new success claim |

Build commands:

```powershell
dotnet build AppleRemote.sln -c Release --nologo -v minimal
dotnet build tools/A2854.Probe/A2854.Probe.csproj -c Release --nologo -v minimal
dotnet test tests/AppleRemote.Tests/AppleRemote.Tests.csproj -c Release --no-restore --nologo --blame-hang-timeout 90s -v minimal
```

The final full test run reported `179/179`, duration approximately 2 seconds. VSTest's Blame collector reported that no sequence file was produced because all tests completed; this was not a test failure. The final test-only fixture refinement did not change the Core or Probe binaries built above.

## Issues fixed

- Made physical value-handle matching mandatory in discovery: FB `0039`, FC `003D`, F0 `004D`. The previous API allowed callers to omit the optional strict check. Any mismatch now fails before writes.
- Made the discovered map immutable and tied write permission to the exact GATT client that discovered it. A constructed map, including one containing the expected numbers, or a map reused on another client is refused before any write.
- After partial enable failure, attempt both CCCD disables with a separate five-second cancellation budget and preserve the original failure. During disable, still attempt the touch CCCD if the button CCCD is explicitly rejected. A timed-out/cancelled ATT client remains closed: no speculative retry after an ambiguous response; external link teardown remains the fallback.
- Replaced the anonymous Probe notification handler with a removable handler. A lock serializes decoding, final counters and in-memory input-state clearing. An already-dispatched late callback cannot mutate the decoder after capture teardown. The pending observation delay is cancelled on early disconnect.
- Print frame counters on failure as well as success, and retain the original diagnostic failure stage during cleanup.

The fake A2854 layout now uses the measured value handles, but its CCCDs `003A` and `003E` are **synthetic test choices**, not hardware measurements. The real CCCDs must still be discovered by UUID `2902` within each characteristic's descriptor range; no offset is assumed. The legacy fake layout remains the default and existing first-generation regressions passed.

The 12 HID cases cover allowlisted enable/disable writes and button/touch decoding, unknown-report exclusion, missing/duplicate CCCD and Report Reference metadata, mismatched measured handles, forged maps, client-bound maps, partial enable rejection, and cleanup after the first disable is rejected. The duplicate-CCCD fixture contains a valid FB Report Reference so its assertion checks the actual CCCD uniqueness branch. Probe callback lifetime safety was reviewed in source; the mock tests do not replace a live transport test.

CLI checks used the built Release DLL with: missing `--confirm-hid-writes`, wrong USB ID `8087:0029`, simultaneous HID/scan modes, and `--confirm-hid-writes` attached to scan mode. All were rejected before USB opening. The missing-bond path was reviewed in source; the existing real bond was not moved or modified to exercise it.

## Preserved boundaries and next trial

No driver/certificate changes, re-pairing, bond deletion/overwrite, microphone subscription/data recording, Windows input synthesis, or physical ATT writes were performed. No real address, key, raw security/HID payload or protected bond file is included in this record.

Only after separate owner approval, run one bounded trial from the verified Release build:

```powershell
dotnet run -c Release --no-build --project tools/A2854.Probe/A2854.Probe.csproj -- --usb 33FA:0001 --hid-input-only --confirm-hid-writes --seconds 30
```

Use the existing DPAPI bond and wake the remote with an ordinary button, **not Back + Volume Up**. The only intended attribute writes are `0100`/`0000` to the two dynamically discovered input CCCDs and one `AF` to F0 `004D`. A failed enable may require additional disable attempts of those same CCCDs. F0's undocumented state cannot be explicitly restored; only notification disables and link teardown are attempted.

Record button and touch frame counts/decoding separately, malformed counts, CCCD cleanup, HCI Disconnect Command Status and Disconnection Complete reason (`16` expected). Any missing report is partial success at most. Stop on failure without automatic pairing or alternative Feature values. See [implementation and safety gate](HID_INPUT_DIAGNOSTIC_2026-10-22.md).
