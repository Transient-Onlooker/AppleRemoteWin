"""Synthetic tests for direct WinRT raw A2854 input probe.

No Bluetooth hardware, PnP mutations or remote addresses.
"""
from __future__ import annotations
import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock

from a2854_winrt_input import (
    HID_SERVICE,
    HID_REPORT,
    REPORT_REFERENCE,
    classify_hid_status,
    get_reports,
    parse_report_reference,
    receive,
    safe_exception,
)


class FakeCache:
    UNCACHED = "uncached"


class FakeCccd:
    NOTIFY = "notify"
    NONE = "none"


def reply(status: str, **kwargs):
    return SimpleNamespace(status=SimpleNamespace(name=status), **kwargs)


class ParserTests(unittest.TestCase):
    def test_report_reference_classifies_input_and_feature(self):
        self.assertEqual(parse_report_reference(bytes([0xFB, 1])).report_id, 0xFB)
        self.assertEqual(parse_report_reference(bytes([0xFC, 1])).report_type, 1)
        self.assertEqual(parse_report_reference(bytes([0xF0, 3])).report_type, 3)
        self.assertIsNone(parse_report_reference(bytes([0xFB, 0])))
        self.assertIsNone(parse_report_reference(bytes([0xFB, 1, 2])))

    def test_access_denied_is_explicit_os_restriction(self):
        self.assertEqual(classify_hid_status(SimpleNamespace(name="ACCESS_DENIED")),
                         "WINDOWS_PROTECTED_HID_SERVICE")
        self.assertEqual(classify_hid_status(SimpleNamespace(name="SUCCESS")), "ACCESS_GRANTED")

    def test_exception_sanitizes_sensitive_pnp_path(self):
        error = OSError("private PnP path BTHLEDEVICE\\DEV_FAKE")
        self.assertNotIn("BTHLEDEVICE", safe_exception(error))


class ProbeTests(unittest.IsolatedAsyncioTestCase):
    async def test_gatt_access_denied_never_reads_reports(self):
        service = SimpleNamespace(
            get_characteristics_with_cache_mode_async=AsyncMock(
                return_value=reply("ACCESS_DENIED")
            )
        )
        reports, state, count = await get_reports(service, FakeCache)
        self.assertEqual(reports, [])
        self.assertEqual(state, "WINDOWS_PROTECTED_HID_SERVICE")
        self.assertEqual(count, 0)

    async def test_report_reference_targets_exact_id_in_duplicate_uuid_characteristics(self):
        async def mk_report(rid: int):
            descriptor = SimpleNamespace(uuid=REPORT_REFERENCE,
                read_value_async=AsyncMock(return_value=reply("SUCCESS", value=bytes([rid, 1]))))
            char = SimpleNamespace(
                uuid=HID_REPORT,
                get_descriptors_with_cache_mode_async=AsyncMock(
                    return_value=reply("SUCCESS", descriptors=[descriptor]))
            )
            return char
        first = await mk_report(0xFB)
        second = await mk_report(0xFC)
        third = await mk_report(0xFA)
        service = SimpleNamespace(
            get_characteristics_with_cache_mode_async=AsyncMock(
                return_value=reply("SUCCESS", characteristics=[first, second, third])
            )
        )
        reports, state, count = await get_reports(service, FakeCache)
        self.assertEqual(state, "ACCESS_GRANTED")
        self.assertEqual(count, 3)
        self.assertEqual([v.report_id for _, v in reports], [0xFB, 0xFC, 0xFA])
        self.assertTrue(all(v.report_type == 1 for _, v in reports))

    async def test_unreachable_status_only_allows_bounded_transport_retry(self):
        device = SimpleNamespace(
            get_gatt_services_with_cache_mode_async=AsyncMock(
                return_value=reply("UNREACHABLE", services=[]))
        )
        self.assertEqual(
            await receive(device, FakeCache, FakeCccd, seconds=5, enable=False),
            7,
        )
        other = SimpleNamespace(
            get_gatt_services_with_cache_mode_async=AsyncMock(
                return_value=reply("PROTOCOL_ERROR", services=[]))
        )
        self.assertEqual(
            await receive(other, FakeCache, FakeCccd, seconds=5, enable=False),
            2,
        )

    async def test_receiver_exits_before_notifications_when_hid_denied(self):
        service = SimpleNamespace(
            uuid=HID_SERVICE, close=Mock(),
            get_characteristics_with_cache_mode_async=AsyncMock(
                return_value=reply("ACCESS_DENIED"))
        )
        device = SimpleNamespace(
            get_gatt_services_with_cache_mode_async=AsyncMock(
                return_value=reply("SUCCESS", services=[service]))
        )
        result = await receive(device, FakeCache, FakeCccd, seconds=5, enable=True)
        self.assertEqual(result, 3)
        service.close.assert_called_once()

    async def test_no_microphone_or_other_data_subscribed(self):
        service = SimpleNamespace(
            uuid=HID_SERVICE, close=Mock(),
            get_characteristics_with_cache_mode_async=AsyncMock(
                return_value=reply("SUCCESS", characteristics=[]))
        )
        device = SimpleNamespace(
            get_gatt_services_with_cache_mode_async=AsyncMock(
                return_value=reply("SUCCESS", services=[service]))
        )
        result = await receive(device, FakeCache, FakeCccd, seconds=5, enable=False)
        self.assertEqual(result, 4)


if __name__ == "__main__":
    unittest.main()
