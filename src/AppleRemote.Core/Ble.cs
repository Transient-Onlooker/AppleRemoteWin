// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 AnthoDingo

using System.Text.Json;
using System.Text.Json.Nodes;

namespace AppleRemote;

/// <summary>Bluetooth address: text "14:9D:99:26:BF:CC" (most significant byte first) &lt;-&gt; 6 little-endian bytes (HCI).</summary>
public static class BdAddr
{
    public static byte[] Parse(string text)
    {
        byte[] bytes = Convert.FromHexString(text.Replace(":", ""));
        if (bytes.Length != 6) throw new FormatException($"Adresse Bluetooth invalide : {text}");
        Array.Reverse(bytes);
        return bytes;
    }

    public static string Format(ReadOnlySpan<byte> littleEndian) =>
        string.Join(":", littleEndian[..6].ToArray().Reverse().Select(b => b.ToString("X2")));
}

/// <summary>Keys of a "legacy" LE pairing: LTK distributed by the remote, with its EDIV and Rand.</summary>
public sealed record BondKeys(byte[] Ltk, ushort Ediv, byte[] Rand);

/// <summary>Pairing keys per address, in a JSON file (hexadecimal values, HCI byte order).</summary>
public sealed class BondStore(string path)
{
    // JsonNode rather than reflection-based serialisation: also works in AOT / single-file publishing.
    public BondKeys? Load(string address) =>
        Read()[address] is JsonObject e
            ? new BondKeys(Convert.FromHexString((string)e["ltk"]!), (ushort)e["ediv"]!, Convert.FromHexString((string)e["rand"]!))
            : null;

    /// <summary>Paired addresses (a single remote in practice).</summary>
    public IReadOnlyList<string> Addresses => Read().Select(e => e.Key).ToList();

    /// <summary>Forgets all bonds.</summary>
    public void Clear() => File.Delete(path);

    /// <summary>Stores the address's keys; null forgets them.</summary>
    public void Save(string address, BondKeys? keys)
    {
        JsonObject all = Read();
        all.Remove(address);
        if (keys is not null)
            all[address] = new JsonObject
            {
                ["ltk"] = Convert.ToHexString(keys.Ltk), ["ediv"] = keys.Ediv, ["rand"] = Convert.ToHexString(keys.Rand),
            };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, all.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    JsonObject Read() => File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new() : new();
}

/// <summary>Peer address and HCI address type (0 = public, 1 = random).</summary>
public readonly record struct LePeer(string Address, byte AddressType);

/// <summary>Stages of the BLE connection-parameter update negotiation (no peer identity or key data).</summary>
public enum LeConnectionUpdatePhase { LinkEstablished, RequestReceived, CommandAccepted, CommandRejected, CommandFailed, Completed }

/// <summary>
/// Non-sensitive BLE timing/parameter-update diagnostic. Initial link timing
/// and a completed HCI event have no L2CAP request identifier (Identifier = 0).
/// </summary>
public readonly record struct LeConnectionUpdateTrace(
    ushort Handle, byte Identifier, LeConnectionUpdatePhase Phase,
    ushort MinInterval, ushort MaxInterval, ushort Latency,
    ushort SupervisionTimeout, byte? Status = null);

/// <summary>
/// LE "central" role on top of <see cref="HciHost"/>: controller initialisation, connection to a public address,
/// encryption with known keys, disconnection tracking, L2CAP frames. One connection at a time.
/// </summary>
public sealed class LeCentral
{
    const byte EvtDisconnectionComplete = 0x05, EvtEncryptionChange = 0x08, EvtLeMeta = 0x3E, LeConnectionComplete = 0x01,
        LeAdvertisingReport = 0x02, LeEnhancedConnectionComplete = 0x0A, LeExtendedAdvertisingReport = 0x0D;
    public const ushort CidAtt = 0x0004, CidSignaling = 0x0005, CidSmp = 0x0006;

    readonly HciHost _hci;
    readonly object _connectionGate = new();
    readonly HashSet<ushort> _liveHandles = new();
    readonly bool _respondToSignaling;
    readonly List<byte> _rx = new();   // trame L2CAP en cours de reconstitution
    TaskCompletionSource<ushort>? _connecting;
    (Func<byte[], bool> Match, TaskCompletionSource<LePeer> Found, bool AllowRandom)? _scan;
    (ushort Handle, TaskCompletionSource Done)? _encrypting;

    /// <summary>When false, do not send automatic L2CAP signaling responses (link-only diagnostics).</summary>
    public LeCentral(HciHost hci, bool respondToSignaling = true)
    {
        _hci = hci;
        _respondToSignaling = respondToSignaling;
        _hci.Event += OnEvent;
        _hci.AclData += OnAcl;
    }

    /// <summary>Our public address (6 little-endian bytes), read by <see cref="InitializeAsync"/>.</summary>
    public byte[] LocalAddress { get; private set; } = new byte[6];

    /// <summary>
    /// Bluetooth 5 controller (LE Extended Advertising feature), read by <see cref="InitializeAsync"/>: scanning and connection
    /// then use the extended commands, as Bumble does. The Realtek RTL8761B accepts the legacy LE Create Connection
    /// but never connects (observed 2026-10-12, TP-Link UB500); older controllers (Qualcomm QCA61x4A) only have the legacy ones.
    /// </summary>
    public bool Extended { get; private set; }

    /// <summary>Disconnection: (handle, HCI reason). Raised from the transport thread.</summary>
    public event Action<ushort, byte>? Disconnected;

    /// <summary>
    /// LE L2CAP update request, HCI Command Status, and HCI LE Update Complete.
    /// Best-effort diagnostics only: listeners must never affect the radio link.
    /// </summary>
    public event Action<LeConnectionUpdateTrace>? ConnectionUpdateTrace;

    /// <summary>Advertising report received while scanning: (address, 6 little-endian bytes; RSSI in dBm; AD data). Transport thread.</summary>
    public event Action<byte[], sbyte, byte[]>? Advertisement;

    /// <summary>AD structures of advertising data: (type, value). Stops at the first malformed one.</summary>
    public static IEnumerable<(byte Type, byte[] Value)> AdStructures(byte[] data)
    {
        for (int i = 0; i + 1 < data.Length && data[i] > 0 && i + 1 + data[i] <= data.Length; i += data[i] + 1)
            yield return (data[i + 1], data[(i + 2)..(i + 1 + data[i])]);
    }

    /// <summary>Complete L2CAP frame received (except the signalling channel, handled here): (handle, channel, data).</summary>
    public event Action<ushort, ushort, byte[]>? L2capData;

    public void SendL2cap(ushort handle, ushort cid, byte[] payload) =>
        _hci.SendAcl(handle, [(byte)payload.Length, (byte)(payload.Length >> 8), (byte)cid, (byte)(cid >> 8), .. payload]);

    public async Task InitializeAsync()
    {
        await RealtekFirmware.LoadIfNeededAsync(_hci);
        // Default mask + "LE Meta" (bit 61, no LE event without it) + "Encryption Key Refresh" (bit 47).
        await _hci.CommandAsync(HciOpcode.SetEventMask, BitConverter.GetBytes(0x2000_9FFF_FFFF_FFFFUL));
        byte[] features = await _hci.CommandAsync(HciOpcode.LeReadLocalFeatures);
        Extended = features.Length > 2 && (features[2] & 0x10) != 0;   // feature bit 12
        // Legacy LE events (bits 0..4), Enhanced Connection Complete (bit 9)
        // and Extended Advertising Report (bit 12). Without bit 9, a controller
        // that reports only enhanced completions can leave ConnectAsync waiting
        // despite a successful link. Keep the legacy-completion bit for compatibility.
        if (Extended) await _hci.CommandAsync(HciOpcode.LeSetEventMask, BitConverter.GetBytes(0x121FUL));
        LocalAddress = (await _hci.CommandAsync(HciOpcode.ReadBdAddr))[1..7];
    }

    /// <summary>
    /// Connects as soon as the peer (public or random address) shows up: advertising in pairing mode, or a button press
    /// after sleep. Waits indefinitely; cancellation abandons the attempt. Returns the connection handle.
    /// </summary>
    public async Task<ushort> ConnectAsync(string address, CancellationToken ct = default, byte peerAddressType = 0)
    {
        if (peerAddressType is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(peerAddressType));
        ct.ThrowIfCancellationRequested();
        var connecting = new TaskCompletionSource<ushort>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref _connecting, connecting, null) is not null)
            throw new InvalidOperationException("An LE connection attempt is already in progress.");
        byte[] scan = [0x60, 0x00, 0x30, 0x00];   // scan: 60 ms interval, 30 ms window
        byte[] link =
        [
            0x0C, 0x00, 0x18, 0x00,     // intervalle de connexion 15–30 ms
            0x00, 0x00,                 // latence 0
            0xC8, 0x00,                 // supervision 2 s
            0x00, 0x00, 0x00, 0x00,     // event length: up to the controller
        ];
        // No filter list: use the observed peer address and its type, including a temporary random address.
        try
        {
            if (Extended)
                await _hci.CommandAsync(HciOpcode.LeExtendedCreateConnection,
                    [0x00, 0x00, peerAddressType, .. BdAddr.Parse(address), 0x01 /* 1M PHY */, .. scan, .. link]);
            else
                await _hci.CommandAsync(HciOpcode.LeCreateConnection, [.. scan, 0x00, peerAddressType, .. BdAddr.Parse(address), 0x00, .. link]);
            try
            {
                return await connecting.Task.WaitAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (connecting.Task.IsCompleted)
                {
                    await DisconnectIfConnectedAsync(connecting.Task);
                    throw new OperationCanceledException(ct);
                }

                // LE Create Connection Cancel applies to both legacy and extended create.
                // Await its Command Complete before returning so a caller's cleanup/reset
                // cannot overtake the cancel command in HCI command serialization.
                try { await _hci.CommandAsync(HciOpcode.LeCreateConnectionCancel); }
                catch (HciException e) when (e.Status is 0x02 or 0x0C)
                {
                    // Unknown Connection Identifier / Command Disallowed can mean the
                    // create completion won the race with cancellation. Drain that event.
                }

                try
                {
                    // The Controller reports the create procedure's final status after the
                    // cancel response. A successful late completion must be disconnected.
                    await DisconnectIfConnectedAsync(connecting.Task.WaitAsync(TimeSpan.FromSeconds(2)));
                }
                catch (TimeoutException)
                {
                    // The caller owns the controller-specific last-resort reset if the
                    // completion event never arrives.
                }
                catch (HciException e) when ((e.Opcode is HciOpcode.LeCreateConnection or HciOpcode.LeExtendedCreateConnection) && e.Status == 0x02)
                {
                    // Unknown Connection Identifier is the expected completion after a
                    // successful cancellation; no ACL link was created.
                }

                throw new OperationCanceledException(ct);
            }
        }
        finally
        {
            Interlocked.CompareExchange(ref _connecting, null, connecting);
        }
    }

    async Task DisconnectIfConnectedAsync(Task<ushort> connection)
    {
        ushort handle;
        try { handle = await connection; }
        catch (HciException e) when ((e.Opcode is HciOpcode.LeCreateConnection or HciOpcode.LeExtendedCreateConnection) && e.Status == 0x02)
        {
            return;
        }

        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnDisconnected(ushort disconnectedHandle, byte _)
        {
            if (disconnectedHandle == handle) disconnected.TrySetResult();
        }

        Disconnected += OnDisconnected;
        try
        {
            await DisconnectAsync(handle);
            await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            Disconnected -= OnDisconnected;
        }
    }

    /// <summary>Legacy scan: first matching public-address advertiser (preserves gen-1 behavior).</summary>
    public async Task<string> ScanAsync(Func<byte[], bool> match, CancellationToken ct = default) =>
        (await ScanPeerAsync(match, allowRandom: false, ct)).Address;

    /// <summary>
    /// Scan for a matching peer. Optional random-address acceptance is needed for BLE privacy addresses;
    /// the address type must be forwarded to the connection and SMP pairing procedure.
    /// </summary>
    public async Task<LePeer> ScanPeerAsync(Func<byte[], bool> match, bool allowRandom, CancellationToken ct = default)
    {
        var found = new TaskCompletionSource<LePeer>(TaskCreationOptions.RunContinuationsAsynchronously);
        _scan = (match, found, allowRandom);
        try
        {
            // Active (the scan response may carry data), 60 ms / 30 ms, our public address, no filtering.
            if (Extended)
            {
                await _hci.CommandAsync(HciOpcode.LeSetExtendedScanParameters, 0x00, 0x00, 0x01 /* 1M PHY */, 0x01, 0x60, 0x00, 0x30, 0x00);
                await _hci.CommandAsync(HciOpcode.LeSetExtendedScanEnable, 0x01, 0x00, 0, 0, 0, 0);
            }
            else
            {
                await _hci.CommandAsync(HciOpcode.LeSetScanParameters, 0x01, 0x60, 0x00, 0x30, 0x00, 0x00, 0x00);
                await _hci.CommandAsync(HciOpcode.LeSetScanEnable, 0x01, 0x00);
            }
            return await found.Task.WaitAsync(ct);
        }
        finally
        {
            _scan = null;
            try
            {
                if (Extended) await _hci.CommandAsync(HciOpcode.LeSetExtendedScanEnable, 0, 0, 0, 0, 0, 0);
                else await _hci.CommandAsync(HciOpcode.LeSetScanEnable, 0x00, 0x00);
            }
            catch (Exception) { /* controller gone */ }
        }
    }

    /// <summary>Original gen-1 encryption flow. Protocol and behavior unchanged.</summary>
    public Task EncryptAsync(ushort handle, BondKeys keys, CancellationToken cancellationToken = default) =>
        EncryptCoreAsync(handle, keys, requireActiveLink: false, cancellationToken);

    /// <summary>
    /// A2854-safe encryption: check liveness atomically with HCI dispatch,
    /// even if CommandAsync was queued behind another controller command.
    /// </summary>
    public Task EncryptIfConnectedAsync(ushort handle, BondKeys keys, CancellationToken cancellationToken = default) =>
        EncryptCoreAsync(handle, keys, requireActiveLink: true, cancellationToken);

    async Task EncryptCoreAsync(ushort handle, BondKeys keys, bool requireActiveLink,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var encrypting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _encrypting = (handle, encrypting);
        try
        {
            byte[] parameters =
                [(byte)handle, (byte)(handle >> 8), .. keys.Rand, (byte)keys.Ediv, (byte)(keys.Ediv >> 8), .. keys.Ltk];
            if (requireActiveLink)
                await _hci.CommandIfAsync(HciOpcode.LeEnableEncryption, _connectionGate,
                    () => _liveHandles.Contains(handle) && !cancellationToken.IsCancellationRequested,
                    parameters);
            else
                await _hci.CommandAsync(HciOpcode.LeEnableEncryption, parameters);
            await encrypting.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
        finally
        {
            _encrypting = null;
        }
    }

    public Task DisconnectAsync(ushort handle) =>
        _hci.CommandAsync(HciOpcode.Disconnect, (byte)handle, (byte)(handle >> 8), 0x13);   // 0x13: terminated by the remote user

    void OnAcl(ushort handle, int pb, byte[] data)
    {
        byte[] frame;
        lock (_rx)
        {
            if (pb != 0b01) _rx.Clear();   // start of frame (otherwise: continuation of the previous one)
            _rx.AddRange(data);
            if (_rx.Count < 4 || _rx.Count < 4 + (_rx[0] | _rx[1] << 8)) return;
            frame = _rx.ToArray();
            _rx.Clear();
        }
        ushort cid = (ushort)(frame[2] | frame[3] << 8);
        byte[] payload = frame[4..(4 + (frame[0] | frame[1] << 8))];
        if (cid == CidSignaling)
        {
            if (_respondToSignaling) OnSignaling(handle, payload);
        }
        else L2capData?.Invoke(handle, cid, payload);
    }

    /// <summary>
    /// LE signalling: we accept the remote's connection parameter update request (otherwise it may drop the link)
    /// and reject the rest ("command not understood").
    /// </summary>
    void OnSignaling(ushort handle, byte[] p)
    {
        if (p.Length < 4) return;
        byte code = p[0], id = p[1];
        if (code == 0x12 && p.Length >= 12)   // Connection Parameter Update Request
        {
            var trace = new LeConnectionUpdateTrace(
                handle, id, LeConnectionUpdatePhase.RequestReceived,
                (ushort)(p[4] | p[5] << 8), (ushort)(p[6] | p[7] << 8),
                (ushort)(p[8] | p[9] << 8), (ushort)(p[10] | p[11] << 8));
            ReportConnectionUpdate(trace);
            // Preserve existing behavior. Acceptance at L2CAP does not mean
            // the controller also accepted/applied the connection update.
            SendL2cap(handle, CidSignaling, [0x13, id, 0x02, 0x00, 0x00, 0x00]);
            _ = ApplyConnectionUpdateAsync(trace, p[4..12]);
        }
        else if (code is not (0x01 or 0x13))   // neither a reject nor a response: a request we don't handle
        {
            SendL2cap(handle, CidSignaling, [0x01, id, 0x02, 0x00, 0x00, 0x00]);
        }
    }

    async Task ApplyConnectionUpdateAsync(LeConnectionUpdateTrace request, byte[] parameters)
    {
        try
        {
            await _hci.CommandAsync(HciOpcode.LeConnectionUpdate,
                [(byte)request.Handle, (byte)(request.Handle >> 8), .. parameters,
                 0x00, 0x00, 0x00, 0x00]);
            ReportConnectionUpdate(request with { Phase = LeConnectionUpdatePhase.CommandAccepted });
        }
        catch (HciException e)
        {
            // Previously this fire-and-forget exception was unobserved.
            ReportConnectionUpdate(request with
            {
                Phase = LeConnectionUpdatePhase.CommandRejected, Status = e.Status
            });
        }
        catch (Exception)
        {
            // Includes command timeout or transport failure; never expose raw
            // HCI payload, peer address, bond data, or exception message.
            ReportConnectionUpdate(request with { Phase = LeConnectionUpdatePhase.CommandFailed });
        }
    }

    void ReportConnectionUpdate(LeConnectionUpdateTrace value)
    {
        try { ConnectionUpdateTrace?.Invoke(value); }
        catch { /* best-effort telemetry must not disturb HCI transport */ }
    }

    void OnAdvertisement((Func<byte[], bool> Match, TaskCompletionSource<LePeer> Found, bool AllowRandom) scan, byte addressType, byte[] address,
        sbyte rssi, byte[] data)
    {
        Advertisement?.Invoke(address, rssi, data);
        if ((addressType is 0x00 or 0x01) && (addressType == 0x00 || scan.AllowRandom) && scan.Match(data))
            scan.Found.TrySetResult(new LePeer(BdAddr.Format(address), addressType));
    }

    void OnEvent(byte code, byte[] p)
    {
        switch (code)
        {
            case EvtLeMeta when p.Length >= 2 && p[0] == LeAdvertisingReport && _scan is { } scan:
                for (int i = 2, n = 0; n < p[1] && i + 9 <= p.Length; n++)   // type, address type, address, length, data, RSSI
                {
                    int length = p[i + 8];
                    if (i + 10 + length > p.Length) break;   // RSSI included
                    OnAdvertisement(scan, p[i + 1], p[(i + 2)..(i + 8)], (sbyte)p[i + 9 + length], p[(i + 9)..(i + 9 + length)]);
                    i += 10 + length;
                }
                break;
            // Event type (2), address type, address, PHYs (2), SID, TX power, RSSI, periodic interval (2), direct address (7), length, data
            case EvtLeMeta when p.Length >= 2 && p[0] == LeExtendedAdvertisingReport && _scan is { } scan:
                for (int i = 2, n = 0; n < p[1] && i + 24 <= p.Length; n++)
                {
                    int length = p[i + 23];
                    if (i + 24 + length > p.Length) break;
                    // Address type 2 = public identity (resolved by the controller): public too.
                    OnAdvertisement(scan, (byte)(p[i + 2] & ~2), p[(i + 3)..(i + 9)], (sbyte)p[i + 13], p[(i + 24)..(i + 24 + length)]);
                    i += 24 + length;
                }
                break;
            case EvtLeMeta when p.Length >= 2 && (p[0] is LeConnectionComplete or LeEnhancedConnectionComplete):
                CompleteConnection(p);
                break;
            case EvtLeMeta when p.Length >= 10 && p[0] == 0x03: // LE Connection Update Complete
                ushort updatedHandle = (ushort)(p[2] | p[3] << 8);
                ushort interval = (ushort)(p[4] | p[5] << 8);
                ReportConnectionUpdate(new LeConnectionUpdateTrace(
                    updatedHandle, 0, LeConnectionUpdatePhase.Completed,
                    interval, interval, (ushort)(p[6] | p[7] << 8),
                    (ushort)(p[8] | p[9] << 8), p[1]));
                break;
            case EvtEncryptionChange when p.Length >= 4 && _encrypting is { } encryption &&
                encryption.Handle == (ushort)(p[1] | p[2] << 8):
                if (p[0] != 0) encryption.Done.TrySetException(
                    new HciException(HciOpcode.LeEnableEncryption, p[0],
                        HciFailureSource.EncryptionChange));
                else if (p[3] == 0) encryption.Done.TrySetException(new InvalidOperationException("Encryption disabled by the remote."));
                else encryption.Done.TrySetResult();
                break;
            case EvtDisconnectionComplete when p.Length >= 4 && p[0] == 0:
                ushort endedHandle = (ushort)(p[1] | p[2] << 8);
                lock (_connectionGate) _liveHandles.Remove(endedHandle);
                if (_encrypting is { } pendingEncryption && pendingEncryption.Handle == endedHandle)
                    pendingEncryption.Done.TrySetException(new InvalidOperationException($"Disconnected during encryption (reason 0x{p[3]:X2})."));
                Disconnected?.Invoke(endedHandle, p[3]);
                break;
        }
    }

    void CompleteConnection(byte[] p)
    {
        TaskCompletionSource<ushort>? connecting = Volatile.Read(ref _connecting);
        if (connecting is null) return;

        bool enhanced = p[0] == LeEnhancedConnectionComplete;
        ushort opcode = enhanced ? HciOpcode.LeExtendedCreateConnection : HciOpcode.LeCreateConnection;
        if (p[1] != 0)
        {
            connecting.TrySetException(new HciException(opcode, p[1]));
            return;
        }

        // Subevent plus full success parameters: 19 octets for legacy and 31
        // for Enhanced Connection Complete v1 (longer v2 events are accepted).
        int minimumLength = enhanced ? 31 : 19;
        if (p.Length < minimumLength)
        {
            connecting.TrySetException(new InvalidDataException("Truncated LE Connection Complete event."));
            return;
        }

        ushort handle = (ushort)(p[2] | p[3] << 8);
        lock (_connectionGate) _liveHandles.Add(handle);
        // Initial LE Connection Complete contains the controller-accepted
        // interval, latency and supervision timeout. Preserve these numerical
        // timings without logging the peer address, RPA or encryption data.
        int intervalOffset = enhanced ? 24 : 12;
        ushort interval = (ushort)(p[intervalOffset] | p[intervalOffset + 1] << 8);
        ushort latency = (ushort)(p[intervalOffset + 2] | p[intervalOffset + 3] << 8);
        ushort timeout = (ushort)(p[intervalOffset + 4] | p[intervalOffset + 5] << 8);
        ReportConnectionUpdate(new LeConnectionUpdateTrace(
            handle, 0, LeConnectionUpdatePhase.LinkEstablished,
            interval, interval, latency, timeout, 0));
        connecting.TrySetResult(handle);
    }
}
