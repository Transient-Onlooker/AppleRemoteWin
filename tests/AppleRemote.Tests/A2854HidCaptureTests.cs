// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854HidCaptureTests
{
    [Fact]
    public async Task Maps_both_inputs_and_F0_then_subscribes_only_to_two_allowlisted_CCCDs()
    {
        var remote = new FakeRemote(includeA2854ButtonInput: true);
        var central = new LeCentral(remote.Hci);
        await central.InitializeAsync();
        ushort handle = await central.ConnectAsync(FakeRemote.Address);
        await central.EncryptAsync(handle, FakeRemote.Bond);
        var gatt = new GattClient(central, handle);
        try
        {
            var map = await A2854HidCapture.DiscoverAsync(gatt);
            Assert.Equal((ushort)0x0020, map.ButtonValue);
            Assert.Equal((ushort)0x0021, map.ButtonCccd);
            Assert.Equal((ushort)0x0024, map.TouchValue);
            Assert.Equal((ushort)0x0025, map.TouchCccd);
            Assert.Equal((ushort)0x001D, map.UnlockFeatureValue);
            Assert.Empty(remote.Writes); // Discovery is read-only.

            var decoder = new A2854Decoder();
            var allEvents = new List<RemoteEvent>();
            var reportIds = new List<byte>();
            gatt.Notification += (valueHandle, value) =>
            {
                bool accepted = A2854HidCapture.TryDecode(
                    decoder, map, valueHandle, value, out var events, out byte reportId);
                if (!accepted) return;
                reportIds.Add(reportId);
                allEvents.AddRange(events);
            };

            await A2854HidCapture.EnableAsync(gatt, map);
            Assert.Equal(new[] { "0x0021 0100", "0x0025 0100", "0x001D AF" }, remote.Writes);

            remote.EmitA2854Buttons(0x40, 0x00); // Back/Menu
            remote.EmitA2854Touch(0x32, 0x14, 0xFC, 0x01, 0xC9, 0x9E, 0xFB, 0x51, 0x44, 0x01, 0x66);
            remote.EmitUnknownNotification(0xFA, 0x99, 0x88, 0x77); // Excluded microphone/unknown.
            remote.EmitA2854Buttons(0x00, 0x00);

            Assert.Equal(new byte[] { 0xFB, 0xFC, 0xFB }, reportIds);
            var buttons = allEvents.OfType<ButtonsEvent>().ToList();
            Assert.Contains(buttons, e => e.Pressed.Contains("menu"));
            Assert.Contains(buttons, e => e.Released.Contains("menu"));
            Assert.Single(allEvents.OfType<TouchEvent>());

            await A2854HidCapture.DisableAsync(gatt, map);
            Assert.Equal(new[] {
                "0x0021 0100", "0x0025 0100", "0x001D AF",
                "0x0021 0000", "0x0025 0000"
            }, remote.Writes);
            Assert.Equal(0, remote.Pairings); // Reconnected using preexisting LTK, no SMP.
        }
        finally { gatt.Detach(); }
    }

    [Fact]
    public async Task Real_handle_gate_rejects_mismatch_before_any_writes()
    {
        var remote = new FakeRemote(includeA2854ButtonInput: true);
        var central = new LeCentral(remote.Hci);
        await central.InitializeAsync();
        ushort handle = await central.ConnectAsync(FakeRemote.Address);
        await central.EncryptAsync(handle, FakeRemote.Bond);
        var gatt = new GattClient(central, handle);
        try
        {
            // Fake handles are deliberately different from observed 0x0039/0x003D/0x004D.
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                A2854HidCapture.DiscoverAsync(gatt, requireObservedValueHandles: true));
            Assert.Empty(remote.Writes);
        }
        finally { gatt.Detach(); }
    }

    [Fact]
    public async Task Missing_button_report_prevents_any_subscription_or_unlock_write()
    {
        var remote = new FakeRemote(); // historical mock has FC and F0, not FB
        var central = new LeCentral(remote.Hci);
        await central.InitializeAsync();
        ushort handle = await central.ConnectAsync(FakeRemote.Address);
        await central.EncryptAsync(handle, FakeRemote.Bond);
        var gatt = new GattClient(central, handle);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => A2854HidCapture.DiscoverAsync(gatt));
            Assert.Empty(remote.Writes);
        }
        finally { gatt.Detach(); }
    }

    [Fact]
    public void Only_known_notification_value_handles_are_decoded()
    {
        var decoder = new A2854Decoder();
        var map = new A2854HidInputMap(0x0039, 0x003B, 0x003D, 0x003F, 0x004D);
        Assert.False(A2854HidCapture.TryDecode(
            decoder, map, 0x0048, [0xFA, 0xDE, 0xAD], out var ignored, out byte unknownId));
        Assert.Empty(ignored);
        Assert.Equal(0, unknownId);

        Assert.True(A2854HidCapture.TryDecode(
            decoder, map, map.ButtonValue, [0x02, 0x00], out var events, out byte buttonId));
        Assert.Equal((byte)0xFB, buttonId);
        Assert.Contains(events.OfType<ButtonsEvent>(), e => e.Pressed.Contains("volup"));
    }
}
