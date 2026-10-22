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
            var hid = Assert.Single(services.Where(s => GattClient.Is(s.Uuid, 0x1812)));
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
}
