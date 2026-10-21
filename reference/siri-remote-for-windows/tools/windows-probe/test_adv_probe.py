"""Offline tests of BLE scanner privacy and candidate classification."""

import unittest
from types import SimpleNamespace

from adv_probe import APPLE_COMPANY_ID, HID_UUID, classify_advertisement


class AdvertisementProbeTests(unittest.TestCase):
    def test_remote_name_and_hid_uuid(self):
        adv = SimpleNamespace(
            local_name="DL1JPFFV2330",
            service_uuids=[HID_UUID.upper()],
            manufacturer_data={},
        )
        self.assertEqual(classify_advertisement(adv),
                         ("remote-like name", "HID service 0x1812 advertised"))

    def test_apple_manufacturer_alone_is_not_proof_of_remote(self):
        adv = SimpleNamespace(
            local_name=None,
            service_uuids=[],
            manufacturer_data={APPLE_COMPANY_ID: b"\x01\x02\x03"},
        )
        markers = classify_advertisement(adv)
        self.assertEqual(markers, ("Apple manufacturer (not necessarily remote)",))
        self.assertNotIn("01", str(markers))

    def test_unrelated_device_is_ignored(self):
        adv = SimpleNamespace(
            local_name="Generic Keyboard", service_uuids=[],
            manufacturer_data={0x1234: b"\x00"},
        )
        self.assertEqual(classify_advertisement(adv), ())

    def test_missing_advertisement_fields_are_safe(self):
        self.assertEqual(classify_advertisement(SimpleNamespace()), ())

    def test_sensitive_inputs_are_not_returned(self):
        adv = SimpleNamespace(
            local_name="DL1-PRIVATEDEVICE-SERIAL-123",
            service_uuids=[HID_UUID], manufacturer_data={APPLE_COMPANY_ID: b"\xff\xee"},
        )
        result = str(classify_advertisement(adv))
        self.assertNotIn("PRIVATEDEVICE", result)
        self.assertNotIn("SERIAL", result)
        self.assertNotIn("ff", result.lower())


if __name__ == "__main__":
    unittest.main()
