// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class GattValidationTests
{
    [Fact]
    public async Task Fake_bonded_remote_supports_encrypted_reconnect_and_read_only_HID_metadata()
    {
        var remote = new FakeRemote();
        var central = new LeCentral(remote.Hci);
        await central.InitializeAsync();

        // No scan, SMP pairing, or file access. Exercise the same "known public peer" path.
        ushort handle = await central.ConnectAsync(FakeRemote.Address, peerAddressType: 0);
        await central.EncryptAsync(handle, FakeRemote.Bond);
        var gatt = new GattClient(central, handle);
        try
        {
            var services = await gatt.DiscoverPrimaryServicesAsync();
            var hid = Assert.Single(services, s => GattClient.Is(s.Uuid, 0x1812));
            var characteristics = await gatt.DiscoverCharacteristicsAsync(hid);
            Assert.NotEmpty(characteristics);
            bool sawTouch = false, sawFeature = false;
            for (int i = 0; i < characteristics.Count; i++)
            {
                var item = characteristics[i];
                ushort end = i + 1 < characteristics.Count
                    ? (ushort)(characteristics[i + 1].Declaration - 1) : hid.End;
                if (!GattClient.Is(item.Uuid, 0x2A4D) || item.Value >= end) continue;
                var descriptors = await gatt.DiscoverDescriptorsAsync((ushort)(item.Value + 1), end);
                var reference = descriptors.FirstOrDefault(d => GattClient.Is(d.Uuid, 0x2908));
                if (reference.Uuid is null) continue;
                var reportReference = await gatt.ReadAsync(reference.Handle);
                if (reportReference.SequenceEqual(new byte[] { 0xFC, 1 })) sawTouch = true;
                if (reportReference.SequenceEqual(new byte[] { 0xF0, 3 })) sawFeature = true;
            }
            Assert.True(sawTouch);
            Assert.True(sawFeature);
            Assert.Empty(remote.Writes); // ATT reads only; no CCCD write or feature unlock.
            Assert.Equal(0, remote.Pairings); // No SMP Pairing Request.
        }
        finally
        {
            gatt.Detach();
        }
    }

    [Fact]
    public async Task Preconnected_GATT_answers_early_ATT_MTU_before_encryption_or_awaited_handle()
    {
        var commands = new List<byte[]>();
        var acl = new List<byte[]>();
        var host = new HciHost(commands.Add, acl.Add);
        var central = new LeCentral(host);
        var gatt = new GattClient(central); // armed before ConnectAsync
        var earlyRequests = new List<byte>();
        gatt.PeerRequest += earlyRequests.Add;
        try
        {
            // A frame for an unrelated/unknown connection cannot be answered.
            host.ReceiveAclBytes([0x41, 0x00, 0x07, 0x00, 0x03, 0x00, 0x04, 0x00, 0x02, 0xF7, 0x00]);
            Assert.Empty(acl);

            var connecting = central.ConnectAsync("14:9D:99:26:BF:CC");
            host.ReceiveEventBytes([0x0F, 0x04, 0x00, 0x01, 0x0D, 0x20]); // Create Connection command accepted
            host.ReceiveEventBytes([
                0x3E, 0x13, 0x01, 0x00, 0x40, 0x00, 0x00, 0x00,
                0xCC, 0xBF, 0x26, 0x99, 0x9D, 0x14,
                0x18, 0x00, 0x00, 0x00, 0xC8, 0x00, 0x00
            ]);

            // Peer Exchange MTU arrives immediately after LinkEstablished,
            // BEFORE the caller resumes and BEFORE any EncryptAsync call.
            host.ReceiveAclBytes([0x40, 0x00, 0x07, 0x00, 0x03, 0x00, 0x04, 0x00, 0x02, 0xF7, 0x00]);
            Assert.Equal((ushort)0x0040, await connecting);
            Assert.Equal(new byte[] { 0x02 }, earlyRequests);
            Assert.Equal(new byte[]
            {
                0x40, 0x00, 0x07, 0x00, 0x03, 0x00, 0x04, 0x00, 0x03, 0x17, 0x00
            }, Assert.Single(acl));

            // The same early receiver handles unsupported peer-initiated
            // ATT requests with the existing Error Response semantics.
            host.ReceiveAclBytes([0x40, 0x00, 0x07, 0x00, 0x03, 0x00, 0x04, 0x00, 0x0A, 0x01, 0x00]);
            Assert.Equal(2, acl.Count);
            Assert.Equal(new byte[] { 0x01, 0x0A, 0x01, 0x00, 0x06 }, acl[1][8..]);
            Assert.Equal(new byte[] { 0x02, 0x0A }, earlyRequests);
        }
        finally
        {
            gatt.Detach();
        }
        host.ReceiveAclBytes([0x40, 0x00, 0x07, 0x00, 0x03, 0x00, 0x04, 0x00, 0x02, 0xF7, 0x00]);
        Assert.Equal(2, acl.Count);
    }

    [Theory]
    [InlineData(0x11, 0)] // Read By Group Type with illegal zero entry size.
    [InlineData(0x11, 2)] // Read By Group Type with truncated entry.
    [InlineData(0x09, 0)] // Read By Type with illegal zero entry size.
    [InlineData(0x05, 3)] // Find Information with unknown format.
    public async Task Invalid_GATT_discovery_response_throws_without_looping(byte responseOpcode, byte format)
    {
        HciHost? hci = null;
        hci = new HciHost(_ => { }, sent =>
        {
            if (sent.Length < 9 || sent[6] != LeCentral.CidAtt) return;
            byte[] payload = [responseOpcode, format];
            hci!.ReceiveAclBytes([
                0x40, 0x20, (byte)(payload.Length + 4), 0x00,
                (byte)payload.Length, 0x00, 0x04, 0x00, .. payload
            ]);
        });
        var central = new LeCentral(hci);
        var gatt = new GattClient(central, 0x0040);
        try
        {
            if (responseOpcode == 0x11)
                await Assert.ThrowsAsync<InvalidDataException>(() => gatt.DiscoverPrimaryServicesAsync());
            else if (responseOpcode == 0x09)
                await Assert.ThrowsAsync<InvalidDataException>(() => gatt.DiscoverCharacteristicsAsync(
                    new GattService(0x0001, 0x0009, [0x12, 0x18])));
            else
                await Assert.ThrowsAsync<InvalidDataException>(() => gatt.DiscoverDescriptorsAsync(0x0002, 0x0009));
        }
        finally
        {
            gatt.Detach();
        }
    }

    [Fact]
    public async Task Cancelling_GATT_read_stops_waiting_for_response()
    {
        var sent = new List<byte[]>();
        var hci = new HciHost(_ => { }, sent.Add);
        var gatt = new GattClient(new LeCentral(hci), 0x0040);
        using var cancellation = new CancellationTokenSource();

        Task<byte[]> read = gatt.ReadAsync(0x0001, cancellation.Token);
        Assert.Single(sent);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        await Assert.ThrowsAsync<IOException>(() => gatt.ReadAsync(0x0002));
        Assert.Single(sent);
        gatt.Detach();
    }

    [Fact]
    public async Task Separate_optional_battery_client_timeout_preserves_HID_notifications_and_CCCD_writes()
    {
        var acl = new List<byte[]>();
        var hci = new HciHost(_ => { }, acl.Add);
        var central = new LeCentral(hci);
        var hid = new GattClient(central, 0x0040);
        var metadata = new GattClient(central, 0x0040, handlesServerTraffic: false);
        var notifications = new List<byte[]>();
        hid.Notification += (_, value) => notifications.Add(value);
        using var timeout = new CancellationTokenSource();
        try
        {
            var batteryRead = metadata.ReadAsync(0x0001, timeout.Token);
            Assert.Single(acl);
            timeout.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => batteryRead);
            await Assert.ThrowsAsync<IOException>(() => metadata.ReadAsync(0x0002));

            // Still one live HID input sink, not duplicated by metadata.
            hci.ReceiveAclBytes([
                0x40, 0x20, 0x08, 0x00,
                0x04, 0x00, 0x04, 0x00, 0x1B, 0x39, 0x00, 0x55
            ]);
            Assert.Equal(new byte[] { 0x55 }, Assert.Single(notifications));

            // The main GATT alone acknowledges a peer-initiated MTU exchange.
            hci.ReceiveAclBytes([
                0x40, 0x20, 0x07, 0x00,
                0x03, 0x00, 0x04, 0x00, 0x02, 0x17, 0x00
            ]);
            Assert.Equal(2, acl.Count);

            // Stop must still be able to write the HID CCCD on main GATT.
            var disable = hid.WriteAsync(0x003A, 0x00, 0x00);
            Assert.Equal(3, acl.Count);
            hci.ReceiveAclBytes([
                0x40, 0x20, 0x05, 0x00, 0x01, 0x00, 0x04, 0x00, 0x13
            ]);
            await disable;
        }
        finally
        {
            metadata.Detach();
            hid.Detach();
        }
    }

    [Theory]
    [InlineData(0x000A, false)] // Value handle outside service ending at 0x0009.
    [InlineData(0x0004, true)] // Value overlaps the next declaration.
    public async Task Invalid_characteristic_value_handle_is_rejected(ushort value, bool nextDeclaration)
    {
        HciHost? hci = null;
        hci = new HciHost(_ => { }, _ =>
        {
            byte[] first = [0x01, 0x00, 0x02, (byte)value, (byte)(value >> 8), 0x4D, 0x2A];
            byte[] second = nextDeclaration ? [0x04, 0x00, 0x02, 0x05, 0x00, 0x4D, 0x2A] : [];
            byte[] pdu = [0x09, 0x07, .. first, .. second];
            hci!.ReceiveAclBytes([0x40, 0x20, (byte)(pdu.Length + 4), 0,
                (byte)pdu.Length, 0, 0x04, 0, .. pdu]);
        });
        var gatt = new GattClient(new LeCentral(hci), 0x0040);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => gatt.DiscoverCharacteristicsAsync(
                new GattService(0x0001, 0x0009, [0x12, 0x18])));
        }
        finally { gatt.Detach(); }
    }

    [Fact]
    public async Task Timed_out_ATT_client_refuses_further_requests()
    {
        var sent = new List<byte[]>();
        var gatt = new GattClient(new LeCentral(new HciHost(_ => { }, sent.Add)), 0x0040)
        {
            RequestTimeout = TimeSpan.Zero
        };
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => gatt.ReadAsync(0x0001));
            await Assert.ThrowsAsync<IOException>(() => gatt.ReadAsync(0x0002));
            Assert.Single(sent);
        }
        finally { gatt.Detach(); }
    }

    [Fact]
    public async Task Disconnect_fails_pending_and_future_GATT_requests()
    {
        var sent = new List<byte[]>();
        var hci = new HciHost(_ => { }, sent.Add);
        var central = new LeCentral(hci);
        var gatt = new GattClient(central, 0x0040);

        Task<byte[]> read = gatt.ReadAsync(0x0001);
        Assert.Single(sent);
        hci.ReceiveEventBytes([0x05, 0x04, 0x00, 0x40, 0x00, 0x13]);

        await Assert.ThrowsAsync<IOException>(() => read);
        await Assert.ThrowsAsync<IOException>(() => gatt.ReadAsync(0x0002));
        Assert.Single(sent);

        gatt.Detach();
        await Assert.ThrowsAsync<IOException>(() => gatt.ReadAsync(0x0003));
        Assert.Single(sent);
    }
}
