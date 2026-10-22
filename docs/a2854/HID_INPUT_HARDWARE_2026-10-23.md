# A2854 HID input trials: real button/touch capture and normal teardown verified

**Latest result:** the separately approved **8-second trial** at `a40c25cc93f8cda6b879cdcc4ee0fb0b34579303` received **33 button FB frames and 360 touch FC frames**, with **zero malformed frames**. Both CCCD disable Write Responses, successful HCI Disconnect Command Status and Disconnection Complete reason **16** were verified. Process exit: **0**. Earlier attempts below retain their actual results; this bounded success does not establish long-term stability or Windows input synthesis.

## Third approved trial: eight-second capture and normal cleanup

On 2026-10-23 (Asia/Seoul), after the software validation described in [capture assessment](HID_CAPTURE_ASSESSMENT_2026-10-23.md), the owner separately approved one short trial and was instructed to use ordinary buttons and the touchpad, without entering pairing mode. The clean tree and verified Release binary were at `a40c25c` (218/218 tests, solution and Probe builds with zero warnings/errors). This command ran once:

```powershell
dotnet run -c Release --no-build --project tools/A2854.Probe/A2854.Probe.csproj -- --usb 33FA:0001 --hid-input-only --confirm-hid-writes --seconds 8
```

Selected actual output, excluding repetitive decoded inputs and all raw reports:

```text
Controller initialized. Extended LE commands: True.
LE Connection Complete OK; stored peer address redacted.
Encrypted LE reconnection confirmed by HCI Encryption Change.
HID report map verified: 0xFB=0x0039, 0xFC=0x003D, 0xF0=0x004D.
Discovered input CCCDs: buttons=0x003A, touch=0x003E.
Subscribed ONLY to button 0xFB and touch 0xFC; F0 feature unlock sent.
Observe remote buttons/touch for up to 8s. Microphone excluded.
CCCD disable ATT Write Responses: buttons=True, touch=True.
HID summary: button_frames=33, touch_frames=360, malformed_buttons=0, malformed_touches=0, ignored_handles=0.
HCI Disconnect Command Status OK.
Disconnection Complete reason=0x16 (Connection Terminated by Local Host).
HID capture outcome=BothReportsCaptured.
Both CCCD disable writes verified=True. Local host Disconnect 0x16 verified=True.
```

Button press/release and single-finger touch/hover decoding were observed. Each CCCD disable was independently acknowledged before host disconnect; this is actual cleanup evidence, not an inference from the exit code. The configured observation window was eight seconds, not a measured total connection duration. No per-event timing was recorded sufficient to quantify teardown latency.

Only the existing protected bond and external Barrot `33FA:0001` were used. No SMP pairing, bond deletion/overwrite, microphone collection, Windows input synthesis, driver/certificate changes or Intel AX200 operations were performed. No automatic retry followed. This documentation-only result preserves the previously tested source; any further HID-write trial requires separate approval.

## First approved attempt: connection timeout

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

At the end of this first attempt, live Windows FB/FC capture remained unverified. A fresh owner-approved retry was subsequently performed as recorded below.

## Separately approved retry: native input received

The owner then agreed to the requested single retry and actively pressed ordinary buttons. The source tree was clean at documentation commit `413ee7cccd48a53a1b833dc502695d7e17b8dfc0`; the unchanged Release binary was the verified `fde9554` build (179/179 tests, clean solution/Probe builds). The same command above ran exactly once. No rebuild, re-pairing or bond mutation was performed.

Selected runtime output, omitting repetitive decoded input lines and all raw data:

```text
Controller initialized. Extended LE commands: True.
LE Connection Complete OK; stored peer address redacted.
Encrypted LE reconnection confirmed by HCI Encryption Change.
HID report map verified: 0xFB=0x0039, 0xFC=0x003D, 0xF0=0x004D.
Discovered input CCCDs: buttons=0x003A, touch=0x003E.
button +up - (0xFB).
button + -up (0xFB).
Subscribed ONLY to button 0xFB and touch 0xFC; F0 feature unlock sent.
Observe remote buttons/touch for up to 30s. Microphone excluded.
HID summary: button_frames=59, touch_frames=720, malformed_buttons=0, malformed_touches=0, ignored_handles=0.
Protected-bond diagnostic FAILED at bounded A2854 HID report observation: IOException: Remote disconnected during HID observation (HCI reason 0x13).
```

| Stage | Actual result |
| --- | --- |
| Existing protected LTK reconnect/encryption | Connection Complete and HCI Encryption Change succeeded |
| HID report values | FB `0039`, FC `003D`, F0 `004D`, dynamically rediscovered and verified |
| CCCD descriptor handles | **Physically discovered** FB `003A`, FC `003E`; these are not inferred offsets |
| Allowed activation | Both CCCD `0100` requests and F0 `AF` Write Requests completed before the subscription-success message |
| Button input | **59 frames**, 0 malformed; press/release decoding observed |
| Touch input | **720 frames**, 0 malformed; one-finger coordinates and both hover states decoded |
| Unknown handles | 0 notifications counted on other handles |
| End of link | Disconnection Complete reason **13** during observation, before host Disconnect |
| CCCD `0000` cleanup / host Disconnect status / local reason `16` | **Not reached/verified** because the link had already ended |
| Program result | Exit **1**; no automatic retry or external Reset was needed after the observed disconnect |

Decoded button names included `up`, `down`, `left`, `right`, `click`, `tv`, `menu`, `volup`, `voldown`, `playpause`, `mute` and `siri`. The `siri` event is a button bit in report FB; no microphone report FA subscription, collection or decoding was performed. This trial does not establish every possible button, multi-contact touch, long-term stability or Windows mouse/media-key operation.

Source review distinguishes this `13` from the reason byte used by `LeCentral.DisconnectAsync`: here the observation branch consumed `central.Disconnected`, which is raised only by successful HCI Disconnection Complete events (event `05`, status 0, reason from its payload). The host Disconnect call is later in the method and was not reached. Since the disconnect task was already complete, the cleanup deliberately skipped further ATT writes, detached the handler and cleared decoder state. No successful host-disconnect status or CCCD-disabled message was printed.

The controller reason is named **Remote User Terminated Connection**. It does not identify which button, remote policy or timing condition caused termination. There are no per-event timestamps sufficient to establish that cause; do not infer power-button behavior or a missing proprietary keepalive from this capture. Some button notifications arrived before the F0-completed log line, so this single trial also does not prove F0 AF is necessary for every input report.

The Windows-native path is now physically demonstrated through encrypted GATT button/touch notifications with no Linux/WSL/server. **The complete capture-and-normal-cleanup test remains incomplete.** Preserve the bond, Barrot WinUSB, Intel BTHUSB and certificates. Before any further HID writes, obtain new owner approval; target a bounded capture that reaches both CCCD disables and host-driven disconnect reason `16`, recording event timing if needed. Do not auto-repair the bond, change Feature values or implement Windows input synthesis as a consequence of this trial.
