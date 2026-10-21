"""Bounded GATT reconnect regression tests; no physical remote required."""

import unittest
from unittest.mock import AsyncMock, patch

from gatt_probe import Candidate, run


class FakeBluetoothLEDevice:
    pass


class FakeCache:
    pass


class FakeDeviceInfo:
    pass


class RetryTests(unittest.IsolatedAsyncioTestCase):
    async def test_retries_only_unreachable_then_stops_at_access_denied(self):
        statuses = iter([("UNREACHABLE", 2), ("SUCCESS", 1)])

        async def attempt(*args, **kwargs):
            status, code = next(statuses)
            kwargs["attempt_result"].discovery_status = status
            return code

        with (
            patch("gatt_probe.runtime_modules", return_value=(
                FakeBluetoothLEDevice, FakeCache, FakeDeviceInfo)),
            patch("gatt_probe.list_paired", new_callable=AsyncMock,
                  return_value=[Candidate("DL1", "private-id")]),
            patch("gatt_probe.probe_one", new_callable=AsyncMock,
                  side_effect=attempt) as probe,
            patch("gatt_probe.asyncio.sleep", new_callable=AsyncMock) as sleep,
        ):
            result = await run(0, compare_hid=True, attempts=3)
        self.assertEqual(result, 1)
        self.assertEqual(probe.await_count, 2)
        self.assertEqual(probe.await_args.kwargs["compare_hid"], True)
        sleep.assert_awaited_once_with(5)

    async def test_stops_after_limit_on_unreachable(self):
        async def always_unreachable(*args, **kwargs):
            kwargs["attempt_result"].discovery_status = "UNREACHABLE"
            return 2

        with (
            patch("gatt_probe.runtime_modules", return_value=(
                FakeBluetoothLEDevice, FakeCache, FakeDeviceInfo)),
            patch("gatt_probe.list_paired", new_callable=AsyncMock,
                  return_value=[Candidate("DL1", "private-id")]),
            patch("gatt_probe.probe_one", new_callable=AsyncMock,
                  side_effect=always_unreachable) as probe,
            patch("gatt_probe.asyncio.sleep", new_callable=AsyncMock) as sleep,
        ):
            result = await run(0, attempts=3)
        self.assertEqual(result, 2)
        self.assertEqual(probe.await_count, 3)
        self.assertEqual(sleep.await_count, 2)

    async def test_access_denied_not_retried(self):
        async def denied(*args, **kwargs):
            kwargs["attempt_result"].discovery_status = "SUCCESS"
            return 1

        with (
            patch("gatt_probe.runtime_modules", return_value=(
                FakeBluetoothLEDevice, FakeCache, FakeDeviceInfo)),
            patch("gatt_probe.list_paired", new_callable=AsyncMock,
                  return_value=[Candidate("DL1", "private-id")]),
            patch("gatt_probe.probe_one", new_callable=AsyncMock,
                  side_effect=denied) as probe,
            patch("gatt_probe.asyncio.sleep", new_callable=AsyncMock) as sleep,
        ):
            self.assertEqual(await run(0, attempts=3), 1)
        self.assertEqual(probe.await_count, 1)
        sleep.assert_not_awaited()

    async def test_pre_scan_completes_before_attempt(self):
        sequence = []

        async def scan(_):
            sequence.append("scan")
            return 0

        async def probe(*args, **kwargs):
            sequence.append("probe")
            kwargs["attempt_result"].discovery_status = "SUCCESS"
            return 0

        with (
            patch("gatt_probe.runtime_modules", return_value=(
                FakeBluetoothLEDevice, FakeCache, FakeDeviceInfo)),
            patch("gatt_probe.list_paired", new_callable=AsyncMock,
                  return_value=[Candidate("DL1", "private-id")]),
            patch("adv_probe.scan", new_callable=AsyncMock,
                  side_effect=scan) as scanner,
            patch("gatt_probe.probe_one", new_callable=AsyncMock,
                  side_effect=probe),
        ):
            self.assertEqual(await run(0, attempts=1, scan_before=10), 0)
        scanner.assert_awaited_once_with(10)
        self.assertEqual(sequence, ["scan", "probe"])


if __name__ == "__main__":
    unittest.main()
