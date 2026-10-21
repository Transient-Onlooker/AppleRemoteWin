// SPDX-License-Identifier: AGPL-3.0-or-later
namespace AppleRemote;

/// <summary>
/// USB Bluetooth HCI H:2 command transport quirks. Only affects control-transfer
/// setup fields; no vendor firmware, DFU or HID-mode switching is performed.
/// </summary>
public static class UsbHciTransportQuirks
{
    /// <summary>
    /// The reference Gen-1 Realtek transport sends class/device (0x20).
    /// Reverse-engineering of Barrot's brtlinkusb.sys for VID 33FA:PID 0001/0010
    /// observed class/interface (0x21) on EP0 instead. Preserve the Gen-1
    /// behavior for every controller not explicitly documented here.
    /// Evidence: https://github.com/zhuzhuzihan/brtusb/blob/main/linux/brtusb.c
    /// Hardware validation on the owner's BR8652A12 unit is still pending.
    /// </summary>
    public static byte CommandRequestType(ushort vendorId, ushort productId) =>
        vendorId == 0x33FA && productId is 0x0001 or 0x0010
            ? (byte)0x21
            : (byte)0x20;
}
