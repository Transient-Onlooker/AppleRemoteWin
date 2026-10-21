#!/usr/bin/env python3
"""Audit HID report byte alignment and Usage coverage; generate local-only controls.

The captured remote descriptor is supplied via --input and stays on the owner PC.
No BLE access, PnP changes, driver overrides, or firmware writes.
The audit is a *static hypothesis test*, not the Windows HIDCLASS parser.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import sys
from descriptor_variants import inspect_semantics, make_variants, emit_files

# Add one 4-bit CONSTANT Feature field immediately after the existing
# Feature main item. This preserves the on-wire rounded payload size:
# 300 bits => ceil(300/8)=38 bytes; 304 bits => 38 bytes.
FEATURE_TO_INPUT = bytes.fromhex("B2 01 00 85 04")
PADDING_ITEM = bytes.fromhex("75 04 95 01 B1 01")


def alignment_findings(raw: bytes) -> dict:
    parsed = inspect_semantics(raw)
    if parsed["errors"]:
        raise ValueError("structurally invalid descriptor")
    misaligned = {
        (kind, report_id): (bits % 8)
        for (kind, report_id), bits in parsed["report_bits"].items()
        if (bits % 8) != 0
    }
    input_no_usage = sum(
        1 for field in parsed["fields"]
        if field.kind == "Input" and (field.flags & 1) == 0
        and (field.flags & 2) != 0 and field.local_usage_count == 0
    )
    return {
        "unaligned": misaligned,
        "data_variable_input_missing_usage_fields": input_no_usage,
        "report_bits": parsed["report_bits"],
        "structural_errors": parsed["errors"],
    }


def padded(raw: bytes) -> bytes:
    if raw.count(FEATURE_TO_INPUT) != 1:
        raise ValueError("expected one Feature-to-Input boundary")
    return raw.replace(
        FEATURE_TO_INPUT,
        bytes.fromhex("B2 01 00") + PADDING_ITEM + bytes.fromhex("85 04"),
        1,
    )


def make_alignment_matrix(raw: bytes) -> dict[str, bytes]:
    existing = make_variants(raw)  # enforces 38-byte report geometry
    # Four controlled variables: missing Usage, byte misalignment,
    # wide (>32-bit) Feature, and the Buffered Bytes Input bit.
    controls = {
        "original": existing["original"],
        "usage_range_only": existing["input_usage_range"],
        "padding_only": padded(raw),
        "usage_plus_padding": padded(existing["input_usage_range"]),
        "width_plus_padding": padded(existing["feature_25x12"]),
        "usage_width_padding": padded(existing["range_and_25x12"]),
        "usage_padding_no_buffered": padded(existing["range_and_unbuffered"]),
        "all_four_changes": padded(existing["range_25x12_unbuffered"]),
    }
    baseline = alignment_findings(raw)
    if baseline["report_bits"] != {("Feature", 0xB8): 300, ("Input", 0x04): 136}:
        raise ValueError("unexpected baseline report geometry")
    for name, variant in controls.items():
        state = alignment_findings(variant)
        if state["structural_errors"]:
            raise ValueError(f"structural error in {name}")
        if state["report_bits"].get(("Input", 0x04)) != 136:
            raise ValueError(f"unexpected input report change in {name}")
        expected_feature_bits = 300 if name in ("original", "usage_range_only") else 304
        if state["report_bits"].get(("Feature", 0xB8)) != expected_feature_bits:
            raise ValueError(f"unexpected Feature report change in {name}")
    return controls


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--out-dir", type=Path, default=None,
                        help="New local-only directory; fails if it already exists")
    args = parser.parse_args()
    try:
        matrix = make_alignment_matrix(args.input.read_bytes())
        for name, binary in matrix.items():
            findings = alignment_findings(binary)
            sizes = ", ".join(
                f"{typ}:0x{rid:02X}={bits}b"
                for (typ, rid), bits in sorted(findings["report_bits"].items())
            )
            print(
                f"{name}: descriptor_bytes={len(binary)}; reports=[{sizes}]; "
                f"unaligned={len(findings['unaligned'])}; "
                f"missing_input_usage={findings['data_variable_input_missing_usage_fields']}"
            )
        if args.out_dir is not None:
            emit_files(matrix, args.out_dir)
        print("Static checks only; DOES NOT prove HIDCLASS Windows acceptance.")
        print("No descriptor bytes, MACs, instance paths or event payloads printed.")
        return 0
    except (OSError, ValueError):
        print("Failed closed: verify original local 38-byte descriptor and unused output path.", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
