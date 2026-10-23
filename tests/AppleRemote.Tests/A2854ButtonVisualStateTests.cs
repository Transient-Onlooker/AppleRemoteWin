// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854ButtonVisualStateTests
{
    [Fact]
    public void Mute_press_and_release_do_not_toggle_other_buttons()
    {
        var press = A2854ButtonVisualState.FromMask(0x0080);
        Assert.True(press.Mute);
        Assert.False(press.Back);
        Assert.False(press.VolumeDown);
        Assert.False(press.CenterClick);
        Assert.Equal(default(A2854ButtonVisualState), A2854ButtonVisualState.FromMask(0));
    }

    [Fact]
    public void Direction_ring_and_center_are_independent()
    {
        var pressed = A2854ButtonVisualState.FromMask(0x0200 | 0x0400 | 0x0008);
        Assert.True(pressed.Up);
        Assert.True(pressed.Right);
        Assert.True(pressed.CenterClick);
        Assert.False(pressed.Down);
        Assert.False(pressed.Left);
        Assert.False(pressed.Mute);
    }

    [Fact]
    public void Power_and_siri_have_separate_indicators()
    {
        var buttons = A2854ButtonVisualState.FromMask(0x0010 | 0x0020);
        Assert.True(buttons.Power);
        Assert.True(buttons.Siri);
        Assert.False(buttons.PlayPause);
    }

    [Fact]
    public void All_observed_buttons_are_individually_exposed()
    {
        var buttons = A2854ButtonVisualState.FromMask(0x1FFF);
        Assert.True(buttons.Back);
        Assert.True(buttons.Tv);
        Assert.True(buttons.Siri);
        Assert.True(buttons.PlayPause);
        Assert.True(buttons.VolumeUp);
        Assert.True(buttons.VolumeDown);
        Assert.True(buttons.CenterClick);
        Assert.True(buttons.Mute);
        Assert.True(buttons.Power);
        Assert.True(buttons.Up);
        Assert.True(buttons.Right);
        Assert.True(buttons.Down);
        Assert.True(buttons.Left);
    }
}

