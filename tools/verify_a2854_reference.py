# SPDX-License-Identifier: AGPL-3.0-or-later
"""Check the exact archived Linux Rust source snapshot against its Git blob manifest.

No network or external packages needed; safe on Windows, Linux and macOS.
"""
from __future__ import annotations

import hashlib
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "docs" / "a2854" / "SNAPSHOT_MANIFEST.tsv"
REFERENCE = ROOT / "reference" / "siri-remote-for-windows"


def git_blob_sha(data: bytes) -> str:
    return hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()


def validate() -> int:
    expected: dict[str, tuple[str, int]] = {}
    for line in MANIFEST.read_text(encoding="utf-8").splitlines():
        if not line or line.startswith("#"):
            continue
        rel, sha, mode, size = line.split("\t")
        if rel in expected or mode != "100644":
            raise ValueError(f"Unexpected duplicate/path mode: {rel}")
        expected[rel] = (sha, int(size))

    actual = {p.relative_to(REFERENCE).as_posix(): p for p in REFERENCE.rglob("*") if p.is_file()}
    errors = []
    for rel in sorted(expected.keys() - actual.keys()):
        errors.append(f"MISSING: {rel}")
    for rel in sorted(actual.keys() - expected.keys()):
        errors.append(f"UNTRACKED: {rel}")
    for rel in sorted(actual.keys() & expected.keys()):
        data = actual[rel].read_bytes()
        want_sha, want_size = expected[rel]
        if len(data) != want_size or git_blob_sha(data) != want_sha:
            errors.append(f"DIFFERENT: {rel}")
    if errors:
        for item in errors:
            print(item, file=sys.stderr)
        print(f"Reference integrity FAILED: {len(errors)} discrepancies", file=sys.stderr)
        return 1
    print(f"Reference integrity OK: {len(expected)} files, {sum(n for _, n in expected.values())} bytes")
    return 0


if __name__ == "__main__":
    raise SystemExit(validate())
