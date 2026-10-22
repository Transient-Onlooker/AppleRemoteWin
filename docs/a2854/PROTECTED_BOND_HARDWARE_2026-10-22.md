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
