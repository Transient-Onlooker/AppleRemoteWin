// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Security.Cryptography;
using Xunit;

namespace AppleRemote.Tests;

/// <summary>Pair-only gate regression tests: keys stay in memory, ATT/GATT is not touched.</summary>
public sealed class SmpPairOnlyTests
{
    [Fact]
    public async Task Legacy_pair_only_completes_with_fake_remote_without_GATT_or_bond_file()
    {
        var remote = new FakeRemote();
        var central = new LeCentral(remote.Hci);
        await central.InitializeAsync();

        LePeer peer = await central.ScanPeerAsync(
            SiriRemoteBle.IsA2854PairingAdvertisement, allowRandom: true);
        ushort handle = await central.ConnectAsync(peer.Address, peerAddressType: peer.AddressType);
        var progress = new List<SmpPairingStage>();
        BondKeys? keys = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            keys = await SmpPairing.PairAsync(central, handle, central.LocalAddress,
                BdAddr.Parse(peer.Address), peer.AddressType, cts.Token, progress.Add,
                requireObservedA2854Profile: true);

            Assert.Equal(FakeRemote.Bond.Ltk, keys.Ltk);
            Assert.Equal(FakeRemote.Bond.Ediv, keys.Ediv);
            Assert.Equal(FakeRemote.Bond.Rand, keys.Rand);
            Assert.Equal(1, remote.Pairings);
            Assert.Empty(remote.Writes); // There was no ATT discovery, subscription or feature unlock.
            Assert.Contains(SmpPairingStage.LinkEncrypted, progress);
            Assert.Equal(SmpPairingStage.Complete, progress[^1]);
        }
        finally
        {
            if (keys is not null)
            {
                CryptographicOperations.ZeroMemory(keys.Ltk);
                CryptographicOperations.ZeroMemory(keys.Rand);
            }
        }
    }

    [Theory]
    [InlineData(0x03, 0x00, 0x01, 16, 0x00, 0x01, true)] // The real A2854 response.
    [InlineData(0x03, 0x00, 0x09, 16, 0x00, 0x01, false)] // SC flag.
    [InlineData(0x03, 0x00, 0x11, 16, 0x00, 0x01, false)] // Keypress flag; outside observed profile.
    [InlineData(0x03, 0x00, 0x21, 16, 0x00, 0x01, false)] // CT2 flag; outside observed profile.
    [InlineData(0x03, 0x00, 0x01, 12, 0x00, 0x01, false)] // Valid legacy key size, not observed 16.
    [InlineData(0x03, 0x00, 0x41, 16, 0x00, 0x01, false)] // Reserved AuthReq bit.
    [InlineData(0x03, 0x00, 0x05, 16, 0x00, 0x01, false)] // MITM flag.
    [InlineData(0x03, 0x01, 0x01, 16, 0x00, 0x01, false)] // OOB required.
    [InlineData(0x00, 0x00, 0x01, 16, 0x00, 0x01, false)] // DisplayOnly.
    [InlineData(0x03, 0x00, 0x00, 16, 0x00, 0x01, false)] // No bonding.
    [InlineData(0x03, 0x00, 0x01, 16, 0x00, 0x00, false)] // No responder LTK.
    [InlineData(0x03, 0x00, 0x01, 16, 0x01, 0x01, false)] // Unsolicited host key distribution.
    [InlineData(0x03, 0x00, 0x01, 16, 0x00, 0x03, false)] // IRK without an identity plan.
    public void Validator_only_accepts_observed_legacy_just_works_parameters(
        byte io, byte oob, byte auth, byte keySize, byte initDist,
        byte respDist, bool accepted)
    {
        byte[] response = [SmpOpcode.PairingResponse, io, oob, auth, keySize, initDist, respDist];
        if (accepted) SmpPairing.ValidateLegacyJustWorksResponse(response);
        else Assert.Throws<SmpException>(() => SmpPairing.ValidateLegacyJustWorksResponse(response));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(17)]
    public void Validator_rejects_impossible_key_size(byte keySize)
    {
        byte[] response = [0x02, 0x03, 0x00, 0x01, keySize, 0x00, 0x01];
        Assert.Throws<InvalidDataException>(() => SmpPairing.ValidateLegacyJustWorksResponse(response));
    }

    [Fact]
    public async Task Cancellation_while_waiting_for_pairing_response_exits_without_keys_or_encryption()
    {
        var sentAcl = new List<byte[]>();
        var hci = new HciHost(_ => { }, sentAcl.Add);
        var central = new LeCentral(hci);
        using var stop = new CancellationTokenSource();

        Task<BondKeys> pairing = SmpPairing.PairAsync(central, 0x0040,
            new byte[] { 1, 2, 3, 4, 5, 6 }, new byte[] { 7, 8, 9, 10, 11, 12 },
            cancellationToken: stop.Token);
        Assert.Single(sentAcl); // Pairing Request on CID 0x0006, not ATT 0x0004.
        Assert.Equal((byte)LeCentral.CidSmp, sentAcl[0][6]);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pairing);
        Assert.Single(sentAcl); // No Confirm/Random/Encryption was sent after cancellation.
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_mismatching_peer_response_never_starts_link_encryption(bool sendWrongConfirm)
    {
        var sentCommands = new List<byte[]>();
        var sentAcl = new List<byte[]>();
        HciHost? hci = null;
        const ushort handle = 0x0040;

        void DeliverSmp(byte[] pdu)
        {
            hci!.ReceiveAclBytes([
                0x40, 0x20, (byte)(pdu.Length + 4), 0x00,
                (byte)pdu.Length, 0x00, 0x06, 0x00, .. pdu
            ]);
        }

        hci = new HciHost(sentCommands.Add, acl =>
        {
            sentAcl.Add(acl);
            byte opcode = acl[8];
            if (opcode == SmpOpcode.PairingRequest)
            {
                DeliverSmp(sendWrongConfirm
                    ? [0x02, 0x03, 0x00, 0x01, 16, 0x00, 0x01]
                    : [0x05, 0x03]); // Authentication Requirements
            }
            else if (sendWrongConfirm && opcode == SmpOpcode.PairingConfirm)
            {
                DeliverSmp([0x03, .. new byte[16]]); // Deliberately wrong confirm.
            }
            else if (sendWrongConfirm && opcode == SmpOpcode.PairingRandom)
            {
                DeliverSmp([0x04, .. new byte[16]]);
            }
        });
        var central = new LeCentral(hci);
        var result = SmpPairing.PairAsync(central, handle,
            new byte[] { 1, 2, 3, 4, 5, 6 }, new byte[] { 7, 8, 9, 10, 11, 12 });
        await Assert.ThrowsAsync<SmpException>(() => result);
        Assert.Empty(sentCommands); // No LE Enable Encryption.
        Assert.DoesNotContain(sentAcl, acl => acl[6] == LeCentral.CidAtt); // No GATT.
        if (!sendWrongConfirm)
            Assert.Single(sentAcl); // Only a Pairing Request sent.
        else
            Assert.Contains(sentAcl, acl => acl[8] == SmpOpcode.PairingFailed);
    }

    static async Task<(FakeRemote Remote, LeCentral Central, LePeer Peer, ushort Handle)> ConnectFakeAsync(FakeRemote remote)
    {
        var central = new LeCentral(remote.Hci);
        await central.InitializeAsync();
        LePeer peer = await central.ScanPeerAsync(
            SiriRemoteBle.IsA2854PairingAdvertisement, allowRandom: true);
        ushort handle = await central.ConnectAsync(peer.Address, peerAddressType: peer.AddressType);
        return (remote, central, peer, handle);
    }

    [Fact]
    public async Task Encryption_rejection_fails_before_remote_bond_keys_are_available()
    {
        var (remote, central, peer, handle) = await ConnectFakeAsync(new FakeRemote(rejectEncryption: true));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var error = await Assert.ThrowsAsync<HciException>(() => SmpPairing.PairAsync(
            central, handle, central.LocalAddress, BdAddr.Parse(peer.Address), peer.AddressType,
            cts.Token, requireObservedA2854Profile: true));
        Assert.Equal(HciOpcode.LeEnableEncryption, error.Opcode);
        Assert.Equal((byte)0x06, error.Status);
        Assert.Empty(remote.Writes); // No GATT/HID writes.
    }

    [Fact]
    public async Task Missing_LTK_after_STK_encryption_can_be_cancelled_without_persistence()
    {
        var (remote, central, peer, handle) = await ConnectFakeAsync(new FakeRemote(suppressBondKeys: true));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stages = new List<SmpPairingStage>();
        Task<BondKeys> operation = SmpPairing.PairAsync(
            central, handle, central.LocalAddress, BdAddr.Parse(peer.Address), peer.AddressType,
            cts.Token, stage =>
            {
                stages.Add(stage);
                if (stage == SmpPairingStage.ReceiveEncryptionInformation) cts.Cancel();
            }, requireObservedA2854Profile: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Contains(SmpPairingStage.LinkEncrypted, stages);
        Assert.DoesNotContain(SmpPairingStage.Complete, stages);
        Assert.Empty(remote.Writes);
    }

    [Fact]
    public async Task Received_LTK_buffer_is_zeroed_when_Master_Identification_never_arrives()
    {
        // The responder sends its LTK but withholds EDIV/Rand. Cancel at the
        // next stage and assert that the actual received SMP payload (shared
        // with the pairing listener) was erased on the exception path.
        var (remote, central, peer, handle) = await ConnectFakeAsync(
            new FakeRemote(suppressMasterIdentification: true));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        byte[]? observedLtkPdu = null;
        central.L2capData += (observedHandle, cid, pdu) =>
        {
            if (observedHandle == handle && cid == LeCentral.CidSmp &&
                pdu.Length == 17 && pdu[0] == SmpOpcode.EncryptionInformation)
                observedLtkPdu = pdu;
        };

        Task<BondKeys> operation = SmpPairing.PairAsync(
            central, handle, central.LocalAddress, BdAddr.Parse(peer.Address), peer.AddressType,
            cts.Token,
            stage =>
            {
                if (stage == SmpPairingStage.ReceiveMasterIdentification) cts.Cancel();
            },
            requireObservedA2854Profile: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.NotNull(observedLtkPdu);
        Assert.All(observedLtkPdu!, b => Assert.Equal((byte)0, b));
        Assert.Empty(remote.Writes); // No GATT/HID access.
    }

    [Fact]
    public async Task Completion_callback_failure_erases_received_key_distribution_payloads()
    {
        // A progress observer is caller-controlled and could throw. In that
        // case no returned LTK should escape; erase both original SMP frames.
        var (remote, central, peer, handle) = await ConnectFakeAsync(new FakeRemote());
        var capturedKeyFrames = new List<byte[]>();
        central.L2capData += (observedHandle, cid, pdu) =>
        {
            if (observedHandle == handle && cid == LeCentral.CidSmp &&
                pdu.Length > 0 &&
                pdu[0] is SmpOpcode.EncryptionInformation or SmpOpcode.MasterIdentification)
                capturedKeyFrames.Add(pdu);
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SmpPairing.PairAsync(central, handle, central.LocalAddress, BdAddr.Parse(peer.Address),
                peer.AddressType, cts.Token,
                stage =>
                {
                    if (stage == SmpPairingStage.Complete)
                        throw new InvalidOperationException("Simulated diagnostics callback failure.");
                }, requireObservedA2854Profile: true));

        Assert.Contains("Simulated diagnostics", exception.Message);
        Assert.Equal(2, capturedKeyFrames.Count);
        foreach (byte[] frame in capturedKeyFrames)
            Assert.All(frame, value => Assert.Equal((byte)0, value));
        Assert.Empty(remote.Writes); // No GATT/HID.
    }

}
