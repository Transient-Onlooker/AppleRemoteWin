"""Offline HID Report Map parser/BlueZ discovery tests."""

import unittest
import subprocess
import sys
from pathlib import Path
from types import SimpleNamespace

from report_map import (HID_SERVICE, REPORT_MAP, find_map_paths,
                        find_bonded_device_paths, inspect_map,
                        remote_gatt_state, safe_bluez_error_name)


def v(x):
    return SimpleNamespace(value=x)


class ReportMapTests(unittest.TestCase):
    def test_inspect_valid_minimal_map(self):
        raw = bytes.fromhex(
            "05 01 09 06 A1 01 85 FB 75 08 95 02 81 02 C0")
        result = inspect_map(raw)
        self.assertEqual(result["report_ids"], [0xFB])
        self.assertEqual(result["maximum_collection_depth"], 1)
        self.assertEqual(result["warnings"], [])

    def test_two_report_ids_and_long_item(self):
        raw = bytes.fromhex("A1 01 85 FB 85 FC FE 02 AB 01 02 C0")
        result = inspect_map(raw)
        self.assertEqual(result["report_ids"], [0xFB, 0xFC])
        self.assertEqual(result["warnings"], [])

    def test_truncated_short_and_long_items(self):
        self.assertIn("Truncated item payload", inspect_map(bytes([0x75]))["warnings"][0])
        self.assertIn("Truncated long-item header", inspect_map(bytes([0xFE]))["warnings"][0])
        self.assertIn("Truncated long-item payload",
                      inspect_map(bytes([0xFE, 2, 0x12, 0xAA]))["warnings"][0])

    def test_missing_end_and_illegal_report_id(self):
        parsed = inspect_map(bytes.fromhex("A1 01 85 00"))
        self.assertTrue(any("Unclosed Collections" in x for x in parsed["warnings"]))
        self.assertTrue(any("Report ID 0" in x for x in parsed["warnings"]))

    def test_extra_end_collection(self):
        self.assertTrue(any("Unexpected End" in x for x in
                            inspect_map(bytes([0xC0]))["warnings"]))

    def test_empty_map(self):
        self.assertIn("Empty Report Map", inspect_map(b"")["warnings"])

    def test_connected_resolved_only_and_no_address_output(self):
        dev = "/org/bluez/hci0/dev_11_22_33_44_55_66"
        sv = dev + "/service0041"
        ch = sv + "/char0042"
        other = "/org/bluez/hci0/dev_77_88_99_AA_BB_CC"
        objects = {
            dev: {"org.bluez.Device1": {
                "Name": v("DL1PRIVATE_SERIAL"), "Connected": v(True),
                "ServicesResolved": v(True)}},
            sv: {"org.bluez.GattService1": {"UUID": v(HID_SERVICE.upper())}},
            ch: {"org.bluez.GattCharacteristic1": {"UUID": v(REPORT_MAP)}},
            other: {"org.bluez.Device1": {
                "Name": v("Different"), "Connected": v(True),
                "ServicesResolved": v(True)}},
            other + "/service0021": {
                "org.bluez.GattService1": {"UUID": v(HID_SERVICE)}},
            other + "/service0021/char0022": {
                "org.bluez.GattCharacteristic1": {"UUID": v(REPORT_MAP)}},
        }
        self.assertEqual(find_map_paths(objects, "DL1"), [ch])
        self.assertEqual(len(find_map_paths(objects)), 2)
        objects[dev]["org.bluez.Device1"]["Connected"] = v(False)
        self.assertEqual(find_map_paths(objects, "DL1"), [])

    def test_bonded_selection_requires_prefix_and_existing_pair(self):
        objects = {
            "/org/bluez/hci0/dev_private1": {"org.bluez.Device1": {
                "Name": v("DL1REMOTE"), "Paired": v(True), "Connected": v(False)}},
            "/org/bluez/hci0/dev_private2": {"org.bluez.Device1": {
                "Name": v("DL1UNPAIRED"), "Paired": v(False), "Connected": v(True)}},
            "/org/bluez/hci0/dev_private3": {"org.bluez.Device1": {
                "Name": v("Keyboard"), "Paired": v(True), "Connected": v(True)}},
        }
        self.assertEqual(
            find_bonded_device_paths(objects, "DL1"),
            ["/org/bluez/hci0/dev_private1"])
        self.assertEqual(find_bonded_device_paths(objects, ""), [])
        self.assertEqual(find_bonded_device_paths(objects, "missing"), [])

    def test_sanitized_gatt_stage_counts_for_fleeting_connection(self):
        device = "/org/bluez/hci0/dev_PRIVATE_ADDRESS"
        svc = device + "/service0034"
        states = {
            device: {"org.bluez.Device1": {
                "Name": v("DL1PRIVATE_SERIAL"), "Paired": v(True),
                "Connected": v(True), "ServicesResolved": v(False)}},
            svc: {"org.bluez.GattService1": {"UUID": v(HID_SERVICE)}},
            svc + "/char0040": {"org.bluez.GattCharacteristic1": {
                "UUID": v(REPORT_MAP)}},
            svc + "/char0041": {"org.bluez.GattCharacteristic1": {
                "UUID": v("00002a4d-0000-1000-8000-00805f9b34fb")}},
        }
        result = remote_gatt_state(states, device)
        self.assertEqual(result, {
            "connected": True, "resolved": False, "hid_services": 1,
            "map_chars": 1, "report_chars": 1})
        self.assertNotIn("PRIVATE", str(result))
        states[device]["org.bluez.Device1"]["Connected"] = v(False)
        self.assertFalse(remote_gatt_state(states, device)["connected"])

    def test_error_name_filter_never_emits_device_addresses(self):
        self.assertEqual(safe_bluez_error_name(
            SimpleNamespace(error_name="org.bluez.Error.NotReady")),
            "NotReady")
        self.assertEqual(safe_bluez_error_name(
            SimpleNamespace(error_name="org.bluez.Error.Failed_PRIVATE_DEVICE")),
            "other-error")
        self.assertEqual(safe_bluez_error_name(
            SimpleNamespace(error_name="org.freedesktop.DBus.Error.NoReply")),
            "NoReply")
        self.assertEqual(safe_bluez_error_name(SimpleNamespace(error_name=None)),
                         "other-error")

    def test_short_scan_requires_opt_in_connection(self):
        script = Path(__file__).with_name("report_map.py")
        result = subprocess.run(
            [sys.executable, str(script), "--inspect", "nonexistent.bin",
             "--scan-before", "8"],
            capture_output=True, text=True, timeout=10,
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("--scan-before requires --connect", result.stderr)

    def test_connection_requires_exact_prior_name_prefix(self):
        script = Path(__file__).with_name("report_map.py")
        result = subprocess.run(
            [sys.executable, str(script), "--output", "irrelevant.bin",
             "--connect", "--scan-before", "8"],
            capture_output=True, text=True, timeout=10,
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("--connect requires --output", result.stderr)

    def test_observation_duration_is_bounded(self):
        script = Path(__file__).with_name("report_map.py")
        args = [sys.executable, str(script), "--output", "irrelevant.bin",
                "--name-prefix", "DL1", "--connect"]
        for extra in (["--observe-seconds", "91"],
                      ["--observe-seconds", "0"],
                      ["--attempts", "4"],
                      ["--attempts", "0"]):
            proc = subprocess.run(args + extra, capture_output=True,
                                  text=True, timeout=10)
            self.assertNotEqual(proc.returncode, 0)
            self.assertIn("--observe-seconds must be 8-90", proc.stderr)

    def test_long_observation_opt_in_only(self):
        script = Path(__file__).with_name("report_map.py")
        proc = subprocess.run(
            [sys.executable, str(script), "--inspect", "missing.bin",
             "--observe-seconds", "45", "--attempts", "1"],
            capture_output=True, text=True, timeout=10)
        self.assertNotEqual(proc.returncode, 0)
        self.assertIn("require --connect", proc.stderr)

    def test_scan_duration_is_bounded(self):
        script = Path(__file__).with_name("report_map.py")
        result = subprocess.run(
            [sys.executable, str(script), "--output", "irrelevant.bin",
             "--name-prefix", "DL1", "--connect", "--scan-before", "100"],
            capture_output=True, text=True, timeout=10,
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("duration of 1-20", result.stderr)

    def test_no_hid_service_no_candidate(self):
        path = "/org/bluez/hci0/dev_11_22_33_44_55_66"
        objects = {
            path: {"org.bluez.Device1": {
                "Name": "DL1", "Connected": True, "ServicesResolved": True}},
            path + "/service0001": {
                "org.bluez.GattService1": {"UUID": "0000180f-0000-1000-8000-00805f9b34fb"}},
            path + "/service0001/char0002": {
                "org.bluez.GattCharacteristic1": {"UUID": REPORT_MAP}},
        }
        self.assertEqual(find_map_paths(objects, "DL1"), [])


if __name__ == "__main__":
    unittest.main()
