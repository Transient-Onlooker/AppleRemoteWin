// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Third-generation USB-C Siri Remote connection for the existing Windows GUI.
// Explicitly selected external Barrot WinUSB + already protected Windows-user bond.
// No automatic pairing, bond mutations, microphone subscription or Windows injection.

using System.Security.Cryptography;

namespace AppleRemote.Windows;

/// <summary>
/// One encrypted A2854 session. The caller owns the external UsbHci lifetime,
/// receives validated 0xFB/0xFC reports and decides whether to synthesize input.
/// A remote-initiated disconnect ends this session; restarting is an explicit user action.
/// </summary>
public sealed class A2854RemoteBle(
    HciHost host, Action<byte[]?> onReport, Action<string> onStatus,
    string? protectedBondPath = null)
{
    readonly ProtectedBondStore _bonds = new(protectedBondPath ?? ProtectedBondStore.DefaultPath);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        (string Address, byte AddressType, BondKeys Keys)? saved = null;
        GattClient? gatt = null;
        A2854HidInputMap? map = null;
        ushort? handle = null;
        bool subscribed = false;
        bool alreadyDisconnected = false;
        var disconnected = new TaskCompletionSource<(ushort Handle, byte Reason)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            // Fail closed: never fall back to SMP PairAsync or the gen-1 plaintext BondStore.
            saved = _bonds.Load();
            if (saved is null)
            {
                onStatus("A2854: no DPAPI-protected bond. Pair in the separate diagnostic first.");
                return;
            }
            if (saved.Value.AddressType != 0)
            {
                onStatus("A2854: stored random BLE address is unsupported without IRK resolution.");
                return;
            }

            var central = new LeCentral(host);
            central.Disconnected += (h, reason) =>
            {
                if (handle == h)
                    disconnected.TrySetResult((h, reason));
            };
            onStatus("A2854: initializing dedicated external Barrot WinUSB controller.");
            await central.InitializeAsync();
            cancellationToken.ThrowIfCancellationRequested();

            using (var connectWindow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectWindow.CancelAfter(TimeSpan.FromSeconds(15));
                onStatus("A2854: connecting to bonded remote; wake it with a normal button press.");
                handle = await central.ConnectAsync(saved.Value.Address,
                    connectWindow.Token, saved.Value.AddressType);
            }

            using (var encryptWindow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                encryptWindow.CancelAfter(TimeSpan.FromSeconds(12));
                await central.EncryptAsync(handle.Value, saved.Value.Keys, encryptWindow.Token);
            }
            cancellationToken.ThrowIfCancellationRequested();
            onStatus("A2854: encrypted LTK connection established.");

            gatt = new GattClient(central, handle.Value);
            using (var metadataWindow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                metadataWindow.CancelAfter(TimeSpan.FromSeconds(30));
                // The first application integration is tied to measured physical
                // handles. Firmware layout changes must stop before any write.
                map = await A2854HidCapture.DiscoverAsync(
                    gatt, metadataWindow.Token, requireObservedValueHandles: true);
            }
            cancellationToken.ThrowIfCancellationRequested();

            var localDecoder = new A2854Decoder();
            object reportLock = new();
            gatt.Notification += (valueHandle, value) =>
            {
                lock (reportLock)
                {
                    if (!A2854HidCapture.TryDecode(
                        localDecoder, map, valueHandle, value, out _, out byte id))
                        return; // drop microphone/unknown/malformed without printing raw payloads
                    onReport([id, .. value]);
                }
            };

            subscribed = true; // a partially acknowledged enable must still be cleaned up
            using (var writeWindow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                writeWindow.CancelAfter(TimeSpan.FromSeconds(12));
                await A2854HidCapture.EnableAsync(gatt, map, writeWindow.Token);
            }
            cancellationToken.ThrowIfCancellationRequested();
            onStatus(Loc.T("Status.ConnectedDirect"));

            // No auto-repair/re-pairing and no fixed polling duration. The existing
            // app's Stop button cancels, or the peer can voluntarily sleep (0x13).
            try
            {
                var ended = await disconnected.Task.WaitAsync(cancellationToken);
                alreadyDisconnected = true;
                onStatus($"A2854: remote disconnected (HCI 0x{ended.Reason:X2}); use Stop/Start to reconnect.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // User stopped the session. Cleanup runs below.
            }

            // Cleanup on normal exit is in finally so it also handles ATT failures.
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            onStatus("A2854: connection stopped.");
        }
        catch (OperationCanceledException)
        {
            onStatus("A2854: connection or operation timed out. Existing bond was preserved.");
        }
        catch (Exception e)
        {
            // Exception messages must not contain raw key material or peer addresses.
            // Emit exception type and stage-neutral status only.
            onStatus($"A2854: session ended ({e.GetType().Name}); bond unchanged.");
        }
        finally
        {
            try
            {
                if (gatt is not null && map is not null && subscribed &&
                    handle is not null && !disconnected.Task.IsCompleted)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var result = await A2854HidCapture.DisableWithResultAsync(gatt, map, cleanup.Token);
                    onStatus(result.ButtonDisabled && result.TouchDisabled
                        ? "A2854: both input notifications disabled."
                        : "A2854: notification cleanup partial; disconnect follows.");
                }
            }
            catch (Exception)
            {
                onStatus("A2854: notification cleanup interrupted; closing link.");
            }

            gatt?.Detach();

            // A valid BLE connection is the only resource we may close here.
            // This never changes a Windows adapter driver or the protected bond.
            if (!alreadyDisconnected && handle is { } connectedHandle &&
                !disconnected.Task.IsCompleted)
            {
                try
                {
                    // Avoid attempting a disconnect if a peer closure races us.
                    // LeCentral is in scope here only if initialization succeeded;
                    // defer host cleanup to a separate method below.
                    await DisconnectHostAsync(host, connectedHandle, disconnected.Task);
                }
                catch (Exception)
                {
                    onStatus("A2854: link cleanup incomplete; check adapter before restarting.");
                }
            }

            if (saved is { } bond)
            {
                CryptographicOperations.ZeroMemory(bond.Keys.Ltk);
                CryptographicOperations.ZeroMemory(bond.Keys.Rand);
            }
            onReport([]); // release held click/key states on stop, peer sleep or error
        }
    }

    /// <summary>
    /// This method creates no new session: a direct HCI disconnect request
    /// on the already established handle, with a bounded terminal event wait.
    /// </summary>
    private static async Task DisconnectHostAsync(
        HciHost host, ushort handle, Task<(ushort Handle, byte Reason)> disconnected)
    {
        await host.CommandAsync(HciOpcode.Disconnect,
            (byte)handle, (byte)(handle >> 8), 0x13);
        await disconnected.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
