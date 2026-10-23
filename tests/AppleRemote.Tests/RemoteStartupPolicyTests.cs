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
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A2854_requires_manual_Start_legacy_session_autostarts(
        bool a2854Mode, bool expected)
    {
        Assert.Equal(expected, RemoteStartupPolicy.AutoStartSession(a2854Mode));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Dongle_and_PnP_notifications_cannot_auto_restart_A2854(
        bool a2854Mode, bool expected)
    {
        Assert.Equal(expected, RemoteStartupPolicy.AllowBackgroundRestart(a2854Mode));
    }

    [Fact]
    public void Saved_A2854_mode_is_not_an_authorization_to_open_USB_or_write_GATT()
    {
        const bool selectedInPersistedPreferences = true;
        Assert.False(RemoteStartupPolicy.AutoStartSession(selectedInPersistedPreferences));
        Assert.False(RemoteStartupPolicy.AllowBackgroundRestart(selectedInPersistedPreferences));
    }
}
