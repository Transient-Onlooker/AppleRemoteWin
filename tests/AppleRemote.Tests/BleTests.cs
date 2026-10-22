// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 AnthoDingo

using Xunit;

namespace AppleRemote.Tests;

public class BleTests
{
    readonly List<byte[]> _sent = new();
    readonly HciHost _hci;
    readonly LeCentral _central;

    public BleTests()
    {
        _hci = new HciHost(_sent.Add, _sent.Add);
        _central = new LeCentral(_hci);
    }

    void CommandStatusOk(ushort opcode) => _hci.ReceiveEventBytes([0x0F, 0x04, 0x00, 0x01, (byte)opcode, (byte)(opcode >> 8)]);

    [Fact]
    public void Addresses_are_little_endian_on_the_wire()
    {
        Assert.Equal(new byte[] { 0xCC, 0xBF, 0x26, 0x99, 0x9D, 0x14 }, BdAddr.Parse("14:9D:99:26:BF:CC"));
        Assert.Equal("14:9D:99:26:BF:CC", BdAddr.Format(BdAddr.Parse("14:9d:99:26:bf:cc")));
    }

    [Fact]
    public async Task Connect_targets_the_public_address_and_returns_the_handle()
    {
        var connecting = _central.ConnectAsync("14:9D:99:26:BF:CC");
        byte[] cmd = _sent.Single();
        Assert.Equal(new byte[] { 0x0D, 0x20, 25 }, cmd[..3]);
        Assert.Equal(new byte[] { 0x00, 0xCC, 0xBF, 0x26, 0x99, 0x9D, 0x14, 0x00 }, cmd[8..16]);   // public peer, public us

        CommandStatusOk(HciOpcode.LeCreateConnection);
        Assert.False(connecting.IsCompleted);   // waiting for the remote
        _hci.ReceiveEventBytes([0x3E, 0x13, 0x01, 0x00, 0x40, 0x00, 0x00, 0x00, 0xCC, 0xBF, 0x26, 0x99, 0x9D, 0x14,
            0x18, 0x00, 0x00, 0x00, 0xC8, 0x00, 0x00]);
        Assert.Equal((ushort)0x0040, await connecting);
    }

    [Fact]
    public void Scan_reports_every_advertisement_with_address_and_rssi()
    {
        var seen = new List<(byte[] Address, sbyte Rssi, byte[] Data)>();
        _central.Advertisement += (a, r, d) => seen.Add((a, r, d));
        using var cts = new CancellationTokenSource();
        var scanning = _central.ScanAsync(_ => false, cts.Token);
        void CommandCompleteOk(ushort opcode) => _hci.ReceiveEventBytes([0x0E, 0x04, 0x01, (byte)opcode, (byte)(opcode >> 8), 0x00]);
        CommandCompleteOk(HciOpcode.LeSetScanParameters);
        CommandCompleteOk(HciOpcode.LeSetScanEnable);

        // Name "AB", Apple manufacturer data, 16-bit services 0x180F + 0x1812; random address; RSSI -60.
        byte[] ad = [0x03, 0x09, (byte)'A', (byte)'B', 0x04, 0xFF, 0x4C, 0x00, 0x01, 0x05, 0x03, 0x0F, 0x18, 0x12, 0x18];
        _hci.ReceiveEventBytes([0x3E, (byte)(12 + ad.Length), 0x02, 0x01, 0x00, 0x01, 1, 2, 3, 4, 5, 6, (byte)ad.Length, .. ad, 0xC4]);

        var (address, rssi, data) = seen.Single();
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, address);
        Assert.Equal(-60, rssi);
        Assert.Equal([(0x09, "AB"u8.ToArray()), (0xFF, [0x4C, 0x00, 0x01]), (0x03, [0x0F, 0x18, 0x12, 0x18])],
            LeCentral.AdStructures(data).Select(s => (s.Type, s.Value)));

        Assert.False(scanning.IsCompleted);   // nothing matched: scans until cancelled
        cts.Cancel();
    }

    [Fact]
    public async Task Bluetooth_5_controller_scans_and_connects_with_the_extended_commands()
    {
        // Self-answering controller announcing LE Extended Advertising (feature bit 12).
        var sent = new List<byte[]>();
        HciHost? hci = null;
        hci = new HciHost(c =>
        {
            sent.Add(c);
            ushort op = (ushort)(c[0] | c[1] << 8);
            if (op == HciOpcode.LeExtendedCreateConnection) hci!.ReceiveEventBytes([0x0F, 0x04, 0x00, 0x01, c[0], c[1]]);
            else hci!.ReceiveEventBytes([0x0E, 12, 0x01, c[0], c[1], 0x00, .. (op == HciOpcode.LeReadLocalFeatures ? new byte[] { 0, 0x10, 0, 0, 0, 0, 0, 0 } : new byte[8])]);
        }, _ => { });
        var central = new LeCentral(hci);
        await central.InitializeAsync();
        Assert.True(central.Extended);
        Assert.Contains(sent, c => c[0] == 0x01 && c[1] == 0x20 && c[3] == 0x1F && c[4] == 0x12);   // LE event mask: enhanced connection + extended advertising

        var scanning = central.ScanAsync(SiriRemoteBle.IsSiriRemoteAdvertisement);
        Assert.Equal(new byte[] { 0x41, 0x20, 8, 0x00, 0x00, 0x01, 0x01, 0x60, 0x00, 0x30, 0x00 }, sent[^2]);
        // Extended report of a legacy ADV_IND (event type 0x13) from a public address, RSSI -40.
        byte[] ad = FakeRemote.PairingAdvertisement;
        hci.ReceiveEventBytes([0x3E, (byte)(26 + ad.Length), 0x0D, 0x01, 0x13, 0x00, 0x00, .. BdAddr.Parse(FakeRemote.Address),
            0x01, 0x00, 0xFF, 0x7F, 0xD8, 0x00, 0x00, 0x00, 0, 0, 0, 0, 0, 0, (byte)ad.Length, .. ad]);
        Assert.Equal(FakeRemote.Address, await scanning);
        Assert.Equal(new byte[] { 0x42, 0x20, 6, 0, 0, 0, 0, 0, 0 }, sent[^1]);   // scan disabled

        var connecting = central.ConnectAsync(FakeRemote.Address);
        byte[] expected = [0x43, 0x20, 26, 0x00, 0x00, 0x00, .. BdAddr.Parse(FakeRemote.Address), 0x01, 0x60, 0x00, 0x30, 0x00,
            0x0C, 0x00, 0x18, 0x00, 0x00, 0x00, 0xC8, 0x00, 0, 0, 0, 0];
        Assert.Equal(expected, sent[^1]);
        // Extended HCI v5 controller sends LE Enhanced Connection Complete (subevent 0x0A).
        // The previously configured event mask must enable bit 9 for this to be delivered.
        hci.ReceiveEventBytes([0x3E, 0x1F, 0x0A, 0x00, 0x40, 0x00, 0x00, 0x00, .. BdAddr.Parse(FakeRemote.Address),
            .. new byte[12], 0x18, 0x00, 0x00, 0x00, 0xC8, 0x00, 0x00]);
        Assert.Equal((ushort)0x0040, await connecting);
    }

    [Fact]
    public async Task Encrypt_sends_the_bond_keys_and_waits_for_the_encryption_change()
    {
        var keys = new BondKeys(Enumerable.Range(1, 16).Select(i => (byte)i).ToArray(), 0x4D3E, [9, 8, 7, 6, 5, 4, 3, 2]);
        var encrypting = _central.EncryptAsync(0x0040, keys);
        Assert.Equal(new byte[] { 0x19, 0x20, 28, 0x40, 0x00, 9, 8, 7, 6, 5, 4, 3, 2, 0x3E, 0x4D, 1, 2, 3, 4, 5, 6, 7, 8,
            9, 10, 11, 12, 13, 14, 15, 16 }, _sent.Single());

        CommandStatusOk(HciOpcode.LeEnableEncryption);
        Assert.False(encrypting.IsCompleted);
        _hci.ReceiveEventBytes([0x08, 0x04, 0x00, 0x40, 0x00, 0x01]);
        await encrypting;
    }

    [Fact]
    public async Task Disconnection_fails_a_pending_encryption_and_is_reported()
    {
        var disconnected = new List<(ushort, byte)>();
        _central.Disconnected += (h, reason) => disconnected.Add((h, reason));

        var encrypting = _central.EncryptAsync(0x0040, new BondKeys(new byte[16], 0, new byte[8]));
        CommandStatusOk(HciOpcode.LeEnableEncryption);
        _hci.ReceiveEventBytes([0x05, 0x04, 0x00, 0x40, 0x00, 0x3E]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => encrypting);
        Assert.Equal((0x0040, 0x3E), disconnected.Single());
    }

    [Fact]
    public async Task Encryption_ignores_events_for_a_different_connection()
    {
        var encrypting = _central.EncryptAsync(0x0040, new BondKeys(new byte[16], 0, new byte[8]));
        CommandStatusOk(HciOpcode.LeEnableEncryption);
        _hci.ReceiveEventBytes([0x08, 0x04, 0x00, 0x41, 0x00, 0x01]);
        _hci.ReceiveEventBytes([0x05, 0x04, 0x00, 0x41, 0x00, 0x16]);
        Assert.False(encrypting.IsCompleted);
        _hci.ReceiveEventBytes([0x08, 0x04, 0x00, 0x40, 0x00, 0x01]);
        await encrypting;
    }

    [Fact]
    public async Task Encryption_wait_can_be_cancelled_and_next_attempt_still_completes()
    {
        using var stop = new CancellationTokenSource();
        var keys = new BondKeys(new byte[16], 0, new byte[8]);
        Task cancelled = _central.EncryptAsync(0x0040, keys, stop.Token);
        CommandStatusOk(HciOpcode.LeEnableEncryption);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        Task next = _central.EncryptAsync(0x0041, keys);
        CommandStatusOk(HciOpcode.LeEnableEncryption);
        _hci.ReceiveEventBytes([0x08, 0x04, 0x00, 0x40, 0x00, 0x01]);
        Assert.False(next.IsCompleted);
        _hci.ReceiveEventBytes([0x08, 0x04, 0x00, 0x41, 0x00, 0x01]);
        await next;
    }
    [Fact]
    public void Link_only_central_does_not_respond_to_unsolicited_L2CAP_signaling()
    {
        var commands = new List<byte[]>();
        var aclOut = new List<byte[]>();
        var hci = new HciHost(commands.Add, aclOut.Add);
        _ = new LeCentral(hci, respondToSignaling: false);

        // HCI ACL handle 0x0040, PB=0; L2CAP signaling CID 0x0005,
        // Connection Parameter Update Request (0x12), 8-byte params.
        hci.ReceiveAclBytes([
            0x40, 0x00, 0x10, 0x00,
            0x0C, 0x00, 0x05, 0x00,
            0x12, 0x01, 0x08, 0x00,
            0x0C, 0x00, 0x18, 0x00, 0x00, 0x00, 0xC8, 0x00
        ]);

        Assert.Empty(commands);
        Assert.Empty(aclOut);
    }

    [Fact]
    public async Task Link_only_central_uses_random_peer_address_and_disconnects_without_pairing()
    {
        var commands = new List<byte[]>();
        var aclOut = new List<byte[]>();
        var hci = new HciHost(commands.Add, aclOut.Add);
        var central = new LeCentral(hci, respondToSignaling: false);
        var disconnected = new List<(ushort Handle, byte Reason)>();
        central.Disconnected += (handle, reason) => disconnected.Add((handle, reason));

        Task<ushort> link = central.ConnectAsync("14:9D:99:26:BF:CC", peerAddressType: 1);
        byte[] create = commands.Single();
        Assert.Equal(new byte[] { 0x0D, 0x20, 25 }, create[..3]);
        Assert.Equal((byte)1, create[8]); // Random peer address, not a public-address assumption.
        Assert.Equal(BdAddr.Parse("14:9D:99:26:BF:CC"), create[9..15]);

        hci.ReceiveEventBytes([0x0F, 0x04, 0x00, 0x01, 0x0D, 0x20]);
        hci.ReceiveEventBytes([
            0x3E, 0x13, 0x01, 0x00, 0x40, 0x00, 0x00, 0x01,
            .. BdAddr.Parse("14:9D:99:26:BF:CC"),
            0x18, 0x00, 0x00, 0x00, 0xC8, 0x00, 0x00
        ]);
        ushort handle = await link;
        Assert.Equal((ushort)0x0040, handle);

        Task disconnect = central.DisconnectAsync(handle);
        Assert.Equal(new byte[] { 0x06, 0x04, 0x03, 0x40, 0x00, 0x13 }, commands[^1]);
        hci.ReceiveEventBytes([0x0F, 0x04, 0x00, 0x01, 0x06, 0x04]);
        await disconnect;
        hci.ReceiveEventBytes([0x05, 0x04, 0x00, 0x40, 0x00, 0x16]);
        Assert.Equal(new[] { ((ushort)0x0040, (byte)0x16) }, disconnected);
        Assert.Empty(aclOut);
    }

    [Fact]
    public async Task Cancelling_connection_awaits_cancel_command_and_drains_cancelled_completion()
    {
        var commands = new List<byte[]>();
        var cancelSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hci = new HciHost(command =>
        {
            commands.Add(command);
            if ((command[0] | command[1] << 8) == HciOpcode.LeCreateConnectionCancel)
                cancelSent.TrySetResult();
        }, _ => { });
        var central = new LeCentral(hci);
        using var cts = new CancellationTokenSource();

        Task<ushort> connecting = central.ConnectAsync("14:9D:99:26:BF:CC", cts.Token);
        hci.ReceiveEventBytes([0x0F, 0x04, 0x00, 0x01, 0x0D, 0x20]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => central.ConnectAsync("14:9D:99:26:BF:CC"));
        cts.Cancel();
        await cancelSent.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(HciOpcode.LeCreateConnectionCancel, (ushort)(commands[^1][0] | commands[^1][1] << 8));
        Assert.False(connecting.IsCompleted); // Cancel is serialized and awaited before the result is settled.
        hci.ReceiveEventBytes([0x0E, 0x04, 0x01, 0x0E, 0x20, 0x00]); // Cancel Command Complete: success.
        hci.ReceiveEventBytes([0x3E, 0x13, 0x01, 0x02, .. new byte[17]]); // Required failed LE Connection Complete.

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);
        Assert.Equal(new ushort[] { HciOpcode.LeCreateConnection, HciOpcode.LeCreateConnectionCancel },
            commands.Select(c => (ushort)(c[0] | c[1] << 8)));
    }

    [Theory]
    [InlineData(0x02)]
    [InlineData(0x0C)]
    public async Task Late_success_after_cancel_race_is_disconnected_before_cancellation_returns(byte cancelStatus)
    {
        var commands = new List<byte[]>();
        var cancelSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        HciHost? hci = null;
        hci = new HciHost(command =>
        {
            commands.Add(command);
            ushort opcode = (ushort)(command[0] | command[1] << 8);
            if (opcode == HciOpcode.LeCreateConnectionCancel)
                cancelSent.TrySetResult();
            else if (opcode == HciOpcode.Disconnect)
            {
                hci!.ReceiveEventBytes([0x0F, 0x04, 0x00, 0x01, command[0], command[1]]);
                hci.ReceiveEventBytes([0x05, 0x04, 0x00, 0x40, 0x00, 0x16]);
            }
        }, _ => { });
        var central = new LeCentral(hci);
        using var cts = new CancellationTokenSource();

        Task<ushort> connecting = central.ConnectAsync("14:9D:99:26:BF:CC", cts.Token, peerAddressType: 1);
        hci.ReceiveEventBytes([0x0F, 0x04, 0x00, 0x01, 0x0D, 0x20]);
        cts.Cancel();
        await cancelSent.Task.WaitAsync(TimeSpan.FromSeconds(1));

        hci.ReceiveEventBytes([0x0E, 0x04, 0x01, 0x0E, 0x20, cancelStatus]);
        hci.ReceiveEventBytes([0x3E, 0x13, 0x01, 0x00, 0x40, 0x00, 0x00, 0x01,
            .. BdAddr.Parse("14:9D:99:26:BF:CC"), 0x18, 0x00, 0x00, 0x00, 0xC8, 0x00, 0x00]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);
        Assert.Contains(commands, c => c.SequenceEqual((byte[])[0x06, 0x04, 0x03, 0x40, 0x00, 0x13]));
    }

    [Fact]
    public async Task Failed_connection_event_needs_only_the_status_field()
    {
        Task<ushort> connecting = _central.ConnectAsync("14:9D:99:26:BF:CC");
        CommandStatusOk(HciOpcode.LeCreateConnection);
        _hci.ReceiveEventBytes([0x3E, 0x02, 0x01, 0x3E]); // Failed connection: status only after the subevent code.

        HciException error = await Assert.ThrowsAsync<HciException>(() => connecting);
        Assert.Equal(HciOpcode.LeCreateConnection, error.Opcode);
        Assert.Equal((byte)0x3E, error.Status);
    }

    [Fact]
    public async Task Truncated_success_connection_event_is_rejected()
    {
        Task<ushort> connecting = _central.ConnectAsync("14:9D:99:26:BF:CC");
        CommandStatusOk(HciOpcode.LeCreateConnection);
        _hci.ReceiveEventBytes([0x3E, 0x04, 0x01, 0x00, 0x40, 0x00]);

        await Assert.ThrowsAsync<InvalidDataException>(() => connecting);
    }

}
