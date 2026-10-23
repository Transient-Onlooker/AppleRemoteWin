// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

/// <summary>
/// A2854 uses an existing persistent encrypted bond: merely launching the GUI
/// must not open Barrot WinUSB, write CCCDs, send the F0 feature unlock, or
/// synthesize input. First-generation auto-start remains unchanged.
/// </summary>
public static class RemoteStartupPolicy
{
    /// <summary>
    /// In the legacy model, a plugged-in dedicated A2854 Barrot may be picked
    /// by the old generic WinUSB auto-selection even when A2854Mode is false.
    /// Treat its presence as a reason to require an explicit GUI Start click,
    /// too, rather than accidentally starting the gen-1 SMP driver on it.
    /// </summary>
    public static bool AutoStartSession(
        bool a2854Mode, bool dedicatedA2854AdapterPresent = false) =>
        !a2854Mode && !dedicatedA2854AdapterPresent;

    /// <summary>
    /// Device arrival/ESP-dongle takeover must not silently restart an
    /// explicitly started A2854 USB session.
    /// </summary>
    public static bool AllowBackgroundRestart(bool a2854Mode) => !a2854Mode;

    /// <summary>
    /// Legacy Pair/Forget commands must not reset bond state or restart an
    /// A2854 connection, which uses an independently protected bond.
    /// </summary>
    public static bool AllowLegacyPairingCommands(bool a2854Mode) => !a2854Mode;

    /// <summary>
    /// A manual one-click reconnect may replace a finished A2854 session only
    /// AFTER its driver has returned and the previous native USB cleanup has
    /// completed successfully. Never initiate automatic reconnect/pairing.
    /// </summary>
    public static bool CanOfferManualReconnect(
        bool a2854Mode, bool sessionActive, bool driverCompleted, bool usbCleanupSucceeded) =>
        a2854Mode && sessionActive && driverCompleted && usbCleanupSucceeded;
}
