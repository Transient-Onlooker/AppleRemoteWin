"""Offline tests of Windows GATT access denial and HID snapshot diagnostics."""

import asyncio
from collections import Counter
from contextlib import redirect_stdout
from io import StringIO
from types import SimpleNamespace
import unittest
from unittest.mock import AsyncMock, MagicMock, patch

from gatt_probe import Candidate, print_hid_comparison, probe_one


class FakeCacheMode:
    UNCACHED = object()


class GattDeniedPathTests(unittest.IsolatedAsyncioTestCase):
    async def test_connected_hid_denied_and_no_new_hid_interfaces(self):
        service = SimpleNamespace(
            uuid="00001812-0000-1000-8000-00805f9b34fb",
            get_characteristics_with_cache_mode_async=AsyncMock(
                return_value=SimpleNamespace(status="ACCESS_DENIED", protocol_error=None)),
            close=MagicMock(),
        )
        device = SimpleNamespace(
            name="Bluetooth private address",
            connection_status="CONNECTED",
            get_gatt_services_with_cache_mode_async=AsyncMock(
                return_value=SimpleNamespace(
                    status="SUCCESS", protocol_error=None, services=[service])),
            close=MagicMock(),
        )
        class FakeBluetoothLEDevice:
            from_id_async = AsyncMock(return_value=device)

        snapshot = Counter({(0x05AC, 0x1234, 0x01, 0x02, 12): 1})
        capture = StringIO()
        with patch("gatt_probe.hid_snapshot", side_effect=[snapshot, snapshot]):
            with redirect_stdout(capture):
                code = await probe_one(
                    Candidate(name="DL1", device_id="private-device-id"),
                    FakeBluetoothLEDevice, FakeCacheMode, compare_hid=True)
        output = capture.getvalue()
        self.assertEqual(code, 1)
        self.assertIn("Service discovery status: SUCCESS", output)
        self.assertIn("HID characteristic discovery: ACCESS_DENIED", output)
        self.assertIn("HID interfaces before=1 after=1", output)
        self.assertIn("No new HID interface signatures", output)
        self.assertNotIn("private-device-id", output)
        self.assertNotIn("Bluetooth private address", output)
        device.close.assert_called_once()
        service.close.assert_called_once()

    def test_snapshot_outputs_only_coarse_metadata(self):
        old = Counter({(0x046D, 0xC08B, 1, 2, 9): 1})
        new = old + Counter({(0x05AC, 0xABCD, 0x0C, 1, 3): 1})
        capture = StringIO()
        with redirect_stdout(capture):
            print_hid_comparison(old, new)
        output = capture.getvalue()
        self.assertIn("HID interfaces before=1 after=2", output)
        self.assertIn("Added 1: VID=05AC PID=ABCD usage=000C:0001", output)
        self.assertNotIn("DevicePath", output)


if __name__ == "__main__":
    unittest.main()
