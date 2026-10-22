// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 AnthoDingo

using Xunit;

namespace AppleRemote.Tests;

public class SmpNegotiationTests
{
    readonly List<byte[]> _sentCommands = new();
    readonly List<byte[]> _sentAcl = new();
    readonly HciHost _hci;
    readonly LeCentral _central;

    public SmpNegotiationTests()
    {
        _hci = new HciHost(_sentCommands.Add, _sentAcl.Add);
        _central = new LeCentral(_hci);
    }

    void DeliverSmp(ushort handle, byte[] smpPayload)
    {
        ushort l2capLen = (ushort)smpPayload.Length;
        ushort aclLen = (ushort)(4 + l2capLen);
        byte[] packet = [
            (byte)handle, (byte)(handle >> 8),
            (byte)aclLen, (byte)(aclLen >> 8),
            (byte)l2capLen, (byte)(l2capLen >> 8),
            0x06, 0x00, // CidSmp
            .. smpPayload
        ];
        _hci.ReceiveAclBytes(packet);
    }

    [Fact]
    public void Default_PairingRequest_matches_expected_wire_bytes()
    {
        var req = SmpPairingRequest.Default;
        byte[] expected = [0x01, 0x03, 0x00, 0x01, 16, 0x00, 0x01];
        Assert.Equal(expected, req.ToBytes());
    }

    [Fact]
    public void Parse_valid_legacy_pairing_response()
    {
        // Opcode=0x02, IO=NoInputNoOutput(0x03), OOB=0, AuthReq=Bonding(0x01), KeySize=16, InitDist=0, RespDist=EncKey(0x01)
        byte[] pdu = [0x02, 0x03, 0x00, 0x01, 16, 0x00, 0x01];
        var res = SmpPairingResponse.Parse(pdu);

        Assert.Equal(SmpIoCapability.NoInputNoOutput, res.IoCapability);
        Assert.Equal(0x00, res.OobDataFlag);
        Assert.Equal(0x01, res.AuthReq);
        Assert.True(res.BondingRequested);
        Assert.False(res.MitmRequested);
        Assert.False(res.SecureConnectionsRequested);
        Assert.False(res.KeypressRequested);
        Assert.False(res.Ct2Requested);
        Assert.Equal(16, res.MaxKeySize);
        Assert.Equal(0x00, res.InitiatorKeyDistribution);
        Assert.Equal(0x01, res.ResponderKeyDistribution);
        Assert.True(res.HasEncKey);
        Assert.False(res.HasIdKey);
        Assert.False(res.HasSignKey);
        Assert.False(res.HasLinkKey);
    }

    [Fact]
    public void Parse_valid_secure_connections_response()
    {
        // Opcode=0x02, IO=DisplayOnly(0x00), OOB=1, AuthReq=Bonding|SC|MITM(0x0D), KeySize=16, InitDist=0, RespDist=EncKey|IdKey(0x03)
        byte[] pdu = [0x02, 0x00, 0x01, 0x0D, 16, 0x00, 0x03];
        var res = SmpPairingResponse.Parse(pdu);

        Assert.Equal(SmpIoCapability.DisplayOnly, res.IoCapability);
        Assert.Equal(0x01, res.OobDataFlag);
        Assert.True(res.BondingRequested);
        Assert.True(res.MitmRequested);
        Assert.True(res.SecureConnectionsRequested);
        Assert.Equal(16, res.MaxKeySize);
        Assert.True(res.HasEncKey);
        Assert.True(res.HasIdKey);
    }

    [Fact]
    public void Parse_rejects_pdu_too_short()
    {
        byte[] pdu = [0x02, 0x03, 0x00, 0x01, 16, 0x00];
        Assert.Throws<ArgumentException>(() => SmpPairingResponse.Parse(pdu));
    }

    [Fact]
    public void Parse_rejects_overlong_pairing_response()
    {
        // 0x02 Pairing Response has exactly 7 bytes on the wire.
        byte[] pdu = [0x02, 0x03, 0x00, 0x01, 16, 0x00, 0x01, 0xFF];
        Assert.Throws<ArgumentException>(() => SmpPairingResponse.Parse(pdu));
    }

    [Fact]
    public void Parse_rejects_invalid_opcode()
    {
        byte[] pdu = [0x01, 0x03, 0x00, 0x01, 16, 0x00, 0x01];
        Assert.Throws<ArgumentException>(() => SmpPairingResponse.Parse(pdu));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(17)]
    [InlineData(0)]
    public void Parse_rejects_out_of_range_key_size(byte keySize)
    {
        byte[] pdu = [0x02, 0x03, 0x00, 0x01, keySize, 0x00, 0x01];
        Assert.Throws<InvalidDataException>(() => SmpPairingResponse.Parse(pdu));
    }

    [Fact]
    public void Describe_failed_reasons()
    {
        Assert.Contains("Passkey Entry Failed", SmpFailedReason.Describe(0x01));
        Assert.Contains("OOB Not Available", SmpFailedReason.Describe(0x02));
        Assert.Contains("Authentication Requirements", SmpFailedReason.Describe(0x03));
        Assert.Contains("Confirm Value Failed", SmpFailedReason.Describe(0x04));
        Assert.Contains("Pairing Not Supported", SmpFailedReason.Describe(0x05));
        Assert.Contains("Encryption Key Size", SmpFailedReason.Describe(0x06));
        Assert.Contains("Command Not Supported", SmpFailedReason.Describe(0x07));
        Assert.Contains("Unspecified Reason", SmpFailedReason.Describe(0x08));
        Assert.Contains("Repeated Attempts", SmpFailedReason.Describe(0x09));
        Assert.Contains("Invalid Parameters", SmpFailedReason.Describe(0x0A));
        Assert.Contains("DHKey Check Failed", SmpFailedReason.Describe(0x0B));
        Assert.Contains("Numeric Comparison Failed", SmpFailedReason.Describe(0x0C));
        Assert.Contains("BR/EDR Pairing in Progress", SmpFailedReason.Describe(0x0D));
        Assert.Contains("Cross-transport Key Derivation", SmpFailedReason.Describe(0x0E));
        Assert.Contains("Key Rejected", SmpFailedReason.Describe(0x0F));
    }

    [Fact]
    public async Task Negotiator_returns_Success_when_PairingResponse_received()
    {
        ushort handle = 0x0040;
        var task = SmpNegotiator.NegotiateAsync(_central, handle);

        // Verify that central sent the pairing request L2CAP frame on CID 0x0006
        Assert.Single(_sentAcl);
        byte[] sent = _sentAcl[0];
        // ACL header (4 bytes: handle, len) + L2CAP header (4 bytes: len, CID) + SMP payload (7 bytes)
        Assert.Equal(15, sent.Length);
        Assert.Equal(0x06, sent[6]); // CID LSB
        Assert.Equal(0x00, sent[7]); // CID MSB
        Assert.Equal(0x01, sent[8]); // Opcode PairingRequest

        // Remote responds with PairingResponse (Bonding + SC)
        DeliverSmp(handle, [0x02, 0x03, 0x00, 0x09, 16, 0x00, 0x03]);

        var result = await task;
        var success = Assert.IsType<SmpNegotiationResult.Success>(result);
        Assert.True(success.Response.SecureConnectionsRequested);
        Assert.True(success.Response.BondingRequested);
        Assert.True(success.Response.HasIdKey);
    }

    [Fact]
    public async Task Negotiator_returns_Failed_when_PairingFailed_received()
    {
        ushort handle = 0x0040;
        var task = SmpNegotiator.NegotiateAsync(_central, handle);

        // Remote responds with PairingFailed (0x03: Authentication Requirements)
        DeliverSmp(handle, [0x05, 0x03]);

        var result = await task;
        var failed = Assert.IsType<SmpNegotiationResult.Failed>(result);
        Assert.Equal(0x03, failed.ReasonCode);
        Assert.Contains("Authentication Requirements", failed.Description);
    }

    [Fact]
    public async Task Negotiator_ignores_unrelated_L2cap_packets_before_response()
    {
        ushort handle = 0x0040;
        var task = SmpNegotiator.NegotiateAsync(_central, handle);

        // Unrelated handle
        DeliverSmp(0x0099, [0x02, 0x03, 0x00, 0x01, 16, 0x00, 0x01]);
        Assert.False(task.IsCompleted);

        // Security Request (0x0B) on matching handle
        DeliverSmp(handle, [0x0B, 0x01]);
        Assert.False(task.IsCompleted);

        // Finally deliver matching Pairing Response
        DeliverSmp(handle, [0x02, 0x03, 0x00, 0x01, 16, 0x00, 0x01]);
        var result = await task;
        Assert.IsType<SmpNegotiationResult.Success>(result);
    }
}
