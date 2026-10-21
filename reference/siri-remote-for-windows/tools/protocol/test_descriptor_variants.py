"""Pure offline, synthetic HID tests; never commit the captured A2854 bytes."""
import contextlib
import io
import tempfile
from pathlib import Path
import unittest

from descriptor_variants import (
    inspect_semantics, make_variants, describe, emit_files, WIDE, INPUT
)


def synthetic_descriptor() -> bytes:
    # Synthetic vendor collection with same *report geometry* and report IDs,
    # but not the actual A2854's captured 38-byte descriptor.
    raw = (
        bytes.fromhex("05 01 09 06 A1 01")   # Generic Desktop / Keyboard collection
        + bytes.fromhex("55 00 65 00")        # harmless global Unit Exponent/Unit
        + bytes.fromhex("09 30 15 00 26 FF 00")
        + WIDE
        + bytes.fromhex("85 B8 B2 01 00 85 04 75 08 96 11 00")
        + INPUT
        + bytes.fromhex("C0")
    )
    assert len(raw) == 38
    return raw


class DescriptorVariantTests(unittest.TestCase):
    def test_variants_preserve_report_lengths_and_id(self):
        original = synthetic_descriptor()
        variants = make_variants(original)
        self.assertEqual(
            set(variants),
            {"original", "single_input_usage", "input_usage_range",
             "feature_25x12", "range_and_25x12", "unbuffered_input",
             "range_and_unbuffered", "range_25x12_unbuffered"},
        )
        self.assertEqual(variants["original"], original)
        for name, raw in variants.items():
            with self.subTest(name=name):
                state = inspect_semantics(raw)
                self.assertEqual(state["errors"], [])
                self.assertEqual(
                    state["report_bits"],
                    {("Feature", 0xB8): 300, ("Input", 0x04): 136},
                )
                self.assertIn("Report", describe(name, raw).replace("Feature", "Report"))

    def test_usage_fix_only_clears_missing_usage_flag(self):
        before = inspect_semantics(synthetic_descriptor())["issues"]
        after = inspect_semantics(
            make_variants(synthetic_descriptor())["input_usage_range"])["issues"]
        self.assertIn("data_variable_input_without_local_usage", before)
        self.assertNotIn("data_variable_input_without_local_usage", after)
        self.assertIn("field_width_over_32_bits", after)
        self.assertIn("buffered_bytes_input", after)

    def test_width_fix_preserves_total_bits(self):
        variants = make_variants(synthetic_descriptor())
        wide = inspect_semantics(variants["original"])
        narrow = inspect_semantics(variants["feature_25x12"])
        self.assertIn("field_width_over_32_bits", wide["issues"])
        self.assertNotIn("field_width_over_32_bits", narrow["issues"])
        self.assertEqual(wide["report_bits"], narrow["report_bits"])
        self.assertEqual(len(variants["feature_25x12"]), 38)

    def test_combined_fixes_and_buffered_control(self):
        variants = make_variants(synthetic_descriptor())
        combined = inspect_semantics(variants["range_and_25x12"])
        self.assertEqual(combined["issues"], ["buffered_bytes_input"])
        clear_buffer = inspect_semantics(variants["unbuffered_input"])
        self.assertNotIn("buffered_bytes_input", clear_buffer["issues"])
        all_three = inspect_semantics(variants["range_25x12_unbuffered"])
        self.assertEqual(all_three["issues"], [])
        self.assertIn("data_variable_input_without_local_usage", clear_buffer["issues"])

    def test_reject_unrelated_and_corrupted_inputs(self):
        valid = synthetic_descriptor()
        with self.assertRaises(ValueError):
            make_variants(valid[:-1])
        with self.assertRaises(ValueError):
            make_variants(valid.replace(WIDE, bytes.fromhex("75 08 96 06 00")))
        with self.assertRaises(ValueError):
            make_variants(valid.replace(INPUT, bytes.fromhex("81 02")))
        self.assertNotEqual(inspect_semantics(valid[:-1])["errors"], [])

    def test_output_is_local_and_create_only(self):
        variants = make_variants(synthetic_descriptor())
        with tempfile.TemporaryDirectory() as td:
            dest = Path(td) / "new"
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                emit_files(variants, dest)
            self.assertEqual(len(list(dest.glob("*.bin"))), 8)
            self.assertEqual((dest / "original.bin").read_bytes(),
                             synthetic_descriptor())
            self.assertNotIn("06 00", output.getvalue())
            with self.assertRaises(FileExistsError):
                emit_files(variants, dest)


if __name__ == "__main__":
    unittest.main()
