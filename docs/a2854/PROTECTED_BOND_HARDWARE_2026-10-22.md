# A2854 protected-bond hardware gate: scan timeout (2026-10-22)

Source and validated Release binary: `4c86fae80e9a3fb42ab72d172426f74f1aac3522`, branch `feat/a2854-support`, Windows native Barrot `33FA:0001` WinUSB. No rebuild or code changes preceded this hardware run.

The owner explicitly approved one full pairing with CurrentUser DPAPI bond storage, followed by encrypted reconnect only if A succeeded, and read-only GATT inspection only if B succeeded. The protected bond file was absent before execution. A was invoked **once**, with a 60-second scan window to allow the owner time to operate the remote. After the program printed `Starting scan`, the owner was told to hold Back + Volume Up for about five seconds.

## A: one authorized invocation, no candidate found

```powershell
dotnet run -c Release --no-build --project tools/A2854.Probe/A2854.Probe.csproj -- --usb 33FA:0001 --smp-pair-only --confirm-pairing --save-bond --seconds 60
```

Complete process output (no keys, addresses or captured packets):

```text
LE SMP pair-only: USB2.0-BT (33FA:0001); EP0=0x21.
EXPLICIT PAIRING TEST: may replace Apple TV pairing; saves CurrentUser DPAPI-protected bond only.
No GATT, HID, feature unlock, plaintext bond JSON, key dumps, or retries.
Controller initialized. Extended LE commands: True.
Starting scan for A2854 manufacturer prefix + Apple 0x004C + HID 0x1812.
Wake the remote / Back + Volume Up ~5 seconds after scan starts.
SMP pair-only cancelled or timed out at strong A2854 advertisement scan.
```

Exit code: **1**. No user cancellation was sent to the process; the bounded scan expired. The protected bond file was checked again for existence only and remained **absent**. No file contents were read, printed or uploaded.

| Stage | Actual result |
|---|---|
| Barrot USB open and controller initialization | Completed; EP0 `0x21`, extended LE commands enabled |
| Strong A2854 advertisement selection | Timed out without a candidate |
| LE Create Connection / Connection Complete | Not attempted |
| SMP Confirm/Random and STK encryption | Not attempted |
| LTK/EDIV/RAND reception and DPAPI save | Not attempted; protected bond absent |
| Disconnect / cleanup reset | No link was created; no disconnect or last-resort reset was needed |
| B: `--reconnect-only` | Not executed because A did not produce a bond |
| C: `--gatt-inspect-only` | Not executed because prerequisites A and B were not met |

This is an **advertisement-selection timeout**, not evidence of failed LE connection, rejected SMP, invalid LTK, DPAPI failure or GATT failure. No HCI error/status code was returned for a connection or encryption attempt because those commands were not issued. The available log does not distinguish missing remote advertisements, unsuitable timing, or advertisements that did not match the strong-candidate predicate.

No automatic retry, re-pairing, bond deletion, driver/certificate changes, Intel AX200 changes, ATT writes or HID subscriptions followed. Before another A invocation, obtain renewed approval for the single attempt and coordinate the remote operation after scanning begins. Successful protected save remains a prerequisite for B and C. Previous software verification remains 167/167 Release tests, with solution and Probe builds at zero warnings/errors; this documentation-only update does not claim a new test run.

## Second invocation: separately reauthorized, candidate found, HCI timeout

The owner clarified that the remote had not been operated during the first scan, then explicitly approved one further A invocation and began holding the pairing buttons. The same validated `4c86fae` Release binary and exact command above were used. The intervening `b227cd5` commit changed documentation only.

Complete process output:

```text
LE SMP pair-only: USB2.0-BT (33FA:0001); EP0=0x21.
EXPLICIT PAIRING TEST: may replace Apple TV pairing; saves CurrentUser DPAPI-protected bond only.
No GATT, HID, feature unlock, plaintext bond JSON, key dumps, or retries.
Controller initialized. Extended LE commands: True.
Starting scan for A2854 manufacturer prefix + Apple 0x004C + HID 0x1812.
Wake the remote / Back + Volume Up ~5 seconds after scan starts.
Strong A2854-format candidate acquired (address redacted, type=public).
SMP pair-only FAILED at LE Create Connection / LE Connection Complete: TimeoutException: The operation has timed out.
Cleanup Reset failed: TimeoutException. Check dongle before retry.
```

Exit code: **1**. Strong advertisement selection succeeded. No successful LE Connection Complete, SMP stage or key-distribution stage was logged. A file-existence-only check after exit confirmed that the protected bond remained **absent**. B and C were not executed; no third pairing invocation was made.

The failure is at the HCI connection procedure, before SMP or DPAPI. Source review shows a five-second HCI Command Status/Complete timeout in `HciHost.CommandAsync`; the connection procedure has a separate 12-second cancellation window. The current error omits the HCI opcode and event timing, so the log does not prove exactly which connection command/completion wait failed. No numerical controller rejection status was returned. The independent cleanup Reset timeout raises concern about controller responsiveness or USB event reception, rather than proving a remote pairing rejection. `ScanPeerAsync` also suppresses scan-disable cleanup exceptions, and the probe does not subscribe to `UsbHci.Failed`; those diagnostic gaps should be addressed before interpreting a future timeout more precisely.

Recommended recovery: physically unplug/replug **only the external Barrot dongle**, then perform an HCI-only Reset/Read Local Version check before approving another pairing invocation. Do not reinstall its driver, alter the Intel adapter or remove bonds. No additional hardware command was issued after this failed invocation's built-in cleanup attempt.

## Recovery check after external dongle unplug/replug

The owner confirmed physically unplugging and reconnecting the external dongle. The existing validated Release binary was then invoked once with `--usb 33FA:0001 --hci-only` (no scan, pairing or bond access).

```text
USB controller: USB2.0-BT (33FA:0001); HCI EP0 request=0x21.
HCI Reset OK; Read Local Version OK: HCI=0x09, revision=0x0202, LMP=0x09, manufacturer=0x08E7, subversion=0x0002.
HCI-only complete: no BLE scan or pairing attempted.
```

Exit code: **0**. HCI command/event responsiveness is restored after physical reconnection. This does not establish the root cause of the previous timeout or verify SMP, DPAPI saving, encrypted reconnect or GATT. Another A-stage pairing invocation requires renewed approval; no further pairing was automatically started.
