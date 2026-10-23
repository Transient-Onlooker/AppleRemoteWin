// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854TouchProjectionTests
{
    [Fact]
    public void Signed_zero_projects_near_center()
    {
        var p = A2854TouchProjection.Map(0, 0);
        Assert.InRange(p.X, 179, 181);
        Assert.InRange(p.Y, 179, 181);
    }

    [Fact]
    public void Physical_contact_fixture_uses_new_signed_domain()
    {
        // Observed A2854 physical 0xFC contact: x=-922, y=-675.
        var p = A2854TouchProjection.Map(-922, -675);
        Assert.InRange(p.X, 0, 179);
        Assert.InRange(p.Y, 181, 360);
    }

    [Fact]
    public void Y_grows_up_on_remote_but_down_on_screen()
    {
        var low = A2854TouchProjection.Map(0, -1000);
        var high = A2854TouchProjection.Map(0, 1000);
        Assert.True(high.Y < low.Y);
    }

    [Fact]
    public void X_grows_right()
    {
        Assert.True(A2854TouchProjection.Map(1000, 0).X >
                    A2854TouchProjection.Map(-1000, 0).X);
    }

    [Theory]
    [InlineData(-2048, -2048)]
    [InlineData(2047, 2047)]
    [InlineData(-4000, 4000)]
    [InlineData(4095, -4096)]
    public void Extreme_or_out_of_range_contacts_remain_inside_round_pad(int x, int y)
    {
        var p = A2854TouchProjection.Map(x, y);
        double dx = p.X - A2854TouchProjection.PadSize / 2;
        double dy = p.Y - A2854TouchProjection.PadSize / 2;
        double radius = A2854TouchProjection.PadSize / 2 - A2854TouchProjection.DotSize / 2;
        Assert.True(dx * dx + dy * dy <= radius * radius + 1e-6);
        Assert.InRange(p.X, 0, A2854TouchProjection.PadSize);
        Assert.InRange(p.Y, 0, A2854TouchProjection.PadSize);
    }
}

