#!/usr/bin/env python3
"""Run an independent PURE-PYTHON descriptor parser on local HID variants.

Requires: py -3 -m pip install hid-parser==0.1.0
This is NOT the Windows HIDCLASS parser and cannot predict Code 10.

The raw A2854 descriptor never leaves the local computer; only validation
outcome and exception class names are printed. No BLE/driver actions.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import sys
import warnings

from descriptor_variants import make_variants


def check(raw: bytes) -> tuple[str, str]:
    import hid_parser
    try:
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            descriptor = hid_parser.ReportDescriptor(list(raw))
            # Try an input capability query to exercise more than item framing.
            try:
                input_size = descriptor.get_input_report_size(0x04)
                query = f"input_size={input_size}"
            except Exception as exc:
                query = f"input_query={type(exc).__name__}"
        return "constructed", query
    except Exception as exc:
        return f"rejected:{type(exc).__name__}", "input_query=skipped"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True,
                        help="Private 38-byte A2854 descriptor .bin; never uploaded")
    args = parser.parse_args()
    try:
        import hid_parser  # noqa: F401
    except ImportError:
        print("Install dependency: py -3 -m pip install hid-parser==0.1.0",
              file=sys.stderr)
        return 2
    try:
        variants = make_variants(args.input.read_bytes())
    except (OSError, ValueError):
        print("Input missing or failed descriptor identity/geometry checks",
              file=sys.stderr)
        return 2
    for name, raw in variants.items():
        result, query = check(raw)
        print(f"{name}: independent_hid_parser={result}; {query}")
    print("Independent Python parser only. NOT Windows HIDCLASS acceptance.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
