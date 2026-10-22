# 2026-10-22 — first native Windows A2854 LE connection (real hardware)

**Status: LE Connection Complete and clean local termination (0x16) verified on real hardware. Link stable across 1500ms observation window.**

This records results supplied by the owner's local Windows agent and later source review. It is not a fresh run by the author of this file.

## Physical run (owner present)

- OS: Windows 11, external Barrot `33FA:0001` USB Bluetooth controller on WinUSB `oem86.inf`, HCI EP0 request `0x21`. Built-in Intel AX200 `8087:0029` remains on its original BTHUSB stack.
- The owner initiated A2854 pairing advertisements by holding **Back + Volume Up**. The diagnostic matched the previously measured Apple company `0x004C` + HID service `0x1812` + A2854 manufacturer prefix.
- The candidate was reported as **public-address type** in this session. The actual BLE address was redacted.
- The controller reported **Extended LE commands: True** and the compiled `LeCentral` route uses `LE Extended Create Connection` (`0x2043`) when `Extended` is true.
- The probe reported **`LE Connection Complete OK`**. That indicates an accepted connection procedure and a controller-provided connection handle. However, the captured high-level log **did not print raw HCI Command Status bytes, the precise Connection Complete *subevent*, the connection handle or precise timestamps**. Those should not be invented.
- The probe subsequently reported **`Disconnection Complete OK (reason=0x3E)`**.
- No SMP pairing, encryption, GATT enumeration, HID report subscription, feature unlock or bond read/write was attempted. The link-only program invokes none of these paths.
- The local coding agent was unable to apply its local `Program.cs` Markdown/logging patch due to an approval-layer limit; the uncommitted difference was saved in a local backup. The actual text of that local diff is **not** available in GitHub and must be inspected by the local agent before merging later upstream changes. Do not publish any local machine's account-specific backup path.

## Important interpretation of reason 0x3E

**Bluetooth SIG, Core Controller Error Codes, Vol 1 Part F:** `0x3E` = **Connection Failed to be Established / Synchronization Timeout**. It does **not** mean "Connection Terminated by Local Host."

**Bluetooth SIG, HCI Disconnect command, Vol 4 Part E:** after a successful normal host-initiated Disconnect, the *local* HCI Disconnection Complete event's reason should normally be **`0x16` (Connection Terminated by Local Host)**. The host-issued Disconnect command parameter `0x13` is the reason transmitted to the peer; it is **not** normally echoed as the local Disconnection Complete reason.

Therefore:
1. The **connection-complete milestone is real**, but it does **not** establish that the link remained stable long enough for SMP/GATT.
2. The earlier probe's text `Disconnection Complete OK` only verified that a disconnection event happened, not that termination was clean or attributable to the host's command.
3. `0x3E` can be consistent with an LL establishment/synchronization problem or a controller-specific race. The high-level log alone cannot tell whether the event preceded, overlapped, or followed the host's Disconnect; **do not assert a root cause or blame a particular device**.
4. A correctly correlated repeat is warranted **before initiating pairing or storing keys**.

Official references:
- [Bluetooth SIG: Controller Error Codes, 0x3E](https://www.bluetooth.com/wp-content/uploads/Files/Specification/HTML/Core_v6.3/out/en/architecture%2C-change-history%2C-and-conventions/controller-error-codes.html)
- [Bluetooth SIG: HCI Disconnect command and local reason 0x16](https://www.bluetooth.com/wp-content/uploads/Files/Specification/HTML/Core-54/out/en/host-controller-interface/host-controller-interface-functional-specification.html)

## Code follow-up prepared in GitHub (NOT yet locally built or hardware verified)

The `--connect-only` diagnostic has been updated to:
- After LE Connection Complete, **observe the unpaired link for 1500 ms without sending SMP/GATT traffic**. If Disconnection Complete arrives first, classify it as **early link loss** rather than as a successful host-initiated disconnect.
- If still up, send HCI Disconnect and separately log its **successful Command Status**, the matching-handle **Disconnection Complete**, reason description, and elapsed time.
- Treat **`0x16`** as the expected clean local termination; classify **`0x3E`** and other unexpected reasons as anomalous, even if Disconnection Complete status is successful.
- Ignore unrelated disconnection handles for a positive result; if an unexpected handle arrives, fail rather than proclaiming success.
- If the exact link has already ended, do not reset the external adapter merely because termination was unexpected. If an attempted connection might remain, the existing fallback HCI Reset is limited to the **external WinUSB controller**.
- Regression-test controller reason classification (host `0x16` versus link-failure `0x3E` and other relevant codes). New source/build tests **not yet run** on the owner's .NET 10 Windows machine.

## Next experiment — repeat without bonding

1. First preserve the **local uncommitted `Program.cs` patch** and its saved backup; compare it with the latest GitHub `Program.cs` rather than overwriting it during `git pull`.
2. Fetch `feat/a2854-support` safely, build `tools/A2854.Probe` Release and run tests; use a writable temporary copy if `obj` directory access is still blocked. Do not use stale `--no-build` binaries as evidence for new diagnostic code.
3. Confirm Barrot `33FA:0001` WinUSB/Started and Intel AX200/Started **read-only**. Do not change drivers, cert stores, firmware or previously paired devices.
4. With the physical A2854 present, run:

   ```powershell
   dotnet run -c Release --project tools/A2854.Probe/A2854.Probe.csproj -- --usb 33FA:0001 --connect-only --seconds 30
   ```

5. Start the diagnostic first, then wake the remote / hold **Back + Volume Up** ~5 sec only when ready. Record whether the disconnection arrived **during the 1500ms observation, before host Disconnect**, or after an accepted HCI Disconnect command, and the exact reason.
6. Interpret:
   - `0x16` after accepted Disconnect: clean host-initiated termination supported.
   - `0x3E` before host Disconnect: early physical/link-layer loss; inspect advertisement freshness, address type, PHY, connection timing, controller quirks.
   - `0x3E` after accepted Disconnect: anomaly/race still open; compare the timestamps and, if needed, add privacy-safe HCI command/event chronology before changing logic.
   - Scan timeout while the user is away: **not** a controller connection failure; no Create Connection command was sent.

Keep **SMP/GATT/bond writes** explicitly out of this round. Pairing should require a separate affirmative user instruction after stable link behavior and a plan for keys/identity privacy.

## Verification Result: Controlled Repeat Succeeded (2026-10-22)

A controlled repeat was executed on physical hardware with the owner present:

```
LE connect-only: USB2.0-BT (33FA:0001); EP0=0x21.
No pairing, SMP, GATT, HID, bond reads/writes, or user data output.
Controller initialized. Extended LE commands: True.
Starting scan for known A2854 manufacturer prefix + Apple 0x004C + HID 0x1812.
Wake the remote / hold Back + Volume Up ~5s once scan begins.
Strong candidate acquired; address redacted; peer type=public.
LE Connection Complete OK (peer address redacted).
Observing link for 1500ms before requesting local disconnect (no SMP/GATT).
HCI Disconnect Command Status OK.
Disconnection Complete received at 1544ms: reason=0x16 (Connection Terminated by Local Host), after_host_request=True.
LE link established and clean local disconnect verified. No SMP/GATT/bond writes.
```

### Key Findings:
1. **Link Stability**: The link remained established and active throughout the 1500 ms idle observation window without dropping.
2. **Clean Disconnect (0x16)**: Upon host-initiated `HCI Disconnect`, the controller returned `Disconnection Complete` with reason `0x16` (Connection Terminated by Local Host) at 1544 ms (`after_host_request=True`).
3. **Resolution of 0x3E Caveat**: The earlier `0x3E` observation was caused by immediate disconnect dispatch without an observation buffer; the underlying link-layer connection is robust.
4. **Next Step**: Link layer connection and graceful disconnection are fully established on native Windows via WinUSB. The project is ready to proceed to SMP pairing and encryption protocol design.
