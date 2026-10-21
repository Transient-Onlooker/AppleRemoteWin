# Windows-native Siri Remote — project handoff

> **CURRENT (2026-10-18):** The next-stage A2854 Windows work now lives in [Transient-Onlooker/AppleRemoteWin, feat/a2854-support](https://github.com/Transient-Onlooker/AppleRemoteWin/tree/feat/a2854-support). The owner's .NET 10 tests passed **89/89**, and the independent Windows probe built. The probe cannot open the internal Intel AX200 while it is bound to `BTHUSB`; no native Windows A2854 event capture has been demonstrated. The built-in HID Code-10 last-attempt script is committed but **not yet run on hardware**. See [updated handoff for this repository](../../HANDOFF_2026-10-18.md) and [complete authoritative handoff in the new fork](https://github.com/Transient-Onlooker/AppleRemoteWin/blob/feat/a2854-support/docs/HANDOFF_2026-10-18_A2854.md).
>
> The 2026-10-17 notes below remain historical context and should not be treated as the latest status.


**Status: RESTARTED (2026-10-17); read-only native Windows HID diagnostic implemented, real Windows device access not yet tested.**  
**Device:** Apple TV Siri Remote (3rd generation, A2854)  
**Upstream:** [azais-corentin/siri-remote](https://github.com/azais-corentin/siri-remote) · **Fork:** [Transient-Onlooker/siri-remote-for-windows](https://github.com/Transient-Onlooker/siri-remote-for-windows)

## Why this fork exists

The original application successfully receives remote control and touchpad events on Linux. Our intended *product* is different: a **native Windows app** using the normal Windows Bluetooth stack, without WSL2, USB/IP passthrough, a Linux server, or an always-open terminal.

As of 2026-10-17, the remote was successfully tested on Linux in WSL2. **A read-only Windows HID diagnostic now exists, but has not been run against physical hardware; a full Windows BLE/HID application has not been built or validated.** The current fork still contains upstream Linux code. It is not yet a Windows release.

## Entry points

- [Windows HID capability probe](../../tools/windows-probe/README.md) — read-only interface enumerator and 0xFB/0xFC test (hardware result pending)
- [Complete dated handoff (Korean)](../../HANDOFF_2026-10-17.md)
- [Sanitized HID report fixtures](OBSERVED_REPORT_FIXTURES.md)

## Records in this folder

- [PROJECT_BRIEF.md](PROJECT_BRIEF.md) — product goal, boundaries, and upstream relationship
- [EXPERIMENT_TIMELINE.md](EXPERIMENT_TIMELINE.md) — what we actually tried and observed
- [PROTOCOL_FINDINGS.md](PROTOCOL_FINDINGS.md) — tested BLE/HID facts and interpretation limits
- [WSL_COMPATIBILITY_NOTES.md](WSL_COMPATIBILITY_NOTES.md) — Linux proof-of-concept fixes and restoration instructions
- [WINDOWS_PORT_DESIGN.md](WINDOWS_PORT_DESIGN.md) — proposed architecture and unresolved Windows restrictions
- [NEXT_STEPS.md](NEXT_STEPS.md) — ordered validation gates for resuming development

## Evidence policy

“Observed” means supported by output from our own A2854/Intel AX200/BlueZ session. “Upstream” means described by the original repository. “Planned” describes unimplemented work. Do not confuse Linux event reception with native Windows functionality.

Public notes intentionally omit the physical remote's BLE address, serial number, and full manufacturer identity bytes.
