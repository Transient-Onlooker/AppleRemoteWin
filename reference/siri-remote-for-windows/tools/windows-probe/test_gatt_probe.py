"""Regression tests for WinRT BLE enumeration without physical Bluetooth hardware.

Run: py -3 -m unittest discover -s tools/windows-probe -v
"""

import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock

from gatt_probe import list_paired, status_text


class FakeBluetoothLEDevice:
    @staticmethod
    def get_device_selector_from_pairing_state(is_paired):
        assert is_paired is True
        return "(System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True)"


class FakeDeviceInformation:
    # PyWinRT's real AQS overload is exposed under this distinct method name,
    # NOT DeviceInformation.find_all_async(selector).
    find_all_async_aqs_filter = AsyncMock(return_value=[
        SimpleNamespace(name="DL1", id="private-device-id"),
        SimpleNamespace(name="Other Device", id="other-private-id"),
    ])


class GattProbeEnumerationTest(unittest.IsolatedAsyncioTestCase):
    async def test_paired_ble_enumeration_uses_pywinrt_aqs_overload(self):
        FakeDeviceInformation.find_all_async_aqs_filter.reset_mock()
        devices = await list_paired(FakeBluetoothLEDevice, FakeDeviceInformation)
        FakeDeviceInformation.find_all_async_aqs_filter.assert_awaited_once_with(
            "(System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True)")
        self.assertEqual([device.name for device in devices], ["DL1", "Other Device"])
        self.assertEqual(devices[0].device_id, "private-device-id")

    async def test_missing_device_name_is_handled(self):
        class AnonymousDeviceInformation:
            find_all_async_aqs_filter = AsyncMock(
                return_value=[SimpleNamespace(name="", id="hidden")])

        devices = await list_paired(FakeBluetoothLEDevice, AnonymousDeviceInformation)
        self.assertEqual(devices[0].name, "(unnamed BLE)")

    async def test_empty_paired_list(self):
        class EmptyDeviceInformation:
            find_all_async_aqs_filter = AsyncMock(return_value=[])

        self.assertEqual(await list_paired(FakeBluetoothLEDevice, EmptyDeviceInformation), [])

    def test_status_text_supports_string_or_enum_style(self):
        self.assertEqual(status_text("Success"), "Success")
        self.assertEqual(status_text(SimpleNamespace(name="SUCCESS")), "SUCCESS")


if __name__ == "__main__":
    unittest.main()
