# A2854 Windows native — DPAPI bond, encrypted reconnect and read-only GATT gates

**2026-10-22 — Windows software validation completed after review of `8671444`; new radio experiments remain pending.** The earlier physical A2854 Just Works full pairing verified peer Confirm, STK HCI LE encryption, LTK + EDIV/RAND receipt, and HCI Disconnection Complete `0x16` at 963ms. That run erased its keys without saving. The protected bond file was checked for existence only and was absent during this validation; no fresh pairing, encrypted reconnect or GATT hardware inspection was executed.

## Windows verification and fixes (2026-10-22)

Subsequent hardware result: [one approved A invocation timed out during advertisement selection](PROTECTED_BOND_HARDWARE_2026-10-22.md). No connection or SMP exchange occurred, no protected bond was created, and B/C were not executed. The software validation below remains valid; protected storage with a physical bond, encrypted reconnect and GATT inspection remain unverified.

- Environment: Windows, .NET SDK `10.0.402`, original working directory on `feat/a2854-support`; the worktree was clean before fast-forward from `6cd4bbb` to `8671444`.
- Initial solution build failed with `CS1061` in `Gatt.cs`: the descriptor list used an unnamed tuple but referenced `.Handle`. Named tuple fields fix this actual compile failure.
- Final Release solution and A2854.Probe builds: **0 warnings, 0 errors**. Full Release tests: **167 passed, 0 failed, 0 skipped**. Separate filters: **ProtectedBondStoreTests 6/6**, **GattValidationTests 10/10**. DPAPI roundtrip, tampered ciphertext, size limits and no-overwrite tests used synthetic keys and temporary files only. The non-Windows rejection case returns early on Windows; cross-account decryption was not exercised.
- Protected-bond loading now bounds reads before allocation to reject oversized files without reading the whole file. CurrentUser DPAPI flags and the existing native input/output wipe/free paths were reviewed. The original bond is retained on decrypt/save failure; atomic no-overwrite file moves remain in place.
- GATT discovery rejects value handles outside the service or overlapping another declaration. Optional cancellation tokens preserve existing callers; the probe applies `--seconds` to the complete GATT inspection. Disconnect/detach fails pending and future requests. Cancellation or timeout closes the ATT client so a late response cannot satisfy a subsequent request; cleanup must end that link.
- Encryption Change and Disconnection Complete now match the pending encryption's connection handle. Regression tests cover unrelated events and cancellation followed by a new encryption attempt. Existing first-generation decoder and fake-remote BLE tests remain included in the full suite; no first-generation physical hardware test was run.
- Read-only PnP check: one Barrot `33FA:0001`, `WINUSB`, `oem86.inf`, version `1.0.0.0`, status OK/problem 0; one Intel AX200 `8087:0029`, `BTHUSB`, `oem116.inf`, version `23.160.0.9`, status OK/problem 0. No drivers, certificates or system pairings were changed.
- Before the next hardware gate, obtain explicit approval for one `--smp-pair-only --confirm-pairing --save-bond` run. It may replace the Apple TV association. On successful protected save, proceed in order to encrypted reconnect and then read-only GATT discovery. Never delete/overwrite an existing protected bond or re-pair as an automatic recovery.

## Security and source boundaries

- New `src/AppleRemote.Core/ProtectedBondStore.cs` uses Windows **CurrentUser DPAPI** (`CryptProtectData` without `CRYPTPROTECT_LOCAL_MACHINE`) to protect the exact legacy bond: 16-byte LTK, 16-bit EDIV, 8-byte RAND, and observed address/type, in one versioned binary structure. The encrypted data is stored by default under `%LOCALAPPDATA%\AppleRemoteWin\A2854\bond.v1.dpapi`, **not in the Git repo**. The current-user Windows logon and machine protection model applies; do not copy this blob as a device-independent key backup. The old plaintext `BondStore` is **not used** by any of the new modes.
- New mode `--smp-pair-only --confirm-pairing --save-bond --usb 33FA:0001` performs **one** approved physical full pairing and, only on successful receipt/validation of the complete LTK+EDIV+RAND, writes **DPAPI-protected ciphertext**, then disconnects. `--save-bond` is separate from `--confirm-pairing`. Without `--save-bond`, the prior in-memory-only behavior remains. An existing protected bond blocks implicit overwrite; do not delete an existing bond as an automatic workaround.
- New `--reconnect-only --usb 33FA:0001` **does not issue SMP Pairing Request**, use GATT or modify the bond. It reads only a protected bond, connects to the previously observed **public** BLE address, starts encryption using the stored LTK/EDIV/RAND, verifies HCI Encryption Change, disconnects cleanly, and zeroes secret arrays. **No fallback re-pairing.** If the bond's address type is random, reconnect is refused: IRK-based resolution was not negotiated and cannot be inferred from one address.
- New `--gatt-inspect-only --usb 33FA:0001` does the same protected encrypted reconnect, then reads **only ATT/GATT metadata**: primary services, HID service 0x1812, Report characteristic 0x2A4D, Report Reference descriptors 0x2908 and their two-byte ID/type. Reports whether 0xFB button input, 0xFC touch input and 0xF0 feature are present, including **local GATT value handles**. It sends **no ATT attribute writes, no CCCD subscriptions, no feature unlock 0xAF, no microphone reads, no raw HID input logging**, and does not change stored secrets.
- Both reconnect modes require an **explicit** dedicated Barrot WinUSB USB ID `33FA:0001`. They neither touch built-in Intel AX200 nor Windows device pairings, driver installations or certificate stores. Errors, timeouts and disconnects are reported by stage; the external Barrot controller alone is reset as a last-resort cleanup if a link could remain.
- `GattClient` discovery now rejects malformed zero-length entries, impossible entry widths, misaligned frames and non-monotonic ranges rather than looping forever or using incorrect characteristic/descriptor handles.
- DPAPI tests (Windows-only) cover save/load/delete, ciphertext-vs-plaintext, corrupted files and invalid sizes; fake-HCI tests cover encrypted reconnect and read-only GATT metadata. **Windows execution results are recorded above; physical reconnect and GATT inspection remain unverified.**

## Critical operator sequence (separate approvals)

**Gate A — Source verification, no radio pairing**:
1. Preserve local edits, fetch `feat/a2854-support`, and build the exact source (do not rely on stale `--no-build` binaries). Run full .NET 10 Release solution tests and A2854.Probe Release build on Windows. In addition to previous 148 passing tests, execute `ProtectedBondStoreTests` and `GattValidationTests`. Diagnose/fix regressions and commit fixes before hardware use.
2. Review DPAPI P/Invoke marshaling, file handling, memory zeroing, path protections and backward compatibility. Never print real key material or publish protected ciphertext, addresses or pairing packets.

**Gate B — new physical bond, separately authorized**:
- **Warn:** the previous successful bond was discarded, so a new pairing is necessary if the remote is to reconnect to this Windows controller. It may disrupt or replace the A2854's Apple TV association.
- After explicit user approval, run exactly once, with the remote awake after the scan starts:
  ```powershell
  dotnet run -c Release --project tools/A2854.Probe/A2854.Probe.csproj -- --usb 33FA:0001 --smp-pair-only --confirm-pairing --save-bond --seconds 30
  ```
- Expected non-secret result: successful SMP Confirm/Random, STK link encryption, LTK/EDIV/RAND receipt, **DPAPI protected bond saved**, clean local disconnect reason 0x16. Do not upload the protected file; do not auto-retry.

**Gate C — encrypted reconnection, only after B succeeds**:
- Do not hold pairing keys or press Back+VolumeUp; wake with an **ordinary** button press if needed. A persistent-public-address LE direct-create may still fail if the remote is not advertising. Fail safely; do not invoke a new pairing or delete keys automatically.
  ```powershell
  dotnet run -c Release --project tools/A2854.Probe/A2854.Probe.csproj -- --usb 33FA:0001 --reconnect-only
  ```
- Expected: LE Connection Complete, HCI Encryption Change OK with the **stored** LTK/EDIV/RAND, local disconnect reason 0x16. This has not yet happened on hardware.

**Gate D — read-only GATT HID mapping, after C succeeds**:
  ```powershell
  dotnet run -c Release --project tools/A2854.Probe/A2854.Probe.csproj -- --usb 33FA:0001 --gatt-inspect-only
  ```
- Expected: HID service 0x1812, 0x2A4D characteristics and the report references for 0xFB/0xFC/0xF0 if implemented by the physical remote. Their exact Windows HCI attribute handles are **not yet known**. This mode must not subscribe, unlock or actuate any inputs.

**Gate E — separate future feature activation and 0xFB/0xFC notifications**:
- Only after read-only inspection, build a narrow, user-authorized HID-specific probe that writes to the **confirmed** feature 0xF0 and explicitly allowlists notifications to 0xFB and 0xFC. Exclude microphone 0xFA from capture. This is not implemented by new read-only mode.

## Interpretation and limitations

- DPAPI protects local bond material at rest for the Windows user; it does **not** add MITM protection to Legacy Just Works, which is an unauthenticated association mode.
- A remote-reported public address was observed in the previous sessions; bonded BLE privacy/address rotation under other modes is **unverified**. No IRK was distributed in the observed legacy security feature exchange; if address handling differs later, stop and investigate.
- Existence of a DPAPI blob proves neither that its keys remain valid on the remote nor that reconnect succeeded. If encryption is refused, **do not silently re-pair, delete the blob or overwrite the bond**. Capture only HCI status/reason and investigate.
- The old `--full-legacy-driver --confirm-plaintext-bond` mode remains distinct and writes plain JSON; it should not be used for the A2854.
- Linux BlueZ earlier confirmed raw 0xFB/0xFC report formats, but the new Windows WinUSB path has not yet delivered 0xFB/0xFC HID notifications.

References:
- [Microsoft — CryptProtectData](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)
- [Bluetooth SIG — Core Security Manager](https://www.bluetooth.com/wp-content/uploads/Files/Specification/HTML/Core_v6.3/out/en/host/security-manager-specification.html)
- [2026-10-22 physical SMP negotiation](SMP_NEGOTIATION_2026-10-22.md)
