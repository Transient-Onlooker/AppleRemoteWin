// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854HidCaptureTests
{
    [Theory]
    [InlineData(59, 720, 0x13, A2854HidCaptureOutcome.BothReportsCaptured)]
    [InlineData(59, 0, 0x13, A2854HidCaptureOutcome.PartialReportsCaptured)]
    [InlineData(0, 720, 0x13, A2854HidCaptureOutcome.PartialReportsCaptured)]
    [InlineData(0, 0, 0x13, A2854HidCaptureOutcome.NoReportsCaptured)]
    [InlineData(59, 720, 0x08, A2854HidCaptureOutcome.UnexpectedLinkTermination)]
    [InlineData(59, 720, 0x3E, A2854HidCaptureOutcome.UnexpectedLinkTermination)]
    [InlineData(59, 720, null, A2854HidCaptureOutcome.BothReportsCaptured)]
    [InlineData(0, 0, null, A2854HidCaptureOutcome.NoReportsCaptured)]
    public void Remote_disconnect_reason_and_HID_data_quality_are_independent(
        int buttons, int touches, byte? reason, A2854HidCaptureOutcome expected)
    {
        Assert.Equal(expected, A2854HidCaptureAssessment.Evaluate(buttons, touches, reason));
    }

    [Fact]
    public void Negative_HID_frame_counts_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            A2854HidCaptureAssessment.Evaluate(-1, 10, 0x13));
    }

    [Fact]
    public async Task Observed_mapping_discovers_decodes_and_writes_only_allowlisted_handles()
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
            Assert.Equal((ushort)0x0039, map.ButtonValue);
            Assert.Equal((ushort)0x003A, map.ButtonCccd);
            Assert.Equal((ushort)0x003D, map.TouchValue);
            Assert.Equal((ushort)0x003E, map.TouchCccd);
            Assert.Equal((ushort)0x004D, map.UnlockFeatureValue);
            Assert.Empty(remote.Writes);

            var decoder = new A2854Decoder();
            var reportIds = new List<byte>();
            var events = new List<RemoteEvent>();
            gatt.Notification += (valueHandle, value) =>
            {
                if (!A2854HidCapture.TryDecode(decoder, map, valueHandle, value,
                    out var decoded, out byte reportId)) return;
                reportIds.Add(reportId);
                events.AddRange(decoded);
            };

            await A2854HidCapture.EnableAsync(gatt, map);
            Assert.Equal(new[] { "0x003A 0100", "0x003E 0100", "0x004D AF" }, remote.Writes);
            remote.EmitA2854Buttons(0x40, 0x00);
            remote.EmitA2854Touch(0x32, 0x14, 0xFC, 0x01, 0xC9, 0x9E, 0xFB, 0x51, 0x44, 0x01, 0x66);
            remote.EmitUnknownNotification(0xFA, 0x99, 0x88, 0x77);
            remote.EmitA2854Buttons(0x00, 0x00);
            Assert.Equal(new byte[] { 0xFB, 0xFC, 0xFB }, reportIds);
            Assert.Contains(events.OfType<ButtonsEvent>(), e => e.Pressed.Contains("menu"));
            Assert.Contains(events.OfType<ButtonsEvent>(), e => e.Released.Contains("menu"));
            Assert.Single(events.OfType<TouchEvent>());

            await A2854HidCapture.DisableAsync(gatt, map);
            Assert.Equal(new[] {
                "0x003A 0100", "0x003E 0100", "0x004D AF",
                "0x003A 0000", "0x003E 0000"
            }, remote.Writes);
            Assert.DoesNotContain(remote.Writes, w => w.StartsWith("0x0021 "));
            Assert.Equal(0, remote.Pairings);
        }
        finally { gatt.Detach(); }
    }

    [Fact]
    public async Task Enable_and_disable_reject_forged_exact_and_mismatched_maps_before_any_writes()
    {
        var remote = new FakeRemote(includeA2854ButtonInput: true);
        var central = new LeCentral(remote.Hci);
        await central.InitializeAsync();
        ushort handle = await central.ConnectAsync(FakeRemote.Address);
        await central.EncryptAsync(handle, FakeRemote.Bond);
        var gatt = new GattClient(central, handle);
        try
        {
            var forgedExact = new A2854HidInputMap(0x0039, 0x003A, 0x003D, 0x003E, 0x004D);
            var forgedMismatch = new A2854HidInputMap(0x0020, 0x0021, 0x0024, 0x0025, 0x001D);
            await Assert.ThrowsAsync<InvalidDataException>(() => A2854HidCapture.EnableAsync(gatt, forgedExact));
            await Assert.ThrowsAsync<InvalidDataException>(() => A2854HidCapture.DisableAsync(gatt, forgedExact));
            await Assert.ThrowsAsync<InvalidDataException>(() => A2854HidCapture.EnableAsync(gatt, forgedMismatch));
            Assert.Empty(remote.Writes);
        }
        finally { gatt.Detach(); }
    }

    [Theory]
    [InlineData(FakeA2854Faults.MissingButtonCccd)]
    [InlineData(FakeA2854Faults.DuplicateButtonCccd)]
    [InlineData(FakeA2854Faults.MissingButtonReportReference)]
    [InlineData(FakeA2854Faults.DuplicateButtonReportReference)]
    public async Task Invalid_button_descriptors_fail_discovery_before_writes(FakeA2854Faults faults)
    {
        var remote = new FakeRemote(includeA2854ButtonInput: true, a2854Faults: faults);
        var central = new LeCentral(remote.Hci);
        await central.InitializeAsync();
        ushort handle = await central.ConnectAsync(FakeRemote.Address);
        await central.EncryptAsync(handle, FakeRemote.Bond);
        var gatt = new GattClient(central, handle);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => A2854HidCapture.DiscoverAsync(gatt));
            if (faults == FakeA2854Faults.DuplicateButtonCccd)
                Assert.Contains("Expected exactly one CCCD", error.Message);
            if (faults == FakeA2854Faults.DuplicateButtonReportReference)
                Assert.Contains("Duplicate HID Report Reference", error.Message);
            Assert.Empty(remote.Writes);
        }
        finally { gatt.Detach(); }
    }

    [Fact]
    public async Task Mismatched_observed_value_handles_fail_before_any_write()
    {
        var remote = new FakeRemote(includeA2854ButtonInput: true, mismatchA2854ValueHandles: true);
        var central = new LeCentral(remote.Hci);
        await central.InitializeAsync();
        ushort handle = await central.ConnectAsync(FakeRemote.Address);
        await central.EncryptAsync(handle, FakeRemote.Bond);
        var gatt = new GattClient(central, handle);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => A2854HidCapture.DiscoverAsync(gatt));
            Assert.Contains("differ", error.Message);
            Assert.Empty(remote.Writes);
        }
        finally { gatt.Detach(); }
    }

    [Fact]
    public async Task Discovered_map_is_bound_to_its_gatt_client()
    {
        var remote = new FakeRemote(includeA2854ButtonInput: true);
        var central = new LeCentral(remote.Hci);
        await central.InitializeAsync();
        ushort handle = await central.ConnectAsync(FakeRemote.Address);
        await central.EncryptAsync(handle, FakeRemote.Bond);
        var first = new GattClient(central, handle);
        var second = new GattClient(central, handle);
        try
        {
            var map = await A2854HidCapture.DiscoverAsync(first);
            await Assert.ThrowsAsync<InvalidDataException>(() => A2854HidCapture.EnableAsync(second, map));
            Assert.Empty(remote.Writes);
        }
        finally { first.Detach(); second.Detach(); }
    }

    [Fact]
    public async Task Partial_enable_rejection_attempts_both_cccd_disables_and_preserves_failure()
    {
        var remote = new FakeRemote(includeA2854ButtonInput: true) { RejectWriteHandle = 0x003E };
        var central = new LeCentral(remote.Hci);
        await central.InitializeAsync();
        ushort handle = await central.ConnectAsync(FakeRemote.Address);
        await central.EncryptAsync(handle, FakeRemote.Bond);
        var gatt = new GattClient(central, handle);
        try
        {
            var map = await A2854HidCapture.DiscoverAsync(gatt);
            var error = await Assert.ThrowsAsync<AttException>(() => A2854HidCapture.EnableAsync(gatt, map));
            Assert.Equal((byte)0x03, error.Error);
            Assert.Equal(new[] { "0x003A 0100", "0x003E 0100", "0x003A 0000", "0x003E 0000" }, remote.WriteAttempts);
            Assert.DoesNotContain(remote.WriteAttempts, w => w == "0x004D AF");
        }
        finally { gatt.Detach(); }
    }

    [Fact]
    public async Task Disable_attempts_touch_cccd_when_button_cccd_is_rejected()
    {
        var remote = new FakeRemote(includeA2854ButtonInput: true) { RejectWriteHandle = 0x003A };
        var central = new LeCentral(remote.Hci);
        await central.InitializeAsync();
        ushort handle = await central.ConnectAsync(FakeRemote.Address);
        await central.EncryptAsync(handle, FakeRemote.Bond);
        var gatt = new GattClient(central, handle);
        try
        {
            var map = await A2854HidCapture.DiscoverAsync(gatt);
            var error = await Assert.ThrowsAsync<AttException>(() => A2854HidCapture.DisableAsync(gatt, map));
            Assert.Equal((byte)0x03, error.Error);
            Assert.Equal(new[] { "0x003A 0000", "0x003E 0000" }, remote.WriteAttempts);
            Assert.Equal(new[] { "0x003E 0000" }, remote.Writes);
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
