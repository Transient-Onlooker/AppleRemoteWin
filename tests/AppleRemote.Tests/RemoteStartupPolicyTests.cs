// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

/// <summary>
/// Keeps the WPF initial launch and device-arrival paths passive whenever
/// third-generation A2854 is selected, without altering legacy auto-start.
/// </summary>
public sealed class RemoteStartupPolicyTests
{
    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)] // legacy mode must not implicitly choose Barrot
    [InlineData(false, false, true)] // previous gen-1 auto-start is unchanged otherwise
    public void A2854_and_dedicated_Barrot_require_manual_Start(
        bool a2854Mode, bool dedicatedBarrotPresent, bool expected)
    {
        Assert.Equal(expected,
            RemoteStartupPolicy.AutoStartSession(a2854Mode, dedicatedBarrotPresent));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Dongle_and_PnP_notifications_cannot_auto_restart_A2854(
        bool a2854Mode, bool expected)
    {
        Assert.Equal(expected, RemoteStartupPolicy.AllowBackgroundRestart(a2854Mode));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Legacy_pair_and_forget_commands_are_disabled_in_A2854_mode(bool a2854Mode, bool expected)
    {
        Assert.Equal(expected, RemoteStartupPolicy.AllowLegacyPairingCommands(a2854Mode));
    }

    [Fact]
    public void Driver_change_completion_is_not_A2854_Start_authorization()
    {
        Assert.False(RemoteStartupPolicy.AllowBackgroundRestart(a2854Mode: true));
        Assert.True(RemoteStartupPolicy.AllowBackgroundRestart(a2854Mode: false));
    }

    [Theory]
    [InlineData(true, true, true, true, true, true)]
    [InlineData(true, true, true, true, false, false)] // never reached encrypted HID
    [InlineData(true, true, false, true, true, false)] // driver still running
    [InlineData(true, true, true, false, true, false)] // native USB cleanup not done
    [InlineData(true, false, true, true, true, false)] // logical session already stopped
    [InlineData(false, true, true, true, true, false)] // legacy remote
    [InlineData(true, true, true, false, false, false)]
    public void Manual_reconnect_requires_finished_driver_and_successful_native_cleanup(
        bool a2854Mode, bool hasSession, bool driverDone, bool usbClean,
        bool hidReady, bool expected)
    {
        Assert.Equal(expected, RemoteStartupPolicy.CanOfferManualReconnect(
            a2854Mode, hasSession, driverDone, usbClean, hidReady));
    }

    [Fact]
    public void Saved_A2854_mode_is_not_an_authorization_to_open_USB_or_write_GATT()
    {
        const bool selectedInPersistedPreferences = true;
        Assert.False(RemoteStartupPolicy.AutoStartSession(selectedInPersistedPreferences));
        Assert.False(RemoteStartupPolicy.AllowBackgroundRestart(selectedInPersistedPreferences));
    }
}
