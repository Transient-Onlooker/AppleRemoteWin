"""Regression tests for the byte-alignment and Usage hypotheses."""
import unittest
from test_descriptor_variants import synthetic_descriptor
from byte_alignment_matrix import alignment_findings, make_alignment_matrix

class AlignmentTests(unittest.TestCase):
    def test_original_has_exact_two_independent_hidclass_risks(self):
        result = alignment_findings(synthetic_descriptor())
        self.assertEqual(result["unaligned"], {("Feature", 0xB8): 4})
        self.assertEqual(result["data_variable_input_missing_usage_fields"], 1)
        self.assertEqual(result["report_bits"][("Input", 0x04)], 136)

    def test_padding_changes_only_feature_bits_not_byte_payload_length(self):
        variants = make_alignment_matrix(synthetic_descriptor())
        self.assertEqual(len(variants), 8)
        padded = alignment_findings(variants["padding_only"])
        self.assertEqual(padded["unaligned"], {})
        self.assertEqual(padded["report_bits"][("Feature", 0xB8)], 304)
        self.assertEqual(padded["report_bits"][("Input", 0x04)], 136)
        self.assertEqual((300 + 7) // 8, (304 + 7) // 8)
        self.assertEqual(padded["data_variable_input_missing_usage_fields"], 1)

    def test_usage_fix_does_not_cure_alignment(self):
        variants = make_alignment_matrix(synthetic_descriptor())
        usage = alignment_findings(variants["usage_range_only"])
        self.assertEqual(usage["unaligned"], {("Feature", 0xB8): 4})
        self.assertEqual(usage["data_variable_input_missing_usage_fields"], 0)

    def test_two_independent_fixes_clear_both_flags(self):
        variants = make_alignment_matrix(synthetic_descriptor())
        combined = alignment_findings(variants["usage_plus_padding"])
        self.assertEqual(combined["unaligned"], {})
        self.assertEqual(combined["data_variable_input_missing_usage_fields"], 0)
        self.assertEqual(combined["structural_errors"], [])

    def test_full_matrix_preserves_exact_input_geometry(self):
        for name, binary in make_alignment_matrix(synthetic_descriptor()).items():
            with self.subTest(name=name):
                state = alignment_findings(binary)
                self.assertEqual(state["report_bits"][("Input", 0x04)], 136)
                self.assertIn(state["report_bits"][("Feature", 0xB8)], (300, 304))
                self.assertEqual(state["structural_errors"], [])

    def test_refuses_unknown_descriptor(self):
        with self.assertRaises(ValueError):
            make_alignment_matrix(bytes.fromhex("05 01 09 02 A1 01 C0"))

if __name__ == "__main__":
    unittest.main()
