// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

/// <summary>
/// A2854 uses an existing persistent encrypted bond: merely launching the GUI
/// must not open Barrot WinUSB, write CCCDs, send the F0 feature unlock, or
/// synthesize input. First-generation auto-start remains unchanged.
/// </summary>
public static class RemoteStartupPolicy
{
    public static bool AutoStartSession(bool a2854Mode) => !a2854Mode;

    /// <summary>
    /// Device arrival/ESP-dongle takeover must not silently restart an
    /// explicitly started A2854 USB session.
    /// </summary>
    public static bool AllowBackgroundRestart(bool a2854Mode) => !a2854Mode;
}
