"""Synthetic-only tests of Microsoft UMDF2 source patching logic."""
from __future__ import annotations
import re
import unittest

from test_descriptor_variants import synthetic_descriptor
from vhidmini2_source_matrix import patch_sample_source, SOURCE_RE, REGISTRY_PROBE_START


OFFICIAL_SHAPED_FIXTURE = """// sample-shaped stub, NOT actual Microsoft source
typedef unsigned char HID_REPORT_DESCRIPTOR;
HID_REPORT_DESCRIPTOR G_DefaultReportDescriptor[] = {
    0x05,0x01,0x09,0x06,0xA1,0x01,0xC0,
};
int deviceInitialize() {
    status = CheckRegistryForDescriptor(device);
    if (NT_SUCCESS(status)){
        status = ReadDescriptorFromRegistry(device);
    }
    if (!NT_SUCCESS(status)){
        deviceContext->ReportDescriptor = G_DefaultReportDescriptor;
        status = STATUS_SUCCESS;
    }
    return status;
}
"""


class SourcePatchingTests(unittest.TestCase):
    def test_patch_makes_array_exact_and_removes_fallback(self):
        desc = synthetic_descriptor()
        self.assertEqual(len(desc), 38)
        result = patch_sample_source(OFFICIAL_SHAPED_FIXTURE, desc)
        found = SOURCE_RE.search(result)
        self.assertIsNotNone(found)
        decoded = bytes(int(s, 16) for s in re.findall(r"0x([0-9a-fA-F]{2})", found.group(2)))
        self.assertEqual(decoded, desc)
        self.assertNotIn(REGISTRY_PROBE_START, result)
        self.assertIn("A2854 isolated VM experiment", result)
        self.assertIn("sizeof(G_DefaultReportDescriptor)", result)

    def test_reject_already_patched_or_mismatched_source(self):
        original = synthetic_descriptor()
        patched = patch_sample_source(OFFICIAL_SHAPED_FIXTURE, original)
        with self.assertRaises(ValueError):
            patch_sample_source(patched, original)
        with self.assertRaises(ValueError):
            patch_sample_source(OFFICIAL_SHAPED_FIXTURE.replace(REGISTRY_PROBE_START, "status = 0;"), original)
        with self.assertRaises(ValueError):
            patch_sample_source(OFFICIAL_SHAPED_FIXTURE.replace("G_DefaultReportDescriptor[]", "SomethingElse[]"), original)

    def test_variants_generate_different_bytes_while_preserving_report_ids(self):
        from byte_alignment_matrix import make_alignment_matrix, alignment_findings
        variants = make_alignment_matrix(synthetic_descriptor())
        for name in ("original", "usage_range_only", "padding_only", "usage_plus_padding"):
            state = alignment_findings(variants[name])
            self.assertEqual(state["report_bits"][("Input", 0x04)], 136)
            self.assertIn(state["report_bits"][("Feature", 0xB8)], (300, 304))
            src = patch_sample_source(OFFICIAL_SHAPED_FIXTURE, variants[name])
            self.assertEqual(src.count("A2854 isolated VM experiment"), 1)

if __name__ == "__main__":
    unittest.main()
