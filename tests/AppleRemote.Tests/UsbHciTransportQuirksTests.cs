// SPDX-License-Identifier: AGPL-3.0-or-later
using Xunit;

namespace AppleRemote.Tests;

public class UsbHciTransportQuirksTests
{
    [Theory]
    [InlineData(0x33FA, 0x0001, 0x21)] // owner's Axxen / Barrot USB ID
    [InlineData(0x33FA, 0x0010, 0x21)] // related Barrot reverse-engineering target
    [InlineData(0x2357, 0x0604, 0x20)] // RTL8761B UB500 / Gen-1 unchanged
    [InlineData(0x8087, 0x0029, 0x20)] // built-in Intel: no Barrot quirk
    [InlineData(0x33FA, 0x1234, 0x20)] // unrelated Barrot PID is not auto-whitelisted
    [InlineData(0xFFFF, 0x0001, 0x20)] // unrelated manufacturer
    public void Uses_known_vendor_ep0_request_only(ushort vendorId, ushort productId, byte expected)
    {
        Assert.Equal(expected, UsbHciTransportQuirks.CommandRequestType(vendorId, productId));
    }
}
