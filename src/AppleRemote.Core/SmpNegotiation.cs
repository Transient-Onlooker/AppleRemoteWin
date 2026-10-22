// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 AnthoDingo

using System.Threading.Channels;

namespace AppleRemote;

/// <summary>
/// Bluetooth Core Specification SMP opcodes (Vol 3, Part H, 3.3).
/// </summary>
public static class SmpOpcode
{
    public const byte PairingRequest = 0x01;
    public const byte PairingResponse = 0x02;
    public const byte PairingConfirm = 0x03;
    public const byte PairingRandom = 0x04;
    public const byte PairingFailed = 0x05;
    public const byte EncryptionInformation = 0x06;
    public const byte MasterIdentification = 0x07;
    public const byte IdentityInformation = 0x08;
    public const byte IdentityAddressInformation = 0x09;
    public const byte SigningInformation = 0x0A;
    public const byte SecurityRequest = 0x0B;
    public const byte PairingPublicKey = 0x0C;
    public const byte PairingDhKeyCheck = 0x0D;
    public const byte PairingKeypressNotification = 0x0E;
}

/// <summary>
/// Bluetooth Core Specification SMP Pairing Failed reasons (Vol 3, Part H, 3.5.5).
/// </summary>
public static class SmpFailedReason
{
    public const byte PasskeyEntryFailed = 0x01;
    public const byte OobNotAvailable = 0x02;
    public const byte AuthenticationRequirements = 0x03;
    public const byte ConfirmValueFailed = 0x04;
    public const byte PairingNotSupported = 0x05;
    public const byte EncryptionKeySize = 0x06;
    public const byte CommandNotSupported = 0x07;
    public const byte UnspecifiedReason = 0x08;
    public const byte RepeatedAttempts = 0x09;
    public const byte InvalidParameters = 0x0A;
    public const byte DhKeyCheckFailed = 0x0B;
    public const byte NumericComparisonFailed = 0x0C;
    public const byte BrEdrPairingInProgress = 0x0D;
    public const byte CrossTransportKeyNotAllowed = 0x0E;
    public const byte KeyRejected = 0x0F;

    public static string Describe(byte reason) => reason switch
    {
        PasskeyEntryFailed => "Passkey Entry Failed (0x01)",
        OobNotAvailable => "OOB Not Available (0x02)",
        AuthenticationRequirements => "Authentication Requirements (0x03)",
        ConfirmValueFailed => "Confirm Value Failed (0x04)",
        PairingNotSupported => "Pairing Not Supported (0x05)",
        EncryptionKeySize => "Encryption Key Size (0x06)",
        CommandNotSupported => "Command Not Supported (0x07)",
        UnspecifiedReason => "Unspecified Reason (0x08)",
        RepeatedAttempts => "Repeated Attempts (0x09)",
        InvalidParameters => "Invalid Parameters (0x0A)",
        DhKeyCheckFailed => "DHKey Check Failed (0x0B)",
        NumericComparisonFailed => "Numeric Comparison Failed (0x0C)",
        BrEdrPairingInProgress => "BR/EDR Pairing in Progress (0x0D)",
        CrossTransportKeyNotAllowed => "Cross-transport Key Derivation/Generation not allowed (0x0E)",
        KeyRejected => "Key Rejected (0x0F)",
        _ => $"Unknown SMP failure reason (0x{reason:X2})",
    };
}

/// <summary>
/// Bluetooth Core Specification SMP IO Capabilities (Vol 3, Part H, 3.5.1).
/// </summary>
public static class SmpIoCapability
{
    public const byte DisplayOnly = 0x00;
    public const byte DisplayYesNo = 0x01;
    public const byte KeyboardOnly = 0x02;
    public const byte NoInputNoOutput = 0x03;
    public const byte KeyboardDisplay = 0x04;

    public static string Describe(byte io) => io switch
    {
        DisplayOnly => "DisplayOnly (0x00)",
        DisplayYesNo => "DisplayYesNo (0x01)",
        KeyboardOnly => "KeyboardOnly (0x02)",
        NoInputNoOutput => "NoInputNoOutput (0x03)",
        KeyboardDisplay => "KeyboardDisplay (0x04)",
        _ => $"Unknown IO Capability (0x{io:X2})",
    };
}

/// <summary>
/// Bluetooth Core Specification SMP Key Distribution masks (Vol 3, Part H, 3.6.1).
/// </summary>
public static class SmpKeyDistribution
{
    public const byte EncKey = 0x01;
    public const byte IdKey = 0x02;
    public const byte SignKey = 0x04;
    public const byte LinkKey = 0x08;

    public static string Describe(byte mask)
    {
        var list = new List<string>(4);
        if ((mask & EncKey) != 0) list.Add("EncKey(LTK)");
        if ((mask & IdKey) != 0) list.Add("IdKey(IRK)");
        if ((mask & SignKey) != 0) list.Add("SignKey(CSRK)");
        if ((mask & LinkKey) != 0) list.Add("LinkKey");
        return list.Count == 0 ? "None (0x00)" : $"{string.Join("|", list)} (0x{mask:X2})";
    }
}

/// <summary>
/// SMP Pairing Request parameters (Vol 3, Part H, 3.5.1).
/// </summary>
public sealed record SmpPairingRequest(
    byte IoCapability = SmpIoCapability.NoInputNoOutput,
    byte OobDataFlag = 0x00,
    byte AuthReq = 0x01, // Bonding = 0x01
    byte MaxKeySize = 16,
    byte InitiatorKeyDistribution = 0x00,
    byte ResponderKeyDistribution = SmpKeyDistribution.EncKey)
{
    public byte[] ToBytes() => [
        SmpOpcode.PairingRequest,
        IoCapability,
        OobDataFlag,
        AuthReq,
        MaxKeySize,
        InitiatorKeyDistribution,
        ResponderKeyDistribution
    ];

    public static SmpPairingRequest Default => new();
}

/// <summary>
/// SMP Pairing Response parameters (Vol 3, Part H, 3.5.2).
/// </summary>
public sealed record SmpPairingResponse(
    byte IoCapability,
    byte OobDataFlag,
    byte AuthReq,
    byte MaxKeySize,
    byte InitiatorKeyDistribution,
    byte ResponderKeyDistribution)
{
    public bool BondingRequested => (AuthReq & 0x03) == 0x01;
    public bool MitmRequested => (AuthReq & 0x04) != 0;
    public bool SecureConnectionsRequested => (AuthReq & 0x08) != 0;
    public bool KeypressRequested => (AuthReq & 0x10) != 0;
    public bool Ct2Requested => (AuthReq & 0x20) != 0;

    public bool HasEncKey => (ResponderKeyDistribution & SmpKeyDistribution.EncKey) != 0;
    public bool HasIdKey => (ResponderKeyDistribution & SmpKeyDistribution.IdKey) != 0;
    public bool HasSignKey => (ResponderKeyDistribution & SmpKeyDistribution.SignKey) != 0;
    public bool HasLinkKey => (ResponderKeyDistribution & SmpKeyDistribution.LinkKey) != 0;

    public static SmpPairingResponse Parse(ReadOnlySpan<byte> pdu)
    {
        // SMP Pairing Response is a fixed seven-byte PDU. Reject both
        // truncated and overlong frames instead of silently ignoring a suffix.
        if (pdu.Length != 7)
            throw new ArgumentException($"Pairing Response PDU has invalid length ({pdu.Length}, expected 7).", nameof(pdu));
        if (pdu[0] != SmpOpcode.PairingResponse)
            throw new ArgumentException($"Unexpected opcode 0x{pdu[0]:X2} (expected 0x02).", nameof(pdu));

        byte io = pdu[1];
        byte oob = pdu[2];
        byte authReq = pdu[3];
        byte maxKeySize = pdu[4];
        byte initKeyDist = pdu[5];
        byte respKeyDist = pdu[6];

        if (maxKeySize is < 7 or > 16)
            throw new InvalidDataException($"Invalid Maximum Encryption Key Size {maxKeySize} (must be between 7 and 16).");

        return new SmpPairingResponse(io, oob, authReq, maxKeySize, initKeyDist, respKeyDist);
    }
}

/// <summary>
/// The parsed result of an SMP pairing negotiation attempt.
/// </summary>
public abstract record SmpNegotiationResult
{
    public sealed record Success(SmpPairingResponse Response) : SmpNegotiationResult;
    public sealed record Failed(byte ReasonCode, string Description) : SmpNegotiationResult;
}

/// <summary>
/// Dedicated, safe SMP negotiator: issues a Pairing Request on L2CAP CID 0x0006
/// and awaits the peer's Pairing Response or Pairing Failed without continuing
/// to confirm values, random keys, Diffie-Hellman checks, or encryption.
/// </summary>
public static class SmpNegotiator
{
    public static async Task<SmpNegotiationResult> NegotiateAsync(
        LeCentral central,
        ushort handle,
        SmpPairingRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        request ??= SmpPairingRequest.Default;
        var tcs = new TaskCompletionSource<SmpNegotiationResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnL2cap(ushort h, ushort cid, byte[] pdu)
        {
            if (h != handle || cid != LeCentral.CidSmp || pdu.Length == 0) return;

            byte opcode = pdu[0];
            if (opcode == SmpOpcode.PairingResponse)
            {
                try
                {
                    var response = SmpPairingResponse.Parse(pdu);
                    tcs.TrySetResult(new SmpNegotiationResult.Success(response));
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }
            else if (opcode == SmpOpcode.PairingFailed)
            {
                byte reason = pdu.Length > 1 ? pdu[1] : (byte)0x08;
                tcs.TrySetResult(new SmpNegotiationResult.Failed(reason, SmpFailedReason.Describe(reason)));
            }
        }

        central.L2capData += OnL2cap;
        try
        {
            central.SendL2cap(handle, LeCentral.CidSmp, request.ToBytes());

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            return await tcs.Task.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("SMP Pairing Response timed out (no response within 30 seconds).");
        }
        finally
        {
            central.L2capData -= OnL2cap;
        }
    }
}
