# Repository consolidation record (2026-10-21)

## What was moved

**Destination:** `Transient-Onlooker/AppleRemoteWin`, branch `feat/a2854-support` (first prepared on `chore/a2854-consolidation`). The Windows WPF, .NET 10, original Gen-1 code and experimental A2854 code remain at their original paths.

**Source:** [`Transient-Onlooker/siri-remote-for-windows`](https://github.com/Transient-Onlooker/siri-remote-for-windows) at immutable **main commit [`0bb5bd8f5537fa24e760c4f5c9940ab55d4a5e8a`](https://github.com/Transient-Onlooker/siri-remote-for-windows/commit/0bb5bd8f5537fa24e760c4f5c9940ab55d4a5e8a)**.

**Snapshot path:** [`../../reference/siri-remote-for-windows`](../../reference/siri-remote-for-windows). All paths relative to the original source root, including the Rust crate, `Cargo.lock`, `flake.nix`, scripts, `docs/windows-port`, historical CI and LICENSE, are retained under this prefix.

**Integrity check before promotion:** the source had **100 tracked blob files, 842,467 bytes**. After import, all **100 destination blob SHA-1 hashes** and total file sizes matched the source snapshot; no missing/extra files under the snapshot prefix. Git blob SHA equality verifies copied file bytes exactly, including Rust sources, license, tests and docs.

### Reproducible integrity check

All individual relative paths, Git blob SHA-1 values, file modes, and byte lengths are listed in [`SNAPSHOT_MANIFEST.tsv`](SNAPSHOT_MANIFEST.tsv). From the destination repository root, run:

```bash
python tools/verify_a2854_reference.py
```

The verification hashes the current **Git-tracked** reference files byte-for-byte (excluding untracked build output) and ensures no tracked path is missing or extra. The check is also wired into the `python-probe` GitHub Actions job. GitHub source-vs-destination tree SHA comparison was performed at import time; a future CI run is **not** assumed to have succeeded without a reported result.

## Why a snapshot instead of a source-branch merge

- The two repositories have unrelated build systems and Git histories. The current GitHub integration supported copying their file content and constructing a commit/tree, rather than executing an unrelated-history `git subtree` merge.
- **Original commit ancestry is not embedded in AppleRemoteWin's Git history.** The source repository is therefore retained as a permanent, read-only historical source with its original commits and attribution; do not delete it.
- Historical source `.github/workflows/` is nested under `reference/` and is deliberately not part of the destination's active workflows.
- Treat the source as a preserved *reference implementation*: do not import BlueZ/PipeWire Rust dependencies into the Windows C# solution without a reason.

## Licenses and attribution

- Windows project: **AGPL-3.0-or-later** with applicable third-party MIT notices.
- Preserved Rust project: **GPL-3.0**; its unmodified [`LICENSE`](../../reference/siri-remote-for-windows/LICENSE) and source headers accompany the full snapshot.
- Future selective code reuse needs to preserve notices and satisfy the applicable license obligations. Repository colocation by itself is **not** evidence of an integrated, dual-platform executable.

## Canonical documentation

- [A2854 index](README.md) — entry point.
- [Architecture](ARCHITECTURE.md), [protocol evidence](PROTOCOL_AND_EVIDENCE.md), [hardware validation](HARDWARE_VALIDATION.md) — maintained current docs.
- [`docs/HANDOFF_2026-10-21_A2854.md`](../HANDOFF_2026-10-21_A2854.md) and [`docs/A2854_PORT_STATUS.md`](../A2854_PORT_STATUS.md) — project progress and engineering chronology.
- [`reference/siri-remote-for-windows/docs/windows-port`](../../reference/siri-remote-for-windows/docs/windows-port) — untouched historical research, not the current plan.

After promoting the verified snapshot, update the source README with a prominent forward link and stop independent feature development there. Archiving is a separate GitHub repository setting; do not claim it is archived until that state is verified.
