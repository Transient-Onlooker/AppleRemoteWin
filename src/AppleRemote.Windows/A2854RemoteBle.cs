// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Third-generation USB-C Siri Remote connection for the existing Windows GUI.
// Explicitly selected external Barrot WinUSB + already protected Windows-user bond.
// No automatic pairing, bond mutations, microphone subscription or Windows injection.

using System.Diagnostics;
using System.Security.Cryptography;

namespace AppleRemote.Windows;

/// <summary>
/// One encrypted A2854 session. The caller owns the external UsbHci lifetime,
/// receives validated 0xFB/0xFC reports and decides whether to synthesize input.
/// A remote-initiated disconnect ends this session; restarting is an explicit user action.
/// </summary>
public sealed class A2854RemoteBle(
    HciHost host, Action<byte[]?> onReport, Action<string> onStatus,
    string? protectedBondPath = null, Action? onTerminalCleanup = null,
    Action? onReady = null, Action<int>? onBattery = null)
{
    readonly ProtectedBondStore _bonds = new(protectedBondPath ?? ProtectedBondStore.DefaultPath);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        (string Address, byte AddressType, BondKeys Keys)? saved = null;
        GattClient? gatt = null;
        A2854HidInputMap? map = null;
        LeCentral? central = null;
        ushort? handle = null;
        int provisionalHandle = -1; // observed at LE LinkEstablished, before ConnectAsync resumes
        bool subscribed = false;
        bool alreadyDisconnected = false;
        string phase = "load-bond";
        long connectedAt = 0, lastInputAt = 0;
        int buttonFrames = 0, touchFrames = 0;
        int encryptionCompleted = 0, earlyAttRequests = 0;
        var disconnected = new TaskCompletionSource<(ushort Handle, byte Reason)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var inputGate = new A2854InputGate(onReport, onTerminalCleanup);
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

            central = new LeCentral(host);
            // Register the ATT receiver BEFORE initiating LE connection.
            // The remote can send its MTU/attribute request immediately after
            // LinkEstablished, before EncryptAsync resumes. Previously no GATT
            // listener existed until after encryption, so a peer request could
            // time out after ~30 seconds despite continuous HID notifications.
            gatt = new GattClient(central);
            gatt.PeerRequest += opcode =>
            {
                if (Volatile.Read(ref encryptionCompleted) != 0) return;
                int count = Interlocked.Increment(ref earlyAttRequests);
                if (count <= 8)
                    onStatus($"A2854: received early ATT request opcode=0x{opcode:X2} " +
                        "before encryption completed (handled by GATT).");
            };
            central.ConnectionUpdateTrace += trace =>
            {
                // HCI events can arrive on the USB reader thread *before* the
                // awaited ConnectAsync continuation assigns the session handle.
                // This central owns a single dedicated A2854 link. Keep early
                // diagnostics (tagged "unbound"), but reject unrelated handles
                // once the actual session handle is known.
                ushort? activeHandle = handle;
                if (activeHandle is ushort selected && selected != trace.Handle) return;
                if (trace.Phase == LeConnectionUpdatePhase.LinkEstablished)
                {
                    Volatile.Write(ref provisionalHandle, trace.Handle);
                    Interlocked.CompareExchange(ref connectedAt, Stopwatch.GetTimestamp(), 0);
                }
                long start = Interlocked.Read(ref connectedAt);
                string elapsed = start == 0 ? "unknown" :
                    $"{Stopwatch.GetElapsedTime(start).TotalSeconds:F2}s";
                string status = trace.Status is byte code ? $"0x{code:X2}" : "n/a";
                onStatus($"A2854: LE connection parameters {trace.Phase}; " +
                    $"association={(activeHandle.HasValue ? "bound" : "unbound")} " +
                    $"elapsed={elapsed} min={trace.MinInterval} max={trace.MaxInterval} " +
                    $"latency={trace.Latency} timeout={trace.SupervisionTimeout} " +
                    $"status={status}.");
            };
            central.Disconnected += (h, reason) =>
            {
                // A short-lived link can end on the transport thread before
                // ConnectAsync resumes and publishes the handle. Use only the
                // handle learned from this central's LinkEstablished event.
                if (handle != h && Volatile.Read(ref provisionalHandle) != h) return;
                // Invalidate input before completing the disconnect task. Any
                // in-flight notification is ordered before the final release;
                // a resumed callback after this point cannot re-press a button.
                try { inputGate.Close(); }
                catch (Exception e)
                {
                    try { onStatus($"A2854: input release callback failed ({e.GetType().Name})."); }
                    catch { /* do not hide the terminal HCI event */ }
                }
                finally { disconnected.TrySetResult((h, reason)); }
            };
            onStatus(Loc.T("Status.WaitBonded"));
            onStatus("A2854: initializing dedicated external Barrot WinUSB controller.");
            phase = "initialize-controller";
            await central.InitializeAsync();
            cancellationToken.ThrowIfCancellationRequested();

            using (var connectWindow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectWindow.CancelAfter(TimeSpan.FromSeconds(15));
                onStatus("A2854: connecting to bonded remote; wake it with a normal button press.");
                phase = "connect";
                handle = await central.ConnectAsync(saved.Value.Address,
                    connectWindow.Token, saved.Value.AddressType);
                // The LE Connection Complete event records a more precise start
                // before the async ConnectAsync continuation. Keep that timestamp.
                Interlocked.CompareExchange(ref connectedAt, Stopwatch.GetTimestamp(), 0);
            }
            if (disconnected.Task.IsCompleted)
            {
                // Never command encryption for a link already closed by the peer.
                // This avoids disguising a real early disconnect as a stale-LTK
                // HCI 0x2019/0x3E error; no automatic retry or pairing follows.
                alreadyDisconnected = true;
                var endedEarly = await disconnected.Task;
                onStatus($"A2854: early peer disconnect before encryption reason=0x{endedEarly.Reason:X2}; bond unchanged.");
                return;
            }

            using (var encryptWindow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                encryptWindow.CancelAfter(TimeSpan.FromSeconds(12));
                phase = "encrypt";
                await central.EncryptIfConnectedAsync(handle.Value, saved.Value.Keys, encryptWindow.Token);
            }
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref encryptionCompleted, 1);
            onStatus("A2854: encrypted LTK connection established.");
            onStatus($"A2854: early ATT request count={Volatile.Read(ref earlyAttRequests)}.");

            using (var metadataWindow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                metadataWindow.CancelAfter(TimeSpan.FromSeconds(30));
                phase = "discover-hid";
                // The first application integration is tied to measured physical
                // handles. Firmware layout changes must stop before any write.
                // DiscoverAsync already enforces all three observed value handles.
                map = await A2854HidCapture.DiscoverAsync(gatt, metadataWindow.Token);
            }
            cancellationToken.ThrowIfCancellationRequested();

            A2854HidInputMap inputMap = map ??
                throw new InvalidDataException("A2854 HID report map was not resolved.");
            var localDecoder = new A2854Decoder();
            object decoderLock = new();
            gatt.Notification += (valueHandle, value) =>
            {
                lock (decoderLock)
                {
                    if (inputGate.IsClosed) return;
                    if (!A2854HidCapture.TryDecode(
                        localDecoder, inputMap, valueHandle, value, out _, out byte id))
                        return; // drop microphone/unknown/malformed without printing raw payloads
                    Interlocked.Exchange(ref lastInputAt, Stopwatch.GetTimestamp());
                    if (id == A2854Decoder.ButtonsReportId) Interlocked.Increment(ref buttonFrames);
                    else if (id == A2854Decoder.TouchReportId) Interlocked.Increment(ref touchFrames);
                    inputGate.TryForward([id, .. value]);
                }
            };

            subscribed = true; // a partially acknowledged enable must still be cleaned up
            using (var writeWindow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                writeWindow.CancelAfter(TimeSpan.FromSeconds(12));
                phase = "enable-hid";
                await A2854HidCapture.EnableAsync(gatt, map, writeWindow.Token);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // "Connected" is only awarded after the *encrypted* link is up
            // and both button/touch HID notifications have been enabled.
            // A successful HCI connection alone is not enough to allow the
            // UI to offer a restart following failed initialization.
            onReady?.Invoke();
            onStatus(Loc.T("Status.ConnectedDirect"));
            phase = "observe";

            // The HID link is already usable. Battery metadata is optional:
            // read only the standard 0x180F / 0x2A19 characteristic, with a
            // short deadline. Never enable a CCCD, guess a handle, touch the
            // bond, or abort an otherwise healthy input session for battery.
            GattClient? batteryGatt = null;
            try
            {
                // An optional metadata read can time out and poison that
                // ATT client's request state. Keep the original HID GATT
                // client isolated so its notifications AND Stop-time CCCD
                // disable still work if battery metadata is unavailable.
                batteryGatt = new GattClient(central, handle.Value,
                    handlesServerTraffic: false);
                using var batteryWindow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                batteryWindow.CancelAfter(TimeSpan.FromSeconds(5));
                int? battery = await A2854Battery.ReadOnceAsync(batteryGatt, batteryWindow.Token);
                if (battery is int level && !inputGate.IsClosed && !cancellationToken.IsCancellationRequested)
                    onBattery?.Invoke(level);
                else if (!cancellationToken.IsCancellationRequested)
                    onStatus("A2854: battery level unavailable (standard readable characteristic absent or invalid).");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                onStatus("A2854: battery read timed out; HID input remains active.");
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested)
            {
                // Log only the exception type; ATT exceptions may contain
                // sensitive identifiers. Battery failure is never fatal.
                onStatus($"A2854: battery read unavailable ({e.GetType().Name}); HID input remains active.");
            }
            finally
            {
                batteryGatt?.Detach();
            }

            // No auto-repair/re-pairing and no fixed polling duration. The existing
            // app's Stop button cancels, or the peer can voluntarily sleep (0x13).
            try
            {
                var ended = await disconnected.Task.WaitAsync(cancellationToken);
                alreadyDisconnected = true;
                onStatus(Loc.T("Status.DisconnectedReason", ended.Reason));
                long connectionStart = Interlocked.Read(ref connectedAt);
                double durationSeconds = connectionStart == 0 ? 0 :
                    Stopwatch.GetElapsedTime(connectionStart).TotalSeconds;
                long lastReportTimestamp = Interlocked.Read(ref lastInputAt);
                string lastReportAge = lastReportTimestamp == 0
                    ? "none" : $"{Stopwatch.GetElapsedTime(lastReportTimestamp).TotalSeconds:F1}s ago";
                onStatus($"A2854: peer disconnection 0x{ended.Reason:X2} " +
                    $"({HciDisconnectReason.Describe(ended.Reason)}); " +
                    $"connected={durationSeconds:F1}s FB={Volatile.Read(ref buttonFrames)} " +
                    $"FC={Volatile.Read(ref touchFrames)} last-input={lastReportAge}; " +
                    "host Stop/CCCD disable not yet verified.");
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
            onStatus(Loc.T("Status.Unreachable"));
            onStatus("A2854: connection or operation timed out. Existing bond was preserved.");
        }
        catch (HciException e)
        {
            // Distinguish controller Command Status/Complete rejection from
            // Encryption Change failure. All fields are fixed enum values;
            // do not expose any peer address, LTK or raw HCI payload.
            onStatus($"A2854: HCI failure source={e.FailureSource}.");
            onStatus(Loc.T("Status.Unreachable"));
            onStatus($"A2854: HCI error phase={phase} opcode=0x{e.Opcode:X4} " +
                $"status=0x{e.Status:X2}; bond unchanged.");
        }
        catch (Exception e)
        {
            // No raw exception message: it may contain a peer address.
            onStatus(Loc.T("Status.Unreachable"));
            onStatus($"A2854: session error phase={phase} type={e.GetType().Name}; bond unchanged.");
        }
        finally
        {
            // Seal the input stream *before* any potentially slow ATT cleanup.
            // The release marker is synchronized with every notification.
            try { inputGate.Close(); }
            catch (Exception e)
            {
                try { onStatus($"A2854: final input release callback failed ({e.GetType().Name})."); }
                catch { }
            }
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
            if (!alreadyDisconnected && central is not null &&
                handle is { } connectedHandle && !disconnected.Task.IsCompleted)
            {
                try
                {
                    onStatus("A2854: host disconnect requested after notification cleanup.");
                    var result = await A2854HidCapture.DisconnectAsync(
                        central, connectedHandle, disconnected.Task,
                        eventTimeout: TimeSpan.FromSeconds(5),
                        requireLiveLink: true);
                    onStatus(result.CommandStatusSucceeded
                        ? "A2854: host disconnect command accepted."
                        : "A2854: host disconnect command not confirmed.");
                    onStatus(result.LocalHostVerified
                        ? "A2854: host disconnect completed with normal local reason 0x16."
                        : $"A2854: disconnect ended with HCI reason 0x{result.Reason:X2} (host cleanup unverified).");
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
            // inputGate.Close already delivered exactly one release marker.
        }
    }

}
