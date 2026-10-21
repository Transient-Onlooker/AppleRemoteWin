#!/usr/bin/env python3
"""Generate controlled, OFFLINE HID descriptor variants from a PRIVATE local .bin.

The A2854's captured raw descriptor is intentionally NOT stored in the repo.
Nothing touches Bluetooth, Windows PnP, drivers, device firmware, or the registry.
Tests compare descriptor *semantics*; this is NOT the Windows HIDCLASS parser
and cannot establish which variant Windows would accept.

Example:
  py -3 descriptor_variants.py --input "%TEMP%\a2854_hidclass_report_map.bin" ^
      --out-dir "%TEMP%\a2854_descriptor_variants"
"""

from __future__ import annotations

import argparse
from collections import defaultdict
from dataclasses import dataclass
import hashlib
from pathlib import Path
import sys


@dataclass(frozen=True)
class ReportField:
    kind: str
    report_id: int
    width: int
    count: int
    flags: int
    local_usage_count: int

    @property
    def bits(self) -> int:
        return self.width * self.count


def inspect_semantics(raw: bytes) -> dict:
    """Parse standard short HID items and basic global/local state.

    Main items reset local usage state, including Collection and Feature.
    Global Push/Pop is tracked. This does not emulate the Windows HID parser.
    """
    position = 0
    globals_ = {"size": 0, "count": 0, "id": 0}
    stack: list[dict] = []
    local_usage_count = 0
    depth = 0
    max_depth = 0
    fields: list[ReportField] = []
    errors: list[str] = []

    while position < len(raw):
        start = position
        prefix = raw[position]
        position += 1
        if prefix == 0xFE:
            if position + 2 > len(raw):
                errors.append("truncated_long_item_header")
                break
            size = raw[position]
            position += 2
            if position + size > len(raw):
                errors.append("truncated_long_item_payload")
                break
            position += size
            continue
        size = (0, 1, 2, 4)[prefix & 3]
        kind = (prefix >> 2) & 3
        tag = prefix >> 4
        if position + size > len(raw):
            errors.append(f"truncated_item_at_{start}")
            break
        value = int.from_bytes(raw[position:position + size], "little")
        position += size

        if kind == 1:  # Global
            if tag == 7:
                globals_["size"] = value
            elif tag == 8:
                if value == 0:
                    errors.append("report_id_zero")
                globals_["id"] = value
            elif tag == 9:
                globals_["count"] = value
            elif tag == 10:  # Push
                stack.append(globals_.copy())
            elif tag == 11:  # Pop
                if stack:
                    globals_ = stack.pop()
                else:
                    errors.append("global_stack_underflow")
        elif kind == 2:  # Local
            if tag in (0, 1, 2):  # Usage, Usage Min, Usage Max
                local_usage_count += 1
        elif kind == 0:  # Main
            if tag in (8, 9, 11):
                if depth <= 0:
                    errors.append("report_outside_collection")
                field = ReportField(
                    kind={8: "Input", 9: "Output", 11: "Feature"}[tag],
                    report_id=globals_["id"],
                    width=globals_["size"],
                    count=globals_["count"],
                    flags=value,
                    local_usage_count=local_usage_count,
                )
                if field.width <= 0 or field.count <= 0:
                    errors.append("missing_report_size_or_count")
                fields.append(field)
            elif tag == 10:  # Collection
                depth += 1
                max_depth = max(depth, max_depth)
            elif tag == 12:  # End Collection
                depth -= 1
                if depth < 0:
                    errors.append("collection_underflow")
                    depth = 0
            local_usage_count = 0  # HID local items reset AFTER each Main item

    if depth:
        errors.append("unclosed_collection")
    if stack:
        errors.append("global_stack_not_empty")
    if not fields:
        errors.append("no_reports")
    issues: list[str] = []
    for f in fields:
        if f.kind == "Input" and not (f.flags & 1) and (f.flags & 2) and f.local_usage_count == 0:
            issues.append("data_variable_input_without_local_usage")
        if f.width > 32:
            issues.append("field_width_over_32_bits")
        if f.kind == "Input" and (f.flags & 0x100):
            issues.append("buffered_bytes_input")
    totals: dict[tuple[str, int], int] = defaultdict(int)
    for f in fields:
        totals[(f.kind, f.report_id)] += f.bits
    return {
        "errors": errors,
        "issues": sorted(set(issues)),
        "fields": fields,
        "report_bits": dict(totals),
        "max_depth": max_depth,
    }


WIDE = bytes.fromhex("75 32 96 06 00")  # 50 bits * 6 = 300 bits
NARROW = bytes.fromhex("75 19 96 0C 00")  # 25 bits * 12 = 300 bits
INPUT = bytes.fromhex("82 22 01")
INPUT_WITH_USAGE = bytes.fromhex("09 0B 82 22 01")
# Input has 17 one-byte fields. One Usage may be insufficient for an
# independent parser to retain all 17 variable fields; include a Usage range.
INPUT_WITH_USAGE_RANGE = bytes.fromhex("19 01 29 11 82 22 01")
UNBUFFERED_INPUT = bytes.fromhex("82 22 00")


def _once(raw: bytes, marker: bytes, description: str) -> None:
    if raw.count(marker) != 1:
        raise ValueError(f"Expected one {description} marker; refusing unverified mutation")


def make_variants(raw: bytes) -> dict[str, bytes]:
    """Only mutate a descriptor whose semantics match the captured A2854.

    Fail closed rather than editing unrelated HID report descriptors.
    """
    parsed = inspect_semantics(raw)
    if parsed["errors"]:
        raise ValueError("Original HID item structure is invalid")
    if len(raw) != 38 or parsed["max_depth"] != 1:
        raise ValueError("Expected the confirmed 38-byte, depth-one descriptor")
    if parsed["report_bits"] != {("Feature", 0xB8): 300, ("Input", 0x04): 136}:
        raise ValueError("Unexpected report IDs/widths; refusing mutation")
    _once(raw, WIDE, "50-bit Feature size/count")
    _once(raw, INPUT, "buffered Input main item")

    usage_one = raw.replace(INPUT, INPUT_WITH_USAGE, 1)
    usage_range = raw.replace(INPUT, INPUT_WITH_USAGE_RANGE, 1)
    narrow = raw.replace(WIDE, NARROW, 1)
    narrow_range = narrow.replace(INPUT, INPUT_WITH_USAGE_RANGE, 1)
    no_buffer = raw.replace(INPUT, UNBUFFERED_INPUT, 1)
    no_buffer_range = raw.replace(
        INPUT, bytes.fromhex("19 01 29 11") + UNBUFFERED_INPUT, 1
    )
    all_three = narrow.replace(
        INPUT, bytes.fromhex("19 01 29 11") + UNBUFFERED_INPUT, 1
    )
    return {
        "original": raw,
        "single_input_usage": usage_one,
        "input_usage_range": usage_range,
        "feature_25x12": narrow,
        "range_and_25x12": narrow_range,
        "unbuffered_input": no_buffer,
        "range_and_unbuffered": no_buffer_range,
        "range_25x12_unbuffered": all_three,
    }


def describe(name: str, raw: bytes) -> str:
    result = inspect_semantics(raw)
    report_sizes = ", ".join(
        f"{kind}:0x{rid:02X}={bits}b"
        for (kind, rid), bits in sorted(result["report_bits"].items())
    )
    return (
        f"{name}: bytes={len(raw)}, reports=[{report_sizes}], "
        f"structural_errors={','.join(result['errors']) or 'none'}, "
        f"semantic_flags={','.join(result['issues']) or 'none'}"
    )


def emit_files(variants: dict[str, bytes], directory: Path) -> None:
    # Refuse an existing directory so stale binaries cannot be mixed into
    # this run. The descriptor is stored privately on this user's computer.
    directory.mkdir(parents=True, exist_ok=False)
    for name, raw in variants.items():
        dest = directory / f"{name}.bin"
        with dest.open("xb") as f:
            f.write(raw)
    print(f"Created {len(variants)} local binary variants in the requested directory.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True,
                        help="Private 38-byte local descriptor .bin")
    parser.add_argument("--out-dir", type=Path, default=None,
                        help="Create a NEW local output directory for eight .bin variants")
    args = parser.parse_args()
    try:
        raw = args.input.read_bytes()
        variants = make_variants(raw)
        baseline = inspect_semantics(raw)
        for name, desc in variants.items():
            actual = inspect_semantics(desc)
            if actual["errors"]:
                raise ValueError(f"Generated {name} has structural errors")
            if actual["report_bits"] != baseline["report_bits"]:
                raise ValueError(f"Generated {name} changes report bit lengths")
            print(describe(name, desc))
        if args.out_dir is not None:
            emit_files(variants, args.out_dir)
        print("IMPORTANT: static semantic diagnostics; NOT a Windows HID parser pass/fail.")
        print("No raw bytes, device IDs, Bluetooth addresses, or file contents printed.")
        return 0
    except (OSError, ValueError) as exc:
        # Errors only originate in our known code paths, but avoid leaking a
        # sensitive full path via exception formatting.
        print(f"Could not analyze: {type(exc).__name__}. "
              "Check that the input is the original A2854 38-byte binary "
              "and the output directory does not exist.", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
