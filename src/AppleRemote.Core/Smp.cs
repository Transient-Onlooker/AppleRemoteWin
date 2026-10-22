// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 AnthoDingo

using System.Security.Cryptography;
using System.Threading.Channels;

namespace AppleRemote;

/// <summary>Pairing rejected or interrupted (Pairing Failed, wrong confirm value, key not distributed…).</summary>
public sealed class SmpException(string message) : Exception(message);

/// <summary>
/// Spec functions (Core, vol. 3, part H, 2.2) on little-endian values, as they travel:
/// e = AES-128 (on big-endian values, hence the reversals), c1 = confirm value, s1 = short-term key (STK).
/// </summary>
public static class SmpCrypto
{
    public static byte[] E(byte[] key, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key.Reverse().ToArray();
        return aes.EncryptEcb(data.Reverse().ToArray(), PaddingMode.None).Reverse().ToArray();
    }

    /// <param name="preq">Pairing Request (7 octets, opcode compris).</param>
    /// <param name="pres">Pairing Response (7 octets).</param>
    /// <param name="ia">Initiator address (us), 6 bytes; <paramref name="iat"/>: 0 public, 1 random.</param>
    public static byte[] C1(byte[] k, byte[] r, byte[] preq, byte[] pres, byte iat, byte[] ia, byte rat, byte[] ra)
    {
        byte[] p1 = [iat, rat, .. preq, .. pres];
        byte[] p2 = [.. ra, .. ia, 0, 0, 0, 0];
        return E(k, Xor(E(k, Xor(r, p1)), p2));
    }

    /// <summary>STK = e(k, r1' || r2'), r' = 64 bits de poids faible.</summary>
    public static byte[] S1(byte[] k, byte[] r1, byte[] r2) => E(k, [.. r2[..8], .. r1[..8]]);

    static byte[] Xor(byte[] a, byte[] b) => a.Zip(b, (x, y) => (byte)(x ^ y)).ToArray();
}

/// <summary>Non-secret progress stages for a single bounded SMP legacy Just Works pairing.</summary>
public enum SmpPairingStage
{
    PairingRequest,
    PairingResponseValidated,
    PairingConfirm,
    PairingRandom,
    PeerConfirmVerified,
    StartEncryption,
    LinkEncrypted,
    ReceiveEncryptionInformation,
    ReceiveMasterIdentification,
    Complete,
}

/// <summary>
/// LE "legacy" "Just Works" pairing (no display or keyboard, like the remote, Bluetooth 4.0), initiator side:
/// confirm exchange, encryption with the short-term key, then reception of the LTK distributed by the remote.
/// We distribute no key: we always reconnect as central, with the remote's.
/// </summary>
public static class SmpPairing
{
    const byte PairingRequest = 0x01, PairingResponse = 0x02, PairingConfirm = 0x03, PairingRandom = 0x04, PairingFailed = 0x05,
        EncryptionInformation = 0x06, MasterIdentification = 0x07;
    const byte NoInputNoOutput = 0x03, Bonding = 0x01, EncKey = 0x01;

    /// <summary>
    /// Performs a single legacy Just Works pairing. This method NEVER stores bonds,
    /// explores GATT, or prints keys. Callers own the returned key arrays and must
    /// decide how to protect/dispose of them. A caller token bounds the entire run.
    /// </summary>
    public static async Task<BondKeys> PairAsync(LeCentral central, ushort handle,
        byte[] localAddress, byte[] remoteAddress, byte remoteAddressType = 0,
        CancellationToken cancellationToken = default,
        Action<SmpPairingStage>? onStage = null,
        bool requireObservedA2854Profile = false)
    {
        if (remoteAddressType is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(remoteAddressType));
        if (localAddress.Length != 6 || remoteAddress.Length != 6)
            throw new ArgumentException("Local and peer BLE addresses must each be 6 bytes.");
        cancellationToken.ThrowIfCancellationRequested();

        var inbox = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        void OnData(ushort h, ushort cid, byte[] pdu)
        {
            if (h == handle && cid == LeCentral.CidSmp && pdu.Length > 0)
                inbox.Writer.TryWrite(pdu);
        }
        void Send(byte[] pdu)
        {
            cancellationToken.ThrowIfCancellationRequested();
            central.SendL2cap(handle, LeCentral.CidSmp, pdu);
        }
        void Stage(SmpPairingStage stage)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onStage?.Invoke(stage);
        }

        async Task<byte[]> Expect(byte opcode, int length)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                while (true)
                {
                    byte[] pdu = await inbox.Reader.ReadAsync(timeout.Token);
                    if (pdu[0] == PairingFailed)
                        throw new SmpException($"Pairing rejected by the remote (reason 0x{(pdu.Length > 1 ? pdu[1] : 0):X2}).");
                    if (pdu[0] == opcode)
                    {
                        if (pdu.Length != length)
                            throw new SmpException($"Malformed SMP opcode 0x{opcode:X2} length ({pdu.Length}, expected {length}).");
                        return pdu;
                    }
                    // Unsolicited Security Request and unrelated PDUs are ignored.
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new SmpException($"Timed out waiting for SMP opcode 0x{opcode:X2}.");
            }
        }

        byte[]? tk = null, mrand = null, srand = null, sconfirm = null, stk = null;
        central.L2capData += OnData;
        try
        {
            // Previously verified physical A2854 request: NoInputNoOutput,
            // Bonding=1, SC=0, 16-byte key, receive EncKey from peripheral.
            byte[] preq = SmpPairingRequest.Default.ToBytes();
            Stage(SmpPairingStage.PairingRequest);
            Send(preq);
            byte[] pres = await Expect(PairingResponse, 7);
            if (requireObservedA2854Profile)
                ValidateLegacyJustWorksResponse(pres);
            else
            {
                // Preserve compatibility with older remotes distributing more than
                // just EncKey. Still reject impossible key sizes and SC pairing
                // unsupported by this legacy c1/s1 implementation.
                SmpPairingResponse offered = SmpPairingResponse.Parse(pres);
                if (!offered.HasEncKey)
                    throw new SmpException("The remote distributes no encryption key.");
                if (offered.SecureConnectionsRequested)
                    throw new SmpException("LE Secure Connections is unsupported by legacy pairing.");
            }
            Stage(SmpPairingStage.PairingResponseValidated);

            tk = new byte[16]; // Legacy Just Works: zero temporary key.
            mrand = RandomNumberGenerator.GetBytes(16);
            byte[] Confirm(byte[] random) => SmpCrypto.C1(tk, random, preq, pres, 0,
                localAddress, remoteAddressType, remoteAddress);

            Stage(SmpPairingStage.PairingConfirm);
            Send([PairingConfirm, .. Confirm(mrand)]);
            byte[] remoteConfirm = await Expect(PairingConfirm, 17);
            sconfirm = remoteConfirm[1..17];
            Stage(SmpPairingStage.PairingRandom);
            Send([PairingRandom, .. mrand]);
            byte[] remoteRandom = await Expect(PairingRandom, 17);
            srand = remoteRandom[1..17];
            byte[] actual = Confirm(srand);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(actual, sconfirm))
                {
                    Send([PairingFailed, 0x04]); // Confirm Value Failed
                    throw new SmpException("Remote confirm did not match (legacy Just Works).");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(actual);
            }
            Stage(SmpPairingStage.PeerConfirmVerified);

            stk = SmpCrypto.S1(tk, srand, mrand);
            int keySize = pres[4]; // Validated to 7..16, physical A2854 advertises 16.
            CryptographicOperations.ZeroMemory(stk.AsSpan(keySize));
            Stage(SmpPairingStage.StartEncryption);
            await central.EncryptAsync(handle, new BondKeys(stk, 0, new byte[8]), cancellationToken);
            Stage(SmpPairingStage.LinkEncrypted);

            Stage(SmpPairingStage.ReceiveEncryptionInformation);
            byte[] ltkMessage = await Expect(EncryptionInformation, 17);
            Stage(SmpPairingStage.ReceiveMasterIdentification);
            byte[] master = await Expect(MasterIdentification, 11);

            // Return fresh in-memory key arrays: no BondStore, files, JSON or logging.
            byte[] ltk = ltkMessage[1..17];
            byte[] rand = master[3..11];
            ushort ediv = (ushort)(master[1] | master[2] << 8);
            CryptographicOperations.ZeroMemory(ltkMessage);
            CryptographicOperations.ZeroMemory(master);
            Stage(SmpPairingStage.Complete);
            return new BondKeys(ltk, ediv, rand);
        }
        finally
        {
            central.L2capData -= OnData;
            if (tk is not null) CryptographicOperations.ZeroMemory(tk);
            if (mrand is not null) CryptographicOperations.ZeroMemory(mrand);
            if (srand is not null) CryptographicOperations.ZeroMemory(srand);
            if (sconfirm is not null) CryptographicOperations.ZeroMemory(sconfirm);
            if (stk is not null) CryptographicOperations.ZeroMemory(stk);
            inbox.Writer.TryComplete();
            while (inbox.Reader.TryRead(out byte[]? pending))
                CryptographicOperations.ZeroMemory(pending);
        }
    }

    /// <summary>
    /// Refuse unexpected security requirements instead of silently proceeding
    /// with an unauthenticated legacy pairing under incompatible feature flags.
    /// </summary>
    public static void ValidateLegacyJustWorksResponse(ReadOnlySpan<byte> pdu)
    {
        SmpPairingResponse response = SmpPairingResponse.Parse(pdu);
        if (response.IoCapability != SmpIoCapability.NoInputNoOutput || response.OobDataFlag != 0)
            throw new SmpException("Legacy Just Works requires peer NoInputNoOutput and no OOB data.");
        if (response.AuthReq != 0x01)
            throw new SmpException("Unexpected AuthReq; A2854 diagnostic requires observed Bonding-only legacy profile.");
        if (response.MaxKeySize != 16)
            throw new SmpException("Unexpected encryption key size; A2854 diagnostic requires observed 16 bytes.");
        if (!response.BondingRequested || !response.HasEncKey)
            throw new SmpException("Peer did not negotiate bonding and responder encryption-key distribution.");
        if (response.InitiatorKeyDistribution != 0 ||
            response.ResponderKeyDistribution != SmpKeyDistribution.EncKey)
            throw new SmpException("Unexpected SMP key-distribution requirements; no silent fallback.");
    }
}
