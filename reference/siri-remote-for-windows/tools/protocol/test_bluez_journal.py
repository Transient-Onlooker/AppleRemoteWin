"""Offline tests: log classification and output must never leak device IDs."""
import contextlib
import io
import unittest
from unittest.mock import patch
from types import SimpleNamespace
import sys

from bluez_journal import classify, summarize, fingerprint, main


class JournalTests(unittest.TestCase):
    def test_gatt_discovery_error_and_code(self):
        private = "10:B9:C4:2D:07:8E"
        result = classify(
            f"bluetoothd[{private}]: Primary service discovery failed. ATT ECODE: 0x0a"
        )
        self.assertEqual(result, ("gatt-or-service-discovery", ("0x0A",)))
        self.assertNotIn(private, str(result))

    def test_link_reason(self):
        result = classify("hci0: LE connection disconnected reason: 0x13")
        self.assertEqual(result, ("connection-or-disconnection", ("0x13",)))

    def test_bluez_service_error(self):
        result = classify("device_svc_resolved failed: err -5")
        self.assertEqual(result[0], "gatt-or-service-discovery")
        self.assertEqual(result[1], ("-5",))

    def test_clean_output_with_private_source_logs(self):
        mac = "10:B9:C4:2D:07:8E"
        path = "/org/bluez/hci0/dev_10_B9_C4_2D_07_8E"
        entries = (
            f"{path} {mac} Primary service discovery failed. ATT ECODE: 0x0a\n"
            f"{path} {mac} disconnected reason: 0x13\n"
            f"{path} {mac} failed to connect: error 3\n"
        )
        fake = SimpleNamespace(returncode=0, stdout=entries)
        with patch("bluez_journal.subprocess.run", return_value=fake):
            with patch.object(sys, "argv", ["bluez_journal.py"]):
                buffer = io.StringIO()
                with contextlib.redirect_stdout(buffer):
                    status = main()
        self.assertEqual(status, 0)
        output = buffer.getvalue()
        self.assertIn("gatt-or-service-discovery", output)
        self.assertIn("connection-or-disconnection", output)
        self.assertIn("0x0A", output)
        self.assertNotIn(mac, output)
        self.assertNotIn(path, output)
        self.assertNotIn("Primary service", output)

    def test_non_error_unrelated_line_ignored(self):
        self.assertIsNone(classify("Bluetooth device list changed"))

    def test_allowlisted_timeline_reports_stage_not_identifiers(self):
        path = "/org/bluez/hci0/dev_10_B9_C4_2D_07_8E"
        private = "10:B9:C4:2D:07:8E"
        value = fingerprint(
            f"{path} {private} Primary service discovery failed ATT ECODE: 0x0a"
        )
        self.assertEqual(value,
                         ("gatt-or-service-discovery", "primary-service-discovery",
                          "failure", ("0x0A",)))
        self.assertNotIn(path, str(value))
        self.assertNotIn(private, str(value))

    def test_safe_timeline_output_for_mixed_events(self):
        private = "10:B9:C4:2D:07:8E"
        events = (
            f"{private} GATT service discovery failed status: 0x0a\n"
            f"{private} connection aborted reason: 0x13\n"
            "Failed to register codec endpoint\n"
        )
        response = SimpleNamespace(returncode=0, stdout=events)
        with patch("bluez_journal.subprocess.run", return_value=response) as run:
            with patch.object(sys, "argv", [
                "bluez_journal.py", "--detail", "--kernel", "--minutes", "45"]):
                buffer = io.StringIO()
                with contextlib.redirect_stdout(buffer):
                    status = main()
        self.assertEqual(status, 0)
        output = buffer.getvalue()
        self.assertIn("stage=gatt-attribute", output)
        self.assertIn("stage=device-connect", output)
        self.assertIn("stage=plugin-or-profile", output)
        self.assertNotIn(private, output)
        self.assertNotIn("codec endpoint", output)
        command = run.call_args.args[0]
        self.assertIn("--dmesg", command)
        self.assertNotIn("--unit=bluetooth.service", command)

    def test_summary_empty(self):
        categories, details = summarize(["Some unrelated log line"])
        self.assertFalse(categories)
        self.assertFalse(details)


if __name__ == "__main__":
    unittest.main()
