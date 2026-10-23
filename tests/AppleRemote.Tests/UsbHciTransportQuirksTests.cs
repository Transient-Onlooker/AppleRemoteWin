// SPDX-License-Identifier: AGPL-3.0-or-later
using Xunit;

namespace AppleRemote.Tests;

public class UsbHciTransportQuirksTests
{
    [Theory]
    [InlineData("USB\\VID_33FA&PID_0001\\12345", true)]
    [InlineData("\\\\?\\usb#vid_33fa&pid_0001#1234#{6B3E8C52-9D41-4F6A-8A1E-5C0D7B2F4E19}", true)]
    [InlineData("USB\\VID_8087&PID_0029\\INTEL", false)]
    [InlineData("USB\\VID_33FA&PID_0010\\OTHER_BARROT", false)]
    [InlineData("USB\\VID_2357&PID_0604\\GEN1", false)]
    [InlineData(null, false)]
    public void Dedicated_A2854_PnP_path_is_never_eligible_for_legacy_pairing(
        string? instanceId, bool expected)
    {
        Assert.Equal(expected, UsbHciTransportQuirks.IsDedicatedA2854Path(instanceId));
    }

    [Theory]
    [InlineData(0x33FA, 0x0001, 0x21)] // owner's Axxen / Barrot USB ID
    [InlineData(0x33FA, 0x0010, 0x21)] // related Barrot reverse-engineering target
    [InlineData(0x2357, 0x0604, 0x20)] // RTL8761B UB500 / Gen-1 unchanged
    [InlineData(0x8087, 0x0029, 0x20)] // built-in Intel: no Barrot quirk
    [InlineData(0x33FA, 0x1234, 0x20)] // unrelated Barrot PID is not auto-whitelisted
    [InlineData(0xFFFF, 0x0001, 0x20)] // unrelated manufacturer
    public void Uses_known_vendor_ep0_request_only(int vendorId, int productId, int expected)
    {
        Assert.Equal((byte)expected, UsbHciTransportQuirks.CommandRequestType((ushort)vendorId, (ushort)productId));
    }
}
