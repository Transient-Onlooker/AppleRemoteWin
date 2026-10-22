# A2854 HID input trial: connection timeout before ATT writes

On 2026-10-23 (Asia/Seoul), the owner explicitly approved one 30-second HID input trial. The clean `feat/a2854-support` tree and verified Release binary were at `fde9554a51d496cc8e276b460557b5896242e466`. The owner was instructed to wake the remote with an ordinary button and exercise buttons/touch, without entering pairing mode. Actual wake timing was not independently observed.

```powershell
dotnet run -c Release --no-build --project tools/A2854.Probe/A2854.Probe.csproj -- --usb 33FA:0001 --hid-input-only --confirm-hid-writes --seconds 30
```

Sanitized complete program output (no addresses, keys or raw reports):

```text
A2854 protected-bond encrypted HID input capture: 0xFB/0xFC only.
Explicit HID writes enabled: 0xFB/0xFC CCCDs and 0xF0<-0xAF only. No microphone or Windows input synthesis.
Wake the remote with an ordinary button press if needed (do NOT enter pairing mode).
Controller initialized. Extended LE commands: True.
Protected-bond diagnostic cancelled/timed out at LE Create Connection from protected public address.
Cleanup: external Barrot HCI Reset OK.
```

Process exit code: **1**. The introductory "writes enabled" line describes CLI authorization, **not completion of any write**.

| Stage | Actual observation |
| --- | --- |
| Existing CurrentUser DPAPI bond load | Passed the source's load/address-type checks; no bond modifications |
| External Barrot controller initialization | Succeeded; Extended LE commands True |
| LE connection to stored public address | Connection wait timed out (configured 15-second connection window) |
| Connection Complete / connection handle | No successful completion reported; no established handle returned to the probe |
| LTK encryption / GATT metadata discovery | Not reached |
| Button/touch CCCD activation / F0 AF | Not reached; no ATT writes issued |
| Button/touch frame counts and decoding | Not measured; the capture stage never started |
| HCI Disconnect / Disconnection Complete | No normal link-close result; no established handle was available to the probe |
| External controller cleanup | HCI Reset succeeded |

No HCI failure status or disconnection reason was reported in this attempt. Do not label the timeout as `0x3E`, a rejected LTK, or a HID/Feature failure. This evidence does not distinguish wake/availability timing from another connection-establishment issue.

There was **no automatic retry**, no SMP pairing, bond reset/overwrite, GATT write, notification subscription, microphone recording, Windows input synthesis, driver or certificate change. Intel AX200 was not used for the trial. Prior [179/179 tests and clean Release builds](HID_INPUT_VALIDATION_2026-10-23.md) still apply; this documentation-only update does not change code.

Next action requires a fresh owner-approved attempt when the remote is ready for an ordinary-button wake. Start the same verified binary and coordinate the wake with its startup message; do not re-pair or try different Feature values. Confirm connection/encryption/metadata/write results before interpreting missing input frames. Live Windows FB/FC capture remains unverified.
