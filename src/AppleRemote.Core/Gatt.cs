// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 AnthoDingo

namespace AppleRemote;

/// <summary>ATT request rejected by the device (Error Response).</summary>
public sealed class AttException(byte request, ushort handle, byte error)
    : Exception($"ATT request 0x{request:X2} rejected (handle 0x{handle:X4}, error 0x{error:X2}).")
{
    public byte Error { get; } = error;
}

public sealed record GattService(ushort Start, ushort End, byte[] Uuid);
public sealed record GattCharacteristic(ushort Declaration, byte Properties, ushort Value, byte[] Uuid);

/// <summary>
/// Minimal GATT client on a connection's ATT channel (default MTU, 23 bytes): discovery, read, write,
/// notifications. Also answers the requests the device sends us (we expose no service), otherwise it
/// would wait 30 s and drop the link.
/// </summary>
public sealed class GattClient
{
    const ushort PrimaryService = 0x2800, CharacteristicDeclaration = 0x2803;
    const byte ErrorResponse = 0x01, AttributeNotFound = 0x0A, RequestNotSupported = 0x06;

    readonly LeCentral _central;
    // ATT requests are serialized by _gate. Transport callbacks, cancellation
    // and Detach can run on unrelated threads, so their shared state is
    // published atomically, never guarded by a monitor that can stall during
    // external callbacks or shutdown.
    int _connection;
    readonly SemaphoreSlim _gate = new(1, 1);
    PendingRequest? _pending;
    Exception? _closedException;
    int _linkEnded; // ATT cancellation does not itself end HID notifications

    sealed class PendingRequest(byte request, TaskCompletionSource<byte[]> done)
    {
        public byte Request { get; } = request;
        public TaskCompletionSource<byte[]> Done { get; } = done;
    }

    Exception CloseRequests(Exception failure) =>
        Interlocked.CompareExchange(ref _closedException, failure, null) ?? failure;

    /// <summary>
    /// Safe to send a subsequent ATT request only if the previous request
    /// completed or returned an ATT Error Response. Cancellation/timeout
    /// permanently disallows more ATT requests until the link is disconnected:
    /// ATT has no transaction identifier and a late response is ambiguous.
    /// </summary>
    public bool CanSendRequests
    {
        get => Volatile.Read(ref _linkEnded) == 0 &&
            Volatile.Read(ref _closedException) is null &&
            Volatile.Read(ref _connection) != ushort.MaxValue;
    }

    public GattClient(LeCentral central, ushort connection)
    {
        _central = central;
        _connection = connection;
        central.L2capData += OnL2cap;
        central.Disconnected += OnDisconnected;
    }

    /// <summary>
    /// Attach before LE connection starts, so ATT server requests sent as soon
    /// as LinkEstablished (possibly before LTK encryption) are not lost. This
    /// is used only by A2854; legacy callers keep the fixed-handle constructor.
    /// LinkEstablished is raised synchronously from the HCI event reader before
    /// ConnectAsync completes, so the GATT listener is ready for early ATT.
    /// </summary>
    public GattClient(LeCentral central) : this(central, ushort.MaxValue)
        => central.ConnectionUpdateTrace += OnLinkEstablished;

    void OnLinkEstablished(LeConnectionUpdateTrace trace)
    {
        if (trace.Phase != LeConnectionUpdatePhase.LinkEstablished) return;
        if (Volatile.Read(ref _closedException) is null &&
            Volatile.Read(ref _linkEnded) == 0)
            Interlocked.CompareExchange(ref _connection, trace.Handle, ushort.MaxValue);
    }

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Notification or indication: (value handle, value). Raised from the transport thread.</summary>
    public event Action<ushort, byte[]>? Notification;

    /// <summary>
    /// Received ATT server request opcode (no payload/handles). Best-effort
    /// diagnostics only. Never log a raw ATT PDU or peer address.
    /// </summary>
    public event Action<byte>? PeerRequest;

    /// <summary>Call when the connection ends: stops listening to the channel.</summary>
    public void Detach()
    {
        // Publish the terminal state FIRST: unsubscribe cannot revoke an
        // event handler which has already been copied for dispatch.
        Interlocked.Exchange(ref _linkEnded, 1);
        var reason = CloseRequests(new InvalidOperationException("GATT client detached."));
        _central.L2capData -= OnL2cap;
        _central.Disconnected -= OnDisconnected;
        _central.ConnectionUpdateTrace -= OnLinkEstablished;
        // No monitor or synchronous wait for a blocked transport callback.
        // RequestAsync's own finally also clears the pending reference.
        Volatile.Read(ref _pending)?.Done.TrySetException(reason);
    }

    public static bool Is(byte[] uuid, ushort uuid16) => uuid.Length == 2 && (uuid[0] | uuid[1] << 8) == uuid16;

    public async Task<List<GattService>> DiscoverPrimaryServicesAsync(CancellationToken cancellationToken = default)
    {
        var services = new List<GattService>();
        for (ushort start = 1; ;)
        {
            byte[]? r = await RequestOrNotFoundAsync([0x10, .. U16(start), 0xFF, 0xFF, .. U16(PrimaryService)], cancellationToken);   // Read By Group Type
            if (r is null) return services;
            if (r.Length < 8 || r[1] is not (6 or 20) || (r.Length - 2) % r[1] != 0)
                throw new InvalidDataException("Malformed GATT Read By Group Type response.");
            int size = r[1];
            for (int i = 2; i < r.Length; i += size)
            {
                ushort first = U16(r, i), last = U16(r, i + 2);
                if (first < start || last < first || (services.Count > 0 && first <= services[^1].End))
                    throw new InvalidDataException("Non-monotonic GATT primary-service handle range.");
                services.Add(new GattService(first, last, r[(i + 4)..(i + size)]));
            }
            if (services[^1].End == 0xFFFF) return services;
            start = (ushort)(services[^1].End + 1);
        }
    }

    public async Task<List<GattCharacteristic>> DiscoverCharacteristicsAsync(GattService service, CancellationToken cancellationToken = default)
    {
        var characteristics = new List<GattCharacteristic>();
        for (ushort start = service.Start; start <= service.End;)
        {
            byte[]? r = await RequestOrNotFoundAsync([0x08, .. U16(start), .. U16(service.End), .. U16(CharacteristicDeclaration)], cancellationToken);
            if (r is null) break;
            if (r.Length < 9 || r[1] is not (7 or 21) || (r.Length - 2) % r[1] != 0)
                throw new InvalidDataException("Malformed GATT Read By Type response.");
            int size = r[1];
            for (int i = 2; i < r.Length; i += size)
            {
                ushort declaration = U16(r, i), value = U16(r, i + 3);
                ushort nextDeclaration = i + size < r.Length ? U16(r, i + size) : (ushort)0xFFFF;
                if (declaration < start || declaration > service.End || value <= declaration || value > service.End ||
                    (characteristics.Count > 0 && declaration <= characteristics[^1].Value) ||
                    (i + size < r.Length && value >= nextDeclaration))
                    throw new InvalidDataException("Invalid GATT characteristic declaration/value handle.");
                characteristics.Add(new GattCharacteristic(declaration, r[i + 2], value, r[(i + 5)..(i + size)]));
            }
            if (characteristics[^1].Declaration == 0xFFFF)
                return characteristics;
            start = (ushort)(characteristics[^1].Declaration + 1);
        }
        return characteristics;
    }

    /// <summary>Descriptors between two handles: (handle, UUID).</summary>
    public async Task<List<(ushort Handle, byte[] Uuid)>> DiscoverDescriptorsAsync(ushort start, ushort end, CancellationToken cancellationToken = default)
    {
        var descriptors = new List<(ushort Handle, byte[] Uuid)>();
        while (start <= end)
        {
            byte[]? r = await RequestOrNotFoundAsync([0x04, .. U16(start), .. U16(end)], cancellationToken);   // Find Information
            if (r is null) break;
            if (r.Length < 6 || r[1] is not (1 or 2))
                throw new InvalidDataException("Malformed GATT Find Information response.");
            int size = r[1] == 1 ? 4 : 18; // 16- or 128-bit UUID
            if ((r.Length - 2) % size != 0)
                throw new InvalidDataException("Misaligned GATT descriptor entries.");
            for (int i = 2; i < r.Length; i += size)
            {
                ushort handle = U16(r, i);
                if (handle < start || handle > end ||
                    (descriptors.Count > 0 && handle <= descriptors[^1].Handle))
                    throw new InvalidDataException("Non-monotonic GATT descriptor handle.");
                descriptors.Add((handle, r[(i + 2)..(i + size)]));
            }
            if (descriptors[^1].Handle == 0xFFFF) break;
            start = (ushort)(descriptors[^1].Handle + 1);
        }
        return descriptors;
    }

    public async Task<byte[]> ReadAsync(ushort handle, CancellationToken cancellationToken = default) =>
        (await RequestAsync([0x0A, .. U16(handle)], cancellationToken))[1..];

    public Task WriteAsync(ushort handle, params byte[] value) =>
        RequestAsync([0x12, .. U16(handle), .. value]);

    /// <summary>Cancellation-aware ATT Write Request; same request serialization and timeout as reads.</summary>
    public Task WriteAsync(ushort handle, CancellationToken cancellationToken, params byte[] value) =>
        RequestAsync([0x12, .. U16(handle), .. value], cancellationToken);

    async Task<byte[]?> RequestOrNotFoundAsync(byte[] pdu, CancellationToken cancellationToken)
    {
        try { return await RequestAsync(pdu, cancellationToken); }
        catch (AttException e) when (e.Error == AttributeNotFound) { return null; }   // end of a discovery
    }

    /// <summary>One request at a time (ATT rule); returns the whole response, opcode included.</summary>
    async Task<byte[]> RequestAsync(byte[] pdu, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var done = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = new PendingRequest(pdu[0], done);
            var failure = Volatile.Read(ref _closedException);
            if (failure is not null) throw failure;
            int connection = Volatile.Read(ref _connection);
            if (connection == ushort.MaxValue)
                throw new InvalidOperationException("GATT connection not established.");
            cancellationToken.ThrowIfCancellationRequested();

            // An inbound callback or terminal cleanup may run before the send.
            // Publish a single pending request, then recheck termination before
            // invoking the external transport. On shutdown no monitor can be
            // retained by this thread or any transport callback.
            Volatile.Write(ref _pending, pending);
            failure = Volatile.Read(ref _closedException);
            if (failure is not null) throw failure;
            if (Volatile.Read(ref _linkEnded) != 0)
                throw new IOException("GATT link ended before ATT request dispatch.");
            cancellationToken.ThrowIfCancellationRequested();
            _central.SendL2cap((ushort)connection, LeCentral.CidAtt, pdu);
            return await done.Task.WaitAsync(RequestTimeout, cancellationToken);
        }
        catch (Exception e) when (e is OperationCanceledException or TimeoutException)
        {
            // ATT has no transaction IDs. A late response must not complete a
            // subsequent request on this client after an interrupted exchange.
            CloseRequests(new IOException("ATT exchange interrupted; disconnect before creating a new client."));
            throw;
        }
        finally
        {
            // Do not erase a later request's identity from an old callback.
            // _gate ensures at most one producer request is in flight.
            Volatile.Write(ref _pending, null);
            _gate.Release();
        }
    }

    void OnDisconnected(ushort connection, byte reason)
    {
        if (connection != Volatile.Read(ref _connection)) return;
        Interlocked.Exchange(ref _linkEnded, 1);
        var failure = CloseRequests(new IOException($"GATT connection disconnected (reason 0x{reason:X2})."));
        Volatile.Read(ref _pending)?.Done.TrySetException(failure);
    }

    void OnL2cap(ushort connection, ushort cid, byte[] pdu)
    {
        if (cid != LeCentral.CidAtt || pdu.Length == 0) return;
        if (Volatile.Read(ref _linkEnded) != 0 ||
            connection != Volatile.Read(ref _connection)) return;
        // A cancelled/expired optional metadata request poisons only ATT
        // request sequencing. HID notifications and peer ATT requests remain
        // available until the actual link is detached/disconnected.
        PendingRequest? pending = Volatile.Read(ref _closedException) is null
            ? Volatile.Read(ref _pending) : null;
        byte opcode = pdu[0];
        if (opcode is 0x02 or 0x04 or 0x06 or 0x08 or 0x0A or 0x0C or
            0x0E or 0x10 or 0x12 or 0x16 or 0x18 or 0x20)
        {
            try { PeerRequest?.Invoke(opcode); }
            catch { /* Diagnostics must never interrupt ATT responses. */ }
        }
        switch (opcode)
        {
            case ErrorResponse when pdu.Length >= 5 && pending is { } p && p.Request == pdu[1]:
                p.Done.TrySetException(new AttException(pdu[1], U16(pdu, 2), pdu[4]));
                break;
            case 0x1B or 0x1D when pdu.Length >= 3:   // notification, indication
                if (opcode == 0x1D) _central.SendL2cap(_connection, LeCentral.CidAtt, [0x1E]);   // confirmation
                Notification?.Invoke(U16(pdu, 1), pdu[3..]);
                break;
            case 0x02:   // Exchange MTU: stay at the minimum
                _central.SendL2cap(_connection, LeCentral.CidAtt, [0x03, 23, 0]);
                break;
            case 0x04 or 0x06 or 0x08 or 0x0A or 0x0C or 0x0E or 0x10 or 0x12 or 0x16 or 0x18 or 0x20:   // request from the device
                byte error = opcode is 0x04 or 0x06 or 0x08 or 0x10 ? AttributeNotFound : RequestNotSupported;
                ushort handle = pdu.Length >= 3 ? U16(pdu, 1) : (ushort)0;
                _central.SendL2cap(_connection, LeCentral.CidAtt, [ErrorResponse, opcode, .. U16(handle), error]);
                break;
            default:
                if (pending is { Request: 0x12 } write && opcode == 0x13 && pdu.Length != 1)
                {
                    var malformedWrite = new InvalidDataException("Malformed ATT Write Response; disconnect before further requests.");
                    write.Done.TrySetException(CloseRequests(malformedWrite));
                    break;
                }
                if (pending is { } q && opcode == q.Request + 1) q.Done.TrySetResult(pdu);   // response = request + 1
                break;
        }
    }

    static byte[] U16(ushort v) => [(byte)v, (byte)(v >> 8)];
    static ushort U16(byte[] b, int i) => (ushort)(b[i] | b[i + 1] << 8);
}
