"""Cross-platform pure-decoder regression tests.

Run: python -m unittest discover -s tools/windows-probe -v
"""
import unittest
from hid_probe import ReportDecoder, button_names, decode_touch


class ReportDecoderTest(unittest.TestCase):
    def test_button_press_and_release_from_hardware_fixtures(self):
        decoder = ReportDecoder()
        self.assertEqual(decoder.decode(bytes.fromhex("fb 00 04")),
                         "0xFB buttons=Right pressed=Right released=none")
        self.assertEqual(decoder.decode(bytes.fromhex("fb 00 04")), None)
        self.assertEqual(decoder.decode(bytes.fromhex("fb 00 00")),
                         "0xFB buttons=none pressed=none released=Right")

    def test_button_little_endian_and_padding(self):
        self.assertIn("TV+Play/Pause", ReportDecoder().decode(bytes.fromhex("fb 01 01 00 00")))
        self.assertEqual(button_names(0xE000), "none")

    def test_touch_hover_fixture(self):
        report = bytes.fromhex("fc 32 14 fc 01 c9 9e fb 51 44 01 66")
        self.assertEqual(ReportDecoder().decode(report),
                         "0xFC touch x=-311 y=-71 pressure=1 hover=True major=81 minor=68")

    def test_touch_contact_and_release_fixture(self):
        contact = bytes.fromhex("32 92 d6 01 66 dc d5 60 1c 09 ac")
        self.assertEqual(decode_touch(contact),
                         "touch x=-922 y=-675 pressure=9 hover=False major=96 minor=28")
        self.assertEqual(decode_touch(bytes.fromhex("32 36 fc 00 c9 9e fb 00 00 00 67")),
                         "touch released")

    def test_reject_audio_and_invalid_packets(self):
        decoder = ReportDecoder()
        self.assertIsNone(decoder.decode(bytes.fromhex("fa 00 01 02")))
        self.assertIsNone(decoder.decode(bytes.fromhex("fc 00 01")))
        self.assertIsNone(decoder.decode(b""))
        self.assertIsNone(decode_touch(bytes.fromhex("00 14 fc 01 c9 9e fb 51 44 01 66")))


if __name__ == "__main__":
    unittest.main()
