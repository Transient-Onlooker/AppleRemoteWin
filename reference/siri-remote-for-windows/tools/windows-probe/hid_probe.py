#!/usr/bin/env python3
"""Read-only Windows HID discovery and A2854 report accessibility probe.

Requires Windows 10/11 and Python 3.9+. Uses only the Python standard library.
No pairing, report-enable writes, input injection, or microphone capture.
"""

from __future__ import annotations

import argparse
import ctypes
from ctypes import wintypes
from dataclasses import dataclass
import sys
import time

APPLE_USB_VENDOR_ID = 0x05AC
BUTTONS = (
    (0x0001, "TV"), (0x0002, "Volume Up"), (0x0004, "Volume Down"),
    (0x0008, "Select"), (0x0010, "Power"), (0x0020, "Siri"),
    (0x0040, "Back"), (0x0080, "Mute"), (0x0100, "Play/Pause"),
    (0x0200, "Up"), (0x0400, "Right"), (0x0800, "Down"),
    (0x1000, "Left"),
)


def button_names(mask: int) -> str:
    return "+".join(name for bit, name in BUTTONS if mask & bit) or "none"


def decode_touch(payload: bytes) -> str | None:
    """Decode only the first contact; HID packets may be zero-padded."""
    if len(payload) < 11 or payload[0] != 0x32:
        return None
    b = payload[4:11]
    if b[3] == b[4] == b[5] == 0:
        return "touch released"
    packed = b[0] | b[1] << 8 | b[2] << 16
    x = packed & 0xFFF
    y = (packed >> 12) & 0xFFF
    x = x - 0x1000 if x & 0x800 else x
    y = y - 0x1000 if y & 0x800 else y
    return (f"touch x={x} y={y} pressure={b[5]} "
            f"hover={bool(b[6] & 0x02)} major={b[3]} minor={b[4]}")


@dataclass
class ReportDecoder:
    last_buttons: int = 0

    def decode(self, data: bytes) -> str | None:
        # Windows HID ReadFile buffers start with a report ID. Some device
        # drivers pad the remaining data to the maximum HID report length.
        if not data:
            return None
        report_id, payload = data[0], data[1:]
        if report_id == 0xFB and len(payload) >= 2:
            mask = int.from_bytes(payload[:2], "little")
            pressed, released = mask & ~self.last_buttons, self.last_buttons & ~mask
            self.last_buttons = mask
            if not (pressed or released):
                return None
            return (f"0xFB buttons={button_names(mask)} "
                    f"pressed={button_names(pressed)} released={button_names(released)}")
        if report_id == 0xFC:
            touch = decode_touch(payload)
            return f"0xFC {touch}" if touch is not None else None
        # Do not log unknown reports; 0xFA may contain microphone data.
        return None


if sys.platform == "win32":
    # Importable without Windows so pure decoder tests also run on Linux.
    setupapi = ctypes.WinDLL("setupapi", use_last_error=True)
    hid = ctypes.WinDLL("hid", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

    class GUID(ctypes.Structure):
        _fields_ = [("Data1", wintypes.DWORD), ("Data2", wintypes.WORD),
                    ("Data3", wintypes.WORD), ("Data4", ctypes.c_ubyte * 8)]

    class InterfaceData(ctypes.Structure):
        _fields_ = [("cbSize", wintypes.DWORD), ("InterfaceClassGuid", GUID),
                    ("Flags", wintypes.DWORD), ("Reserved", ctypes.c_void_p)]

    class HidAttributes(ctypes.Structure):
        _fields_ = [("Size", wintypes.DWORD), ("VendorID", wintypes.WORD),
                    ("ProductID", wintypes.WORD), ("VersionNumber", wintypes.WORD)]

    class HidCaps(ctypes.Structure):
        _fields_ = [("Usage", wintypes.WORD), ("UsagePage", wintypes.WORD),
                    ("InputReportByteLength", wintypes.WORD),
                    ("OutputReportByteLength", wintypes.WORD),
                    ("FeatureReportByteLength", wintypes.WORD),
                    ("Reserved", wintypes.WORD * 17),
                    ("NumberLinkCollectionNodes", wintypes.WORD),
                    ("NumberInputButtonCaps", wintypes.WORD),
                    ("NumberInputValueCaps", wintypes.WORD),
                    ("NumberInputDataIndices", wintypes.WORD),
                    ("NumberOutputButtonCaps", wintypes.WORD),
                    ("NumberOutputValueCaps", wintypes.WORD),
                    ("NumberOutputDataIndices", wintypes.WORD),
                    ("NumberFeatureButtonCaps", wintypes.WORD),
                    ("NumberFeatureValueCaps", wintypes.WORD),
                    ("NumberFeatureDataIndices", wintypes.WORD)]

    class Overlapped(ctypes.Structure):
        _fields_ = [("Internal", ctypes.c_void_p), ("InternalHigh", ctypes.c_void_p),
                    ("Offset", wintypes.DWORD), ("OffsetHigh", wintypes.DWORD),
                    ("hEvent", wintypes.HANDLE)]

    HID_GUID = GUID()
    hid.HidD_GetHidGuid.argtypes = [ctypes.POINTER(GUID)]
    hid.HidD_GetHidGuid.restype = None
    setupapi.SetupDiGetClassDevsW.argtypes = [ctypes.POINTER(GUID), wintypes.LPCWSTR,
                                              wintypes.HWND, wintypes.DWORD]
    setupapi.SetupDiGetClassDevsW.restype = wintypes.HANDLE
    setupapi.SetupDiEnumDeviceInterfaces.argtypes = [wintypes.HANDLE, ctypes.c_void_p,
                                                      ctypes.POINTER(GUID), wintypes.DWORD,
                                                      ctypes.POINTER(InterfaceData)]
    setupapi.SetupDiEnumDeviceInterfaces.restype = wintypes.BOOL
    setupapi.SetupDiGetDeviceInterfaceDetailW.argtypes = [wintypes.HANDLE,
                                                           ctypes.POINTER(InterfaceData),
                                                           ctypes.c_void_p, wintypes.DWORD,
                                                           ctypes.POINTER(wintypes.DWORD),
                                                           ctypes.c_void_p]
    setupapi.SetupDiGetDeviceInterfaceDetailW.restype = wintypes.BOOL
    setupapi.SetupDiDestroyDeviceInfoList.argtypes = [wintypes.HANDLE]
    setupapi.SetupDiDestroyDeviceInfoList.restype = wintypes.BOOL
    kernel32.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                                     ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD,
                                     wintypes.HANDLE]
    kernel32.CreateFileW.restype = wintypes.HANDLE
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.CloseHandle.restype = wintypes.BOOL
    kernel32.CreateEventW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.BOOL,
                                      wintypes.LPCWSTR]
    kernel32.CreateEventW.restype = wintypes.HANDLE
    kernel32.ReadFile.argtypes = [wintypes.HANDLE, ctypes.c_void_p, wintypes.DWORD,
                                  ctypes.POINTER(wintypes.DWORD), ctypes.POINTER(Overlapped)]
    kernel32.ReadFile.restype = wintypes.BOOL
    kernel32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    kernel32.WaitForSingleObject.restype = wintypes.DWORD
    kernel32.GetOverlappedResult.argtypes = [wintypes.HANDLE, ctypes.POINTER(Overlapped),
                                             ctypes.POINTER(wintypes.DWORD), wintypes.BOOL]
    kernel32.GetOverlappedResult.restype = wintypes.BOOL
    kernel32.CancelIoEx.argtypes = [wintypes.HANDLE, ctypes.POINTER(Overlapped)]
    kernel32.CancelIoEx.restype = wintypes.BOOL
    hid.HidD_GetAttributes.argtypes = [wintypes.HANDLE, ctypes.POINTER(HidAttributes)]
    hid.HidD_GetAttributes.restype = wintypes.BOOL
    hid.HidD_GetPreparsedData.argtypes = [wintypes.HANDLE, ctypes.POINTER(ctypes.c_void_p)]
    hid.HidD_GetPreparsedData.restype = wintypes.BOOL
    hid.HidD_FreePreparsedData.argtypes = [ctypes.c_void_p]
    hid.HidD_FreePreparsedData.restype = wintypes.BOOL
    hid.HidP_GetCaps.argtypes = [ctypes.c_void_p, ctypes.POINTER(HidCaps)]
    hid.HidP_GetCaps.restype = ctypes.c_long  # NTSTATUS; nonnegative is success.

    INVALID_HANDLE = ctypes.c_void_p(-1).value
    DIGCF_PRESENT = 0x02
    DIGCF_DEVICEINTERFACE = 0x10
    ERROR_NO_MORE_ITEMS = 259
    ERROR_INSUFFICIENT_BUFFER = 122
    ERROR_IO_PENDING = 997
    ERROR_OPERATION_ABORTED = 995
    FILE_SHARE_READ_WRITE = 0x01 | 0x02
    GENERIC_READ = 0x80000000
    OPEN_EXISTING = 3
    FILE_ATTRIBUTE_NORMAL = 0x80
    FILE_FLAG_OVERLAPPED = 0x40000000
    WAIT_OBJECT_0 = 0
    WAIT_TIMEOUT = 0x102

    @dataclass
    class Device:
        path: str
        vendor: int | None
        product: int | None
        usage_page: int | None
        usage: int | None
        input_length: int | None
        probe_error: int | None

    def last_error() -> int:
        return ctypes.get_last_error()

    def open_handle(path: str, access: int, overlapped: bool = False) -> int:
        flags = FILE_FLAG_OVERLAPPED if overlapped else FILE_ATTRIBUTE_NORMAL
        return kernel32.CreateFileW(path, access, FILE_SHARE_READ_WRITE, None,
                                    OPEN_EXISTING, flags, None)

    def query_device(path: str) -> Device:
        handle = open_handle(path, 0)
        if handle in (None, INVALID_HANDLE):
            return Device(path, None, None, None, None, None, last_error())
        try:
            attrs = HidAttributes()
            attrs.Size = ctypes.sizeof(HidAttributes)
            vendor = product = None
            if hid.HidD_GetAttributes(handle, ctypes.byref(attrs)):
                vendor, product = attrs.VendorID, attrs.ProductID
            prep = ctypes.c_void_p()
            page = usage = length = None
            if hid.HidD_GetPreparsedData(handle, ctypes.byref(prep)):
                try:
                    caps = HidCaps()
                    if hid.HidP_GetCaps(prep, ctypes.byref(caps)) >= 0:
                        page, usage, length = (caps.UsagePage, caps.Usage,
                                               caps.InputReportByteLength)
                finally:
                    hid.HidD_FreePreparsedData(prep)
            return Device(path, vendor, product, page, usage, length, None)
        finally:
            kernel32.CloseHandle(handle)

    def enumerate_hid() -> list[Device]:
        hid.HidD_GetHidGuid(ctypes.byref(HID_GUID))
        devices = setupapi.SetupDiGetClassDevsW(
            ctypes.byref(HID_GUID), None, None, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE)
        if devices in (None, INVALID_HANDLE):
            raise OSError(last_error(), "SetupDiGetClassDevsW failed")
        result = []
        try:
            i = 0
            while True:
                info = InterfaceData()
                info.cbSize = ctypes.sizeof(InterfaceData)
                if not setupapi.SetupDiEnumDeviceInterfaces(
                        devices, None, ctypes.byref(HID_GUID), i, ctypes.byref(info)):
                    if last_error() == ERROR_NO_MORE_ITEMS:
                        break
                    raise OSError(last_error(), "SetupDiEnumDeviceInterfaces failed")
                i += 1
                required = wintypes.DWORD(0)
                setupapi.SetupDiGetDeviceInterfaceDetailW(
                    devices, ctypes.byref(info), None, 0, ctypes.byref(required), None)
                if last_error() != ERROR_INSUFFICIENT_BUFFER or required.value < 6:
                    raise OSError(last_error(), "HID interface detail sizing failed")
                buffer = ctypes.create_string_buffer(required.value)
                # Windows SDK requires cbSize=8 on 64-bit and 6 on 32-bit.
                ctypes.cast(buffer, ctypes.POINTER(wintypes.DWORD))[0] = (
                    8 if ctypes.sizeof(ctypes.c_void_p) == 8 else 6)
                if not setupapi.SetupDiGetDeviceInterfaceDetailW(
                        devices, ctypes.byref(info), buffer, required, None, None):
                    raise OSError(last_error(), "HID interface detail failed")
                # DevicePath always starts at offset 4, even when cbSize=8.
                path = ctypes.wstring_at(ctypes.addressof(buffer) + 4)
                result.append(query_device(path))
        finally:
            setupapi.SetupDiDestroyDeviceInfoList(devices)
        return result

    def read_one(handle: int, length: int, timeout_ms: int) -> bytes | None:
        event = kernel32.CreateEventW(None, True, False, None)
        if not event:
            raise OSError(last_error(), "CreateEventW failed")
        try:
            ov = Overlapped()
            ov.hEvent = event
            buffer = ctypes.create_string_buffer(length)
            count = wintypes.DWORD(0)
            ok = kernel32.ReadFile(handle, buffer, length, ctypes.byref(count),
                                   ctypes.byref(ov))
            if not ok:
                err = last_error()
                if err != ERROR_IO_PENDING:
                    raise OSError(err, "ReadFile failed")
                wait = kernel32.WaitForSingleObject(event, timeout_ms)
                if wait == WAIT_TIMEOUT:
                    kernel32.CancelIoEx(handle, ctypes.byref(ov))
                    # Reap completion before freeing OVERLAPPED/event/buffer.
                    kernel32.GetOverlappedResult(handle, ctypes.byref(ov),
                                                 ctypes.byref(count), True)
                    return None
                if wait != WAIT_OBJECT_0:
                    kernel32.CancelIoEx(handle, ctypes.byref(ov))
                    kernel32.GetOverlappedResult(handle, ctypes.byref(ov),
                                                 ctypes.byref(count), True)
                    raise OSError(last_error(), f"Unexpected wait result {wait}")
                if not kernel32.GetOverlappedResult(handle, ctypes.byref(ov),
                                                     ctypes.byref(count), False):
                    err = last_error()
                    if err == ERROR_OPERATION_ABORTED:
                        return None
                    raise OSError(err, "GetOverlappedResult failed")
            return buffer.raw[:count.value]
        finally:
            kernel32.CloseHandle(event)

    def probe(device: Device, seconds: int, raw: bool) -> int:
        if not device.input_length or not (1 <= device.input_length <= 4096):
            print("No usable input report length; cannot attempt read.")
            return 2
        handle = open_handle(device.path, GENERIC_READ, overlapped=True)
        if handle in (None, INVALID_HANDLE):
            print(f"Cannot open HID interface for reading (Win32 error {last_error()}).")
            print("This may be Windows HID ownership/access policy, not a pairing failure.")
            return 2
        try:
            print(f"Read-only capture for {seconds}s; press buttons/touch the pad now.")
            print("Only 0xFB/0xFC are decoded; no microphone data or device paths logged.")
            decoder = ReportDecoder()
            events = 0
            deadline = time.monotonic() + seconds
            while time.monotonic() < deadline:
                remaining_ms = max(1, min(500, int((deadline - time.monotonic()) * 1000)))
                packet = read_one(handle, device.input_length, remaining_ms)
                if packet is None:
                    continue
                decoded = decoder.decode(packet)
                if decoded:
                    events += 1
                    if raw:
                        # 0xFB/0xFC only; do not print full padding or audio frames.
                        visible_len = 3 if packet[0] == 0xFB else min(19, len(packet))
                        decoded += " raw=" + packet[:visible_len].hex(" ")
                    print(decoded)
            print(f"Observed {events} decoded button/touch events.")
            if events == 0:
                print("No 0xFB/0xFC events were received; this is NOT proof of no GATT access.")
            return 0 if events else 1
        except OSError as e:
            print(f"HID read failed: {e}")
            return 2
        finally:
            kernel32.CloseHandle(handle)


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--all", action="store_true", help="Include non-Apple/unknown-vendor HID interfaces")
    p.add_argument("--index", type=int, help="Read selected index from the displayed device list")
    p.add_argument("--seconds", type=int, default=15, help="Read duration (1..120 seconds; default 15)")
    p.add_argument("--raw", action="store_true", help="Print sanitized button/touch frame hex (never audio)")
    args = p.parse_args()
    if sys.platform != "win32":
        p.error("This HID discovery tool must be run on native Windows, not WSL/Linux")
    if args.seconds < 1 or args.seconds > 120:
        p.error("--seconds must be in the range 1..120")
    try:
        devices = enumerate_hid()
    except OSError as e:
        print(f"HID enumeration failed: {e}", file=sys.stderr)
        return 2
    selected = [d for d in devices if args.all or d.vendor == APPLE_USB_VENDOR_ID]
    print(f"Found {len(devices)} HID interfaces; displaying {len(selected)}.")
    for i, d in enumerate(selected):
        vid = f"{d.vendor:04X}" if d.vendor is not None else "unknown"
        pid = f"{d.product:04X}" if d.product is not None else "unknown"
        page = f"{d.usage_page:04X}" if d.usage_page is not None else "unknown"
        usage = f"{d.usage:04X}" if d.usage is not None else "unknown"
        print(f"[{i}] VID={vid} PID={pid} usage={page}:{usage} "
              f"input_length={d.input_length or 'unknown'}"
              + (f" metadata_error={d.probe_error}" if d.probe_error else ""))
    if not selected:
        print("No candidates. Pair through Windows Settings first; then retry --all.")
        return 1
    if args.index is None:
        print("To test a candidate: py -3 hid_probe.py --all --index N --seconds 15")
        return 0
    if not 0 <= args.index < len(selected):
        p.error("--index must match a displayed device index")
    return probe(selected[args.index], args.seconds, args.raw)


if __name__ == "__main__":
    raise SystemExit(main())
