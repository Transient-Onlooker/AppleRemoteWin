# A2854 protected-bond hardware gates: A/B success, C connection timeout (2026-10-22)

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

## Latest separately approved sequence: A and B pass, C stops before GATT

After the recovery check, the owner explicitly approved proceeding with another A invocation. The existing validated `4c86fae` Release binaries were used without rebuilding; subsequent intervening commits changed documentation only. No driver, certificate or Intel AX200 changes were performed.

### A: full pairing and CurrentUser DPAPI storage — success

One invocation used the same `--smp-pair-only --confirm-pairing --save-bond --seconds 60` command. A strong public-address candidate was already advertising and was acquired immediately, before a new physical-button prompt was needed.

```text
LE SMP pair-only: USB2.0-BT (33FA:0001); EP0=0x21.
EXPLICIT PAIRING TEST: may replace Apple TV pairing; saves CurrentUser DPAPI-protected bond only.
No GATT, HID, feature unlock, plaintext bond JSON, key dumps, or retries.
Controller initialized. Extended LE commands: True.
Starting scan for A2854 manufacturer prefix + Apple 0x004C + HID 0x1812.
Wake the remote / Back + Volume Up ~5 seconds after scan starts.
Strong A2854-format candidate acquired (address redacted, type=public).
LE Connection Complete OK (no BLE address/handle printed).
Beginning one bounded Legacy Just Works pairing attempt.
Stages only; raw SMP PDU/key/address content is never logged.
Pairing stage: PairingRequest.
Pairing stage: PairingResponseValidated.
Pairing stage: PairingConfirm.
Pairing stage: PairingRandom.
Pairing stage: PeerConfirmVerified.
Pairing stage: StartEncryption.
Pairing stage: LinkEncrypted.
Pairing stage: ReceiveEncryptionInformation.
Pairing stage: ReceiveMasterIdentification.
Pairing stage: Complete.
SMP full pairing / STK link encryption / responder LTK distribution completed at 958ms.
Protected bond saved to this Windows user profile (DPAPI ciphertext only).
HCI Disconnect Command Status OK.
Disconnection Complete received at 989ms: reason=0x16 (Connection Terminated by Local Host), after_host_request=True.
SMP pair-only success. Protected key saved; encrypted reconnection NOT YET verified.
```

Exit **0**. A metadata-only filesystem check confirmed a **324-byte** protected bond file in the CurrentUser profile. The file was not opened by diagnostic shell commands, copied into the repository or uploaded. No plaintext JSON was created by this mode.

### B: stored-LTK encrypted reconnect — success

Command: `dotnet run -c Release --no-build --project tools/A2854.Probe/A2854.Probe.csproj -- --usb 33FA:0001 --reconnect-only`

```text
A2854 protected-bond encrypted reconnect-only diagnostic.
No pairing request, no key persistence changes, no feature writes, no HID input subscriptions.
Wake the remote with an ordinary button press if needed (do NOT enter pairing mode).
Controller initialized. Extended LE commands: True.
LE Connection Complete OK; stored peer address redacted.
Encrypted LE reconnection confirmed by HCI Encryption Change.
HCI Disconnect Command Status OK.
Disconnection Complete reason=0x16 (Connection Terminated by Local Host).
Encrypted reconnect-only completed; no GATT or SMP traffic was sent.
```

Exit **0**. This independently launched process loaded and decrypted the saved DPAPI bond, used the stored LTK/EDIV/RAND, confirmed link encryption and disconnected with `0x16`. This verifies persistent-key reuse on the physical A2854 under the same Windows user. It does not verify cross-user portability, privacy-address rotation or long-term stability.

### C: read-only GATT probe — connection timeout, no GATT attempted

Command: `dotnet run -c Release --no-build --project tools/A2854.Probe/A2854.Probe.csproj -- --usb 33FA:0001 --gatt-inspect-only --seconds 30`

```text
A2854 protected-bond reconnect + READ-ONLY GATT inspection.
No pairing request, no key persistence changes, no feature writes, no HID input subscriptions.
Wake the remote with an ordinary button press if needed (do NOT enter pairing mode).
Controller initialized. Extended LE commands: True.
Protected-bond diagnostic cancelled/timed out at LE Create Connection from protected public address.
Cleanup: external Barrot HCI Reset OK.
```

Exit **1**. The connection window in the current source is **15 seconds**; `--seconds 30` bounds GATT inspection only after encryption succeeds. The operator was prompted to press an ordinary button during connection waiting, but confirmed pressing it only after the process had timed out. No retry followed that confirmation.

No LE Connection Complete, LTK encryption attempt, ATT discovery, service `0x1812` confirmation, descriptor `0x2908` read or report-handle mapping was reached during C. Thus `0xFB`, `0xFC` and `0xF0` handles remain unknown on this physical Windows path. The observed failure is consistent with absent/tardy connectable advertisements, but there was no simultaneous scan and the exact RF cause remains unproven. No numeric HCI rejection code was reported; cancellation and cleanup Reset completed, unlike the earlier unresponsive-controller attempt.

The protected bond remained present at **324 bytes** after B/C; only existence and length were checked externally. No bond deletion/overwrite, automatic pairing, GATT write, HID subscription or microphone operation occurred. Stop here as requested on C failure. The next experiment should coordinate a normal-button wake with a separately requested C retry, reusing this bond. **Do not run A again merely to recover from this C timeout.** Software is unchanged: prior Release validation remains 167/167 tests and zero-warning/error solution and Probe builds.
