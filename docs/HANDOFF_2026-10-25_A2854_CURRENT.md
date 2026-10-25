# A2854 / AppleRemoteWin — CURRENT HANDOFF (2026-10-25, KST)

> **START HERE when this conversation is lost.** This is the current, concise operational truth. The long chronological record is [HANDOFF_2026-10-23_A2854.md](HANDOFF_2026-10-23_A2854.md). Prior dated README/QA statements are **historical** where they conflict with this page. Check the GitHub branch HEAD again before making a code change. Do **not** treat this documentation commit as a Windows-build or hardware QA pass.

## 2026-10-25 — Latest independent Windows QA PASS: `d6d97e0`

**Saved calibration default follow-up:** At the user's explicit request, valid saved five-point measurements are now preferred for pointer axes as well as the existing automatic visual projection. Settings loaded from older releases migrate the previous default-off option to on once (`A2854CalibrationDefaultsVersion=1`); subsequent explicit opt-out is preserved. Pointer/keyboard enable switches remain independent and are not turned on by this migration. Missing or invalid profiles retain the existing raw-axis fallback. Measurements remain local and are never embedded in source. This supersedes historical descriptions below that call pointer calibration default-off. No new live BLE or pointer trial is implied.

Local Windows verification for this follow-up: Release solution build 0 warnings/errors; full tests **435/435**, including three new settings migration/default/opt-out regressions compiled against the actual Settings source. No live BLE or SendInput trial was run.

**Validated application HEAD:** `d6d97e01ff099a7ba95b59f17187e3107e214d47` on `feat/a2854-support`, clean working tree. The user reported independent **overall PASS** for the requested release-build, core regression and offline session/UI mock scope:

- Release build: **0 warnings / 0 errors**.
- Entire suite: **432/432**; `KeyTests`: **34/34**.
- Actual `RemoteSession` exercised with mock/fake devices plus WPF checks: **233/233**.
- Repeated `Stop()` with all key-ups initially rejected: pending retained; later stopped-session `Stop()` retries; rejection persists without losing pending; a later accepted release clears it. **Five more Stop calls after success caused zero extra key-ups**.
- Shared F5 owner ordering, duplicate keydown prevention, map changes and Keyboard OFF cleanup remained passing.
- Settings: both remote models × three languages × every tested viewport width, **0 DIP horizontal overflow**.
- **No defects found in this validation scope**. No source/user setting/DPAPI bond/five-point calibration changes, no BLE or actual Windows pointer/keyboard injection, and no leftover processes.
- Detailed local-only report: `AppleRemoteWin-Validation/stop-retry-d6d97e0/QA_REPORT_KO.md` (not retrieved from GitHub).

**Scope boundary and next work:** This PASS is for simulated input and WPF regression, **not** a field claim about real A2854 input dispatch. Earlier independent QA explicitly left the **foreground keyboard-capture hook**, **right-click KeyPicker context menu**, and **full real mouse drag-and-drop sequence** as unverified interactive UX behaviors. These are the next safe, separate, offline manual/UI-only validation targets. Do not trigger real BLE, keyboard/mouse `SendInput`, driver modifications or changes to protected bond/calibration without explicit user approval. Documentation-only commits after this validated SHA do not change the application build/test baseline.

## 2026-10-25 post-handoff QA and follow-up source (current)

The user reported **independent Windows QA at application commit `85f20a7`**: clean worktree, Release **0 errors / 0 warnings**, **387/387** full tests and **26/26** pointer tests. In a bounded real A2854 session, normal movement, clicking, click wobble/release stabilization and dragging were subjectively **PASS**. **37 mouse-down/up pairs** were observed, both HID CCCDs were disabled on **the on-screen Stop path**, Disconnect was accepted and the **actual local termination reason was 0x16**. Original preferences and the DPAPI bond / five-point calibration file were unchanged, with zero residual processes. This was a practical assessment rather than precise pointer-motion instrumentation. The user-local detailed report is `AppleRemoteWin-Validation/pressure-click-85f20a7/QA_REPORT_KO.md`; it is not in GitHub. **Subsequent Start/reconnect was not separately confirmed in this report.**

**A newly committed follow-up after that QA must be treated as source-only until independently built and tested.** It changes the empirical pre-click pressure guard from per-axis limits to **radial displacement**, cancels any pending pressure suppression on a deliberate larger swipe (including a diagonal), and adds two fake-pointer regression tests. It also removes a nested disabled ScrollViewer so only `SettingsViewport` handles mouse wheel input, and synchronizes saved visual five-point projection after restoring the persisted A2854 model on cold start. The actual Windows pointer's *separate* calibrated-axis opt-in remains unchanged, as do click/drag thresholds, bonded BLE and USB safety controls. Validate Release tests, Settings wheel/keyboard scrolling in both layouts, and saved visual projection after restart **without** changing actual user calibration values.

## 2026-10-25 — optional A2854 center keyboard binding (source, Windows QA pending)

User request: keep A2854 pointer movement while remapping the physical **center 0xFB.click** to a chosen keyboard key, keyboard combo, application or macro through the existing `Bindings[click]` KeyPicker. The pre-existing key-picker works with the pointer **off**, but pointer mode previously claimed click exclusively as Windows left-mouse.

A new opt-in preference `A2854ClickAsKeyboard` (default **false**) switches *only* the center button's ownership while the A2854 pointer is on: **false** = physical left mouse click, **true** = existing user-defined keyboard/shortcut binding (requires separately enabled `KeyboardEnabled`). Pointer *touch movement* remains on. The physical click never generates both mouse-left and keyboard key in one mode. With pointer off, prior keyboard center-key mapping remains unchanged. TV/Siri/Power remain inert for OS keyboard, as before. First-gen and ESP32 code paths do not change.

UI: Settings → A2854 mouse card → **Use center clickpad button as a keyboard binding**; enable keyboard input lower in Settings; assign **Clickpad center** in the existing left-side A2854 KeyPicker. The option is disabled while keyboard injection is off and the saved choice is preserved. Switching the ownership while connected proactively releases any mapped key and attempts mouse-up, retaining existing failed-release diagnostics. User settings save the new non-secret Boolean (no protected bond, five-point calibration or BLE changes). `A2854ButtonRouting` is a pure portable classifier shared by Windows session and UI combo/macro dispatch to avoid duplicate actions; new fake sink unit tests cover modes, custom F5/Space, TV/Siri/Power exclusions, and held release.

**New source is not Windows-built, WPF-verified, or physically field-tested yet.** Request a read-only Release build/full tests, focused `A2854ButtonRoutingTests`, localization consistency, and offline WPF Settings binding checks. Physical mouse/keyboard `SendInput` and BLE may only be tested after separate user approval, and the default mouse-click behavior must not regress.

## 2026-10-25 — G HUB-inspired press-to-assign key capture (new source, QA pending)

The user asked for a straightforward Settings key assignment workflow: click a remote input, select **Record a keystroke…**, physically press the desired key (or chord), preview it, and confirm. Proprietary Logitech G HUB assets or implementation were **not** copied. The existing first-party `KeyPicker` now exposes this workflow for **all mapped remote buttons** (A2854 and first-gen); it reuses the pre-existing `KeyComboWindow` foreground-only low-level keyboard hook, with a capture-specific localized prompt/title, Cancel button, and modifier-only rejection. A single supported key (e.g. F5 or Space) persists as a **normal KeyMap binding** (press=down, release=up); combinations such as Ctrl+C or Win+D persist as existing one-shot `#Ctrl+C`/ `#Win+D` mappings. An explicit confirmation is required; closing or cancellation leaves prior mappings unchanged. Unsupported/unrecognized capture is not saved. `CapturedKeyBinding` is a pure portable translator covered by new offline tests.

**No new background keyboard listener**, no Bluetooth/GATT/bond change, no automatic A2854 Start, no new Windows input injection except previously opt-in mappings. The A2854 center keyboard routing still requires the separately enabled keyboard input and `A2854ClickAsKeyboard` when pointer movement is on. Verify Release builds, complete tests, new capture tests, WPF language text, and actual focused-dialog capture via an offline local interactive QA; distinguish such UI capture from an actual remote BLE test. The new source is **not yet independently Windows-tested**.

## 2026-10-25 — G HUB-inspired assignments panel and keyboard key-up acknowledgement (new source, QA pending)

**Independent local QA for earlier `f7b1519` (NOT for later commits):** Release 0 errors/warnings; **398/398** full and **63/63** focused tests passed. Offline routing of F5/Space, click exclusivity, normal Stop release, new three-language option and 1024/1280/1600 DIP (0 overflow) passed. Real device and new `72ed14d` key-capture feature were not tested. QA identified a **pre-existing safety defect**: rejected keyboard key-up was discarded by `KeyBinder`; retries at normal release, remap, Keyboard OFF or Stop could not succeed. Local report `AppleRemoteWin-Validation/keyboard-bindings-f7b1519/QA_REPORT_KO.md` is user-local, not committed.

**Source follow-up (not independently built):** `IConfirmedKeySink` extends but does not replace the existing portable `IKeySink`. Windows `KeyboardInjector.TryKey` reports actual `SendInput` acceptance. `KeyBinder` now retains the exact original VK for each held physical input, preserves key-up rejects for later retries (including after remap or Keyboard OFF), prevents duplicate down while release is pending, and tracks gesture key-up failures. `RemoteSession` warns about rejected key-up on the A2854 path and retries pending keyboard releases in the existing mode-toggle/Stop/input-fault cleanup paths. Existing legacy fake sinks still function; no BLE, DPAPI or driver changes. New fake rejecting-sink regressions cover normal release, remap, Keyboard OFF, Stop, gesture release and rejected down. **Actual failure recovery must be validated, not assumed.**

**UX requested by owner:** G HUB's Assignments workflow, **not** a mere popup menu. The current Settings A2854 remote illustration remains on the left; its usable key pickers now act as click-to-select targets with accent border, while the right pane gains a searchable assignment browser with categories **Commands / Keys / System / Macros / Programs**, selected button/current binding summary, press-to-record, clear and restore-default actions. Selecting a command assigns it to the highlighted button; mouse-dragging a command to a button assigns directly to that target. Right-click on a target preserves the full original KeyPicker menu. Assignments reuse existing `KeyChoice`, `KeyMap`, software/shortcut/macro settings; this does **not** add profile switching, G-Shift or Logitech proprietary code/assets. The keyboard capture dialog still activates its temporary hook only while open and focused. The new UX and key-up fix are source only pending Windows Release + offline WPF QA, not proven by the prior 398/398 run.

## 2026-10-25 — Shared-key lifetime: independent QA and follow-up fix (source only)

User-reported **Windows read-only QA on `c8782f6`**: Release build **0 warnings, 0 errors**; **421/421** full tests, **50/50** focused tests; all **six** keyboard key-up failure/cleanup scenarios passed. Offline assignments, search, drag target/drop, clear, default, save/reload passed; ko/en/fr × 1024/1280/1600 DIP had **0 horizontal overflow**. Actual key capture hook, right-click popup and the full manual drag gesture were **not tested**, nor were real BLE/`SendInput`. User-local detailed report `AppleRemoteWin-Validation/assignments-c8782f6/QA_REPORT_KO.md` is *not in GitHub*.

**Bug verified by QA:** when two physical inputs were mapped to the same VK (e.g. `click=F5`, `menu=F5`), releasing the first emitted an OS F5 key-up even though the second remained held. This was caused by the existing per-button rather than per-VK send/release policy.

**Follow-up source:** `src/AppleRemote.Core/Keys.cs` `KeyBinder` now tracks each input's original owned VK and a *single acknowledged down per distinct key*. Additional owners do not resend down; releasing a non-final owner does not send up; releasing the last owner sends exactly one key-up. If that final key-up fails, the VK remains in a key-level pending retry set for subsequent Stop, mode change, map replacement or retrying input. New keydowns are blocked until rejected key-ups can be retried. `ReleaseAll` releases each unique OS-down VK once and retains failed releases; mapping changes release the *old* VK, not the newly mapped key. The same ownership prevents transient gesture pulses from releasing a physically held key. Portable fake-sink regressions added for shared F5 in both orders, Stop idempotence, failed final up, remap failure, gesture overlap, pending-up gating and distinct-key behavior.

**Validation pending on new source.** The 421/421 result applies only to `c8782f6`, not the follow-up commit. Run Windows Release full + focused `KeyTests` with fake rejected key-up, verify Settings UI without changes, and report back. Do not modify BLE, protected DPAPI bond, five-point calibration or drivers. No real input injection without additional user approval.

## 2026-10-25 — Retrying keyboard key-up after session Stop (source-only follow-up)

Independent local Windows QA at **`a63325b`**: clean worktree, Release **0 errors/0 warnings**, full **429/429**, `KeyTests` **31/31**. Shared F5 ownership worked for both release orders and mode/Stop cleanup. Offline WPF two models × ko/en/fr × 1024/1280/1600 DIP had 0 horizontal overflow. External mock/WPF suite was **209 PASS / 1 FAIL**: when all key-up attempts during the initial Stop were rejected, `KeyBinder` correctly preserved pending keys but a **later `RemoteSession.Stop()` made 0 further attempts**, because `RemoteSession.cs:401` returned immediately if `_cts == null`. The detailed report `AppleRemoteWin-Validation/shared-keys-a63325b/QA_REPORT_KO.md` exists only on the user's local Windows environment; not in GitHub. No physical BLE or Windows input injection was attempted.

**Narrow repair (unverified new source):** When `Stop()` is called with `_cts == null`, it now holds the existing lifecycle and input gates, checks **only** `_binder.HasPendingReleases`, and if needed runs `ReportKeyboardReleaseResult()` exactly once before returning. No BLE reopen, new shutdown wait, pointer injection, settings rewrite or duplicate "Stopped" event; normal active-session shutdown is unchanged. Repeated stopped-session Stop attempts can retry until Windows accepts the pending key-up, and calls with no pending key-up remain inert. The ack sink still determines real OS acceptance; do not claim recovery if Windows persistently refuses. Added portable `KeyTests` covering repeated rejects then success, idempotence after success, and multiple pending VKs. They prove binder retry behavior, **not** the `RemoteSession` lifecycle path; independent Windows mock QA must specifically invoke `Stop()` twice/three times on an already stopped session using a rejecting sink or equivalent injection shim, and distinguish pending from zero-pending calls.

**New commit requires Windows Release rebuild and tests**; 429/429 are previous `a63325b` evidence only. Keep protected DPAPI bond, measured calibration and drivers untouched. No automatic BLE reconnection or `SendInput` field test.

## 0. Quick resume / ownership

- GitHub: `Transient-Onlooker/AppleRemoteWin`; work branch: `feat/a2854-support`.
- **Latest application source baseline as inspected when this handoff was written:** `85f20a73e704ce5faf48849b7dd90268e9dec06e` (`fix(a2854): stabilize physical click pointer with pressure-aware jitter guard`). It is a **successor** of `abd69aab0d909e1aefca5976c8f949e9d5844b06`; this new click code has **not yet received an independent Windows QA result** in the current conversation. This handoff is a *documentation-only* successor commit; fetch actual HEAD, not hardcoded SHA.
- **Previously independently QA-verified app source:** `abd69aa`: Release build **0 errors / 0 warnings**, full **381/381**, focused Stop **28/28**. WPF Settings for `ko/en/fr × first-gen/A2854 × 1024/1280/1600 px`: **0 DIP horizontal overflow**. Actual A2854 encrypted connection held **~27 seconds**; **window X-close** disabled both HID CCCDs, HCI Disconnect accepted, actual Disconnection Complete reason **0x16**. **Stop-button click and subsequent Start/reconnect were not tested** in that session. Protected bond, measured five-point calibration and settings remained unchanged; no residual processes.
- The user has also independently achieved **~100 seconds of encrypted A2854 HID operation and actual Windows mouse movement** at `64b927c` (Release **380/380**, no warnings/errors), showing real-world control works. During that **earlier** trial, Stop disabled both CCCDs, but actual disconnect reason was **0x08 supervision timeout**. At `abd69aa` a separate X-close produced actual 0x16. These are **different runs**, not a guarantee that every Stop produces 0x16.
- **Current user goal:** preserve the now-confirmed usable A2854 cursor/click/drag behavior. The user independently QA-tested `85f20a7` with a normal Stop 0x16. Next: independent Windows validation of the narrow diagonal/pressure follow-up, Settings scrolling and visual calibration cold-start fix. Do not broaden BLE, pointer injection or UI scope without evidence.
- **Roles:** ChatGPT/main author **modifies source and GitHub itself**, commits/pushes and maintains docs. The user's local Windows agent independently performs **read-only** build/test, offline WPF, and specifically user-authorized real-device QA. Do not hand code implementation to the Windows validator or ask it to commit, unless the user explicitly reassigns roles.
- The user prefers **one consolidated QA message in a ChatGPT writing block** after main-author changes, rather than multiple prompts. The document should be sufficient to resume even without prior chat.

## 1. Functional evidence — separate proven vs unverified

| Evidence | Commit/setting | Verified result / limitation |
| --- | --- | --- |
| BLE/USB encryption and clean HCI stop | `dd06b2f` | One encrypted HID run ~70 s, early ATT 0x08 handled, CCCDs disabled, local 0x16. Historical QA; not a stability guarantee. |
| Encryption failure | `71733be` | One LE link failed during encrypt (`0x2019 / 0x3E`, ~0.17 s). Do **not** infer corrupted LTK/bond. |
| Long real input | `64b927c` | Release 380/380, existing DPAPI bond, ~100 s, buttons and **actual Windows mouse motion succeeded**. Stop both CCCDs disabled, **reason 0x08**. |
| WPF + reliable X-close | `abd69aa` | Release 381/381, Stop 28/28, Settings 0 DIP overflow at all 18 locale/model/width combinations. Actual ~27 s connection; X-close → CCCD two disables → Disconnect accepted → **actual 0x16**. |
| Pointer click smoothing | `85f20a7` | User-reported independent Release 387/387, pointer 26/26, real click/drag subjectively PASS, Stop actual 0x16; later diagonal/scroll/visual-calibration fix is new unverified source. |
| Explicit Stop button then Start | after `abd69aa` | **Not yet verified**. X-close and a prior Stop are separate paths; test both eventually, avoid claims of completion. |
| Repeatability / 30-second peer 0x13 | multiple | A long run and shorter successful runs exist, but intermittent peer `0x13` root cause/permanent resolution **not proved**. |
| Hardware pointer drag under new filter | `85f20a7` | **Not yet verified**; evaluate click stability **and** ability to click-and-drag. |

Local independent QA reports (located on the **user's Windows computer**, *not* committed to GitHub):

- `AppleRemoteWin-Validation/settings-stop-abd69aa/QA_REPORT_KO.md` — latest clean X-close and readability QA.
- `AppleRemoteWin-Validation/practical-64b927c/QA_REPORT_KO.md` — ~100-second real mouse, 0x08 Stop.
- `AppleRemoteWin-Validation/functional-563f776/QA_REPORT_KO.md` — earlier 377/377 and encryption dispatch race.
- `AppleRemoteWin-Validation/localization-9670d0a/QA_REPORT_KO.md` — earlier localization QA.
- Older report directories referenced by the [chronological handoff](HANDOFF_2026-10-23_A2854.md).

**Do not pretend you opened those local Windows paths through GitHub:** screenshots/reports there are user-agent-produced evidence, not repo contents. Request just the relevant measurements/results if a later fix requires them.

## 2. Physical device, safety and user settings

- A2854 is the **third-generation USB-C Apple Siri Remote**. Separate from first-generation and ESP32 paths. Uses the dedicated **external Barrot USB Bluetooth adapter 33FA:0001 under WinUSB** (historically through a USB 3 hub); keep the internal Intel AX200's native Windows Bluetooth driver intact.
- Existing **CurrentUser DPAPI** encrypted bond: `%LOCALAPPDATA%\AppleRemoteWin\A2854\bond.v1.dpapi`. It has already worked on real hardware. **Never open, display, upload, overwrite, delete or re-pair** without explicit new authorization. Never copy keys, addresses or raw private BLE data to public logs or docs.
- User's **measured five-point calibration**: `%APPDATA%\AppleRemote\A2854-touch-calibration.json`; separate from bond, backup stored alongside it. **Preserve actual values.** Display-only calibration and optional actual Windows pointer calibration are different toggles.
- A2854 link requires explicit **Start**; GUI launch must be **passive**. Stop before switching models. If connected, press an ordinary remote button to wake as needed. Do not silently add auto-reconnect or pairing. Never change Barrot/Intel drivers, OS certificates, kernel settings, RF intervals, firmware, or the DPAPI identity.
- **Windows keyboard and pointer injection are opt-in separately.** Real pointer is under Settings A2854 pointer toggle; calibrated pointer axes require the additional 5-point toggle. Physical `0xFB` click remains the only authoritative OS left-click input; TV/Siri/Power have no OS keyboard action. If mouse left-button release fails, do not hide the held state; preserve/retry cleanup.
- A2854 HID subscriptions: `0xFB` buttons, `0xFC` touch. Never subscribe to or log microphone `0xFA`.
- Do not force a final disconnect to show `0x16` when the controller actually reports `0x08`. HCI Disconnect **command status** and **Disconnection Complete reason** are distinct facts. CCCD teardown must precede Disconnect.

## 3. NEW: pressure-aware click stabilization (already committed at 85f20a7)

**User's observable bug:** while pressing the physical clickpad, the finger shifts slightly, causing OS cursor movement and imprecise targeting. User then suggested using pressure sensing.

**Reality of decoded signal:** `src/AppleRemote.Core/A2854Decoder.cs` parses a 7-byte contact slot in HID `0xFC`. Byte field `Pressure` is **empirically named**, **not a validated/calibrated physical force measurement**. Hover reportedly may have nonzero values (~1) and ordinary contacts ~9–14. Therefore pressure **MUST NOT directly synthesize a click**; hardware button transition in HID `0xFB` is authoritative.

**Current source implementation:** `src/AppleRemote.Core/A2854PointerMapper.cs` in HEAD `85f20a7`:

- When *real* 0xFB `click` mouse-down is successfully accepted, anchor at the last tracked touch coordinate. While held, suppress small movement within raw-distance **90 coordinate units**; any intentional longer movement becomes drag, without suddenly replaying the absorbed jitter.
- After successful physical click release, suppress two tail touch movement frames; no ghost cursor displacement.
- When a *single valid contact* shows empirical `Pressure` rise **≥24** from the previous frame **and movement on each axis is below 90**, suppress two pointer movement frames as a conservative *pre-click* guard. Large purposeful swipes are not delayed. No synthetic click from pressure. Pressure parsing rejects invalid reports/hover/multi-contact for Windows pointer use.
- The pointer's original sensitivity (default 0.18; validated range 0.02–3.0), calibrated-axes optional path, normal continuous drag, jump clamp and fractional movement behavior are preserved in the mapper. No new persisted setting or protocol changed.
- If OS mouse-down was rejected, **do not** assume click held or freeze motion. If mouse-up fails, **keep held state** and retry during Reset. Stop/input-fault release semantics remain in effect.
- Production routing: `src/AppleRemote.Windows/RemoteSession.cs` `FeedA2854` decodes the stream and sends touch/buttons into `_pointer.OnEvent` **only when the user enabled A2854 Pointer**; pointer click is not simultaneously mapped to an Enter keyboard key. Normal first-generation and keyboard paths remain independent.
- Tests: `tests/AppleRemote.Tests/A2854PointerMapperTests.cs` adds simulated pressure spikes, ordinary swipes, click wobble, drag threshold, post-release tail, failed OS down/up, reset release. Tests written in source are **not yet independent QA results**.

**Important caveats to validate:** before 0xFB click frames arrive, tiny cursor movement can precede the click. An empirical pressure rise may *not* reliably occur for each real finger or near threshold. Do not claim this completely eliminates pre-press drift without a real A2854 trace and user feel; do not use raw/personal touch traces in public GitHub. Tune constants **only if needed**, grounded in sanitized non-sensitive statistics. Confirm that legitimate click-and-drag, slow micro-adjustments, long swipe, and pointer move after release are still pleasant.

### Suggested next local independent QA (bounded, no code modifications)

1. Fetch latest `feat/a2854-support`; verify actual HEAD includes `85f20a7` (or later docs-only successor). Ensure worktree clean.
2. Release build **once** and run full tests **once**; do not hardcode expected count as 381, since this new commit adds tests. Separately run pointer-mapper and input-gate/Stop-focused cases. Report errors/warnings, pass counts, xUnit analyzer warnings.
3. Offline fake-sink assertions: 0xFB click still sends one mouse-down and up; ≤90 raw finger wobble does not move cursor while held; larger motion becomes drag without jump; 24+ pressure rise suppresses only short movement and never creates a click; hover/second finger ignore; failed mouse-up retains state; Reset frees mouse.
4. If the user permits a **single bounded** real test and the adapter/bond environment is healthy: Start → encrypted HID ready → enable existing Pointer opt-in → aim at a **harmless blank surface** and click 5–10 times → note click target stability; try **one intentional click-drag** then release and confirm mouse-up, normal swipe, small adjustments. No destructive interactions. Stop with on-screen Stop button, and if normal check allows, one subsequent Start test (these were missing at `abd69aa`); do not loop endlessly, reset bond, change driver or keymap.
5. Record actual mouse pointer stability, click targeting, drag start feel, any accidental frozen movement, **CCCD 2 disables**, HCI disconnect command accepted and *actual* reason (0x16 vs 0x08), whether next Start succeeds, no held OS input or residual processes. Ensure original settings, protected bond and calibration unchanged.
6. If hardware run fails early, report **phase/source/status** and stop. Do not conclude pressure guard is bad because encryption failed.
7. Provide a short PASS/PARTIAL/FAIL verdict for **real usability**. Main ChatGPT author then makes any code adjustment directly and hands back **one consolidated QA writing block**.

## 4. Build/test and file map

PowerShell at repo root (verify project requirements first):

```powershell
git fetch origin
git switch feat/a2854-support
git pull --ff-only
git status --short
git rev-parse HEAD
dotnet build AppleRemote.sln -c Release -p:AppleRemoteDriver=false -p:AppleRemoteDongle=false
dotnet test tests/AppleRemote.Tests/AppleRemote.Tests.csproj -c Release --no-build -p:AppleRemoteDriver=false -p:AppleRemoteDongle=false
```

Key locations:

- Main GUI: `src/AppleRemote.App/Views/Page/RemotePage.xaml`, `SettingsPage.xaml`, `SettingsPage.xaml.cs`; visual Siri Remote: `src/AppleRemote.App/Controls/A2854RemoteDrawing.xaml`.
- Main GUI state: `src/AppleRemote.App/ViewModels/Page/RemoteViewModel.cs`, `SettingsViewModel.cs`.
- Session + actual OS pointer sink: `src/AppleRemote.Windows/RemoteSession.cs` and `src/AppleRemote.Windows/WindowsPointerSink.cs` (check actual repository tree; path can change).
- Empirical HID: `src/AppleRemote.Core/A2854Decoder.cs`; UI/input dispatch: `A2854InputGate.cs`; pointer filter: `A2854PointerMapper.cs` + `tests/AppleRemote.Tests/A2854PointerMapperTests.cs`.
- BLE link and HCI: `src/AppleRemote.Windows/A2854RemoteBle.cs`, `src/AppleRemote.Core/Ble.cs`, `Hci.cs`, `A2854HidCapture.cs`.
- User's five-point contact geometry: `A2854TouchCalibration.cs`, `A2854TouchCalibrationStore.cs`, `A2854TouchProjection.cs`. Do not substitute synthetic values for the user's existing profile.
- Fixed-dark WPF Settings readability: `src/AppleRemote.App/Views/Page/SettingsPage.xaml`; text resources: `src/AppleRemote.Core/Strings.ko.resx`, `Strings.en.resx`, `Strings.resx`.
- Korean Blockly local messages: `src/AppleRemote.App/Blockly/msg/ko.js` and attribution notice.
- Complete dated evidence: `docs/HANDOFF_2026-10-23_A2854.md` and `docs/a2854/APP_INTEGRATION_2026-10-23.md`.

## 5. Scope boundaries / open items

**Highest priority:** verify new physical-click stability and whether direct Stop→Start works in real use. Treat any newly discovered OS input stuck-down bug as highest severity.

**Important but separate:** Don't conflate the successful ~27-second X-close 0x16 run with guaranteed 0x16 for all Stop paths; 0x08 can be an authentic controller timeout. Reconnection robustness across sessions is not statistically established by one success.

**Lower priority:** more aesthetics/translation/doc cleanup, 1-second trail fade polish, finger identity when contacts cross, optional settings controls for click guard. Avoid adding complex tuning UI until physical behavior requires it. The Settings text contrast/spacing, 3-language translations and six layout model/width permutations were already verified at earlier commits.

**Do not claim:** physical pressure is a certified force measurement; new pointer smoothing was field-proven; every real session can be cleanly stopped; bonding needs to be redone; the user's local QA artifacts live in GitHub.

## 6. Recommended next response after the handoff

Read this doc + AGENTS, fetch actual HEAD, ask **only for the new read-only QA outcome** if user already ran it. Otherwise, if a QA handoff is needed, supply one short actionable Korean writing block targeting real click stabilization and direct Stop→Start, not weeks of unrelated work. Let the user **use the remote now**; do not regress encryption, GATT, the protected bond or driver to chase UI polish.

---
*Documentation snapshot prepared for continuity; not a claim of new compilation, hardware access or tests by the author.*
