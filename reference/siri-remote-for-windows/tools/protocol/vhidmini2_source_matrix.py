#!/usr/bin/env python3
"""Prepare FOUR isolated A2854 HID-parser test driver source variants.

Uses the owner's PRIVATE 38-byte .bin plus Microsoft's separately
downloaded vhidmini2/driver/vhidmini.c. Never uploads or modifies the input,
the upstream sample, a real device, PnP, driver settings, or system registry.

Outputs four C-file substitutions for the *official UMDF2 vhidmini2
sample*, and hashes/geometry for repeatable Windows VM test runs.
This is SOURCE PREPARATION ONLY, not a Windows HID acceptance test.

Covers: original, Usage range only, 4-bit constant Feature padding only,
and both. Separate independent parser/transport experiments later.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import sys

from byte_alignment_matrix import alignment_findings, make_alignment_matrix

KNOWN_ORIGINAL_SHA256 = "6f9795ae87c7bcfcba0d50c38cbde6abf5163f78e1299cc23dc110795bc57f49"
OFFICIAL_UPSTREAM_REF = "2dc3fd3a0cc84a2933f2194e7ec0871584979071"
SOURCE_RE = re.compile(
    r"(HID_REPORT_DESCRIPTOR\s+G_DefaultReportDescriptor\s*\[\]\s*=\s*\{)"
    r"(.*?)"
    r"(\n\};)",
    re.DOTALL,
)
REGISTRY_PROBE_START = "    status = CheckRegistryForDescriptor(device);"
REGISTRY_PROBE_END = "\n    return status;"


def _sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def patch_sample_source(official_source: str, descriptor: bytes) -> str:
    """Fail closed on any unfamiliar or already-patched upstream driver."""
    matches = list(SOURCE_RE.finditer(official_source))
    if len(matches) != 1:
        raise ValueError("Expected exactly one official G_DefaultReportDescriptor array")
    if official_source.count(REGISTRY_PROBE_START) != 1:
        raise ValueError("Official sample registry-probe structure changed")
    start = official_source.index(REGISTRY_PROBE_START)
    end = official_source.find(REGISTRY_PROBE_END, start)
    if end < 0 or end - start > 1800:
        raise ValueError("Cannot bound official sample's descriptor fallback block")
    block = official_source[start:end]
    for token in ("ReadDescriptorFromRegistry(device)", "G_DefaultReportDescriptor", "STATUS_SUCCESS"):
        if token not in block:
            raise ValueError("Unexpected sample fallback block: " + token)
    if not (0 < len(descriptor) < 1024):
        raise ValueError("Descriptor size outside bounded HID experiment")
    line_items = [
        ", ".join(f"0x{b:02X}" for b in descriptor[i:i + 12])
        for i in range(0, len(descriptor), 12)
    ]
    array_body = (
        "\n    // PRIVATE A2854 VM LAB descriptor; never use in production\n    "
        + ",\n    ".join(line_items) + "\n"
    )
    s = SOURCE_RE.sub(lambda m: m.group(1) + array_body + m.group(3), official_source, count=1)
    start = s.index(REGISTRY_PROBE_START)
    end = s.find(REGISTRY_PROBE_END, start)
    strict_block = (
        "    // A2854 isolated VM experiment: ALWAYS use compiled default array.\n"
        "    // Intentionally disable sample's silent ReadFromRegistry fallback.\n"
        "    deviceContext->ReportDescriptor = G_DefaultReportDescriptor;\n"
        "    deviceContext->HidDescriptor.DescriptorList[0].wReportLength =\n"
        "        (USHORT)sizeof(G_DefaultReportDescriptor);\n"
        "    status = STATUS_SUCCESS;\n"
    )
    s = s[:start] + strict_block + s[end:]
    if s.count("A2854 isolated VM experiment") != 1:
        raise ValueError("Failed to apply strict no-fallback source patch")
    return s


def build_source_matrix(raw: bytes, official_source: str) -> dict[str, tuple[bytes, str]]:
    if _sha256(raw) != KNOWN_ORIGINAL_SHA256:
        raise ValueError("Private original input does not match confirmed Windows ETL SHA-256")
    controls = make_alignment_matrix(raw)
    selected = {
        "A_original": controls["original"],
        "B_usage_range": controls["usage_range_only"],
        "C_padding": controls["padding_only"],
        "D_usage_padding": controls["usage_plus_padding"],
    }
    return {
        name: (desc, patch_sample_source(official_source, desc))
        for name, desc in selected.items()
    }


def write_matrix(raw: bytes, source: str, out_dir: Path) -> None:
    variants = build_source_matrix(raw, source)
    out_dir.mkdir(parents=True, exist_ok=False)
    manifest: dict = {
        "experiment": "A2854 isolated Windows HID parser acceptance (not transport)",
        "source_repository": "microsoft/Windows-driver-samples",
        "pinned_source_commit": OFFICIAL_UPSTREAM_REF,
        "official_filename": "hid/vhidmini2/driver/vhidmini.c",
        "upstream_source_sha256": _sha256(source.encode("utf-8")),
        "original_sha256": KNOWN_ORIGINAL_SHA256,
        "note": "No physical Siri Remote interaction; private C files are intended for VM only",
        "variants": {},
    }
    for name, (data, patched) in variants.items():
        folder = out_dir / name
        folder.mkdir()
        (folder / "vhidmini.c").write_text(patched, encoding="utf-8", newline="\n")
        findings = alignment_findings(data)
        manifest["variants"][name] = {
            "descriptor_bytes": len(data),
            "descriptor_sha256": _sha256(data),
            "patched_source_sha256": _sha256(patched.encode("utf-8")),
            "feature_payload_bits": findings["report_bits"][("Feature", 0xB8)],
            "input_payload_bits": findings["report_bits"][("Input", 0x04)],
            "unaligned_report_count": len(findings["unaligned"]),
            "missing_input_usage_items": findings["data_variable_input_missing_usage_fields"],
        }
        print(
            f"{name}: descriptor={len(data)} bytes; "
            f"feature={findings['report_bits'][('Feature', 0xB8)]} bits; "
            f"input=136 bits; "
            f"unaligned={len(findings['unaligned'])}; "
            f"missing_usage={findings['data_variable_input_missing_usage_fields']}"
        )
    (out_dir / "manifest.json").write_text(
        json.dumps(manifest, indent=2, ensure_ascii=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    print("Four C variants and checksummed manifest saved to a NEW LOCAL directory.")
    print("DO NOT build/install any variant on the primary Windows host.")
    print("The official UMDF2 sample must first build/start with its unmodified descriptor inside a VM.")
    print("Only then test variants one at a time, with a VM snapshot between tests.")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--input", type=Path, required=True, help="Private original 38-byte ETL descriptor")
    ap.add_argument("--sample-c", type=Path, required=True, help="Pinned official vhidmini.c from Microsoft")
    ap.add_argument("--out-dir", type=Path, required=True, help="NEW output directory, never overwrites")
    args = ap.parse_args()
    try:
        write_matrix(args.input.read_bytes(), args.sample_c.read_text(encoding="utf-8"), args.out_dir)
        return 0
    except (ValueError, OSError):
        print("FAILED CLOSED: input/original hash/upstream sample/output path is not suitable.", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
