// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854HidCompletionTests
{
    [Theory]
    [InlineData(0x13, A2854HidCaptureOutcome.BothReportsCaptured)]
    [InlineData(0x08, A2854HidCaptureOutcome.UnexpectedLinkTermination)]
    [InlineData(0x3E, A2854HidCaptureOutcome.UnexpectedLinkTermination)]
    [InlineData(0x16, A2854HidCaptureOutcome.UnexpectedLinkTermination)]
    public async Task Already_ended_link_never_claims_host_disconnect(
        byte reason, A2854HidCaptureOutcome expected)
    {
        int commands = 0;
        var central = new LeCentral(new HciHost(_ => commands++, _ => { }));
        var result = await A2854HidCapture.DisconnectAsync(central, 0x40,
            Task.FromResult<(ushort Handle, byte Reason)>((0x40, reason)));

        Assert.Equal(0, commands);
        Assert.False(result.CommandStatusSucceeded);
        Assert.False(result.LocalHostVerified);
        Assert.Equal(expected, A2854HidCaptureAssessment.Evaluate(59, 720, result.Reason));
    }

    [Theory]
    [InlineData(0x00, 0x13, true)]
    [InlineData(0x00, 0x13, false)]
    [InlineData(0x02, 0x13, true)]
    [InlineData(0x02, 0x13, false)]
    [InlineData(0x00, 0x16, false)]
    [InlineData(0x02, 0x16, false)]
    public async Task Disconnect_event_racing_command_status_is_classified_from_both(
        byte commandStatus, byte reason, bool eventFirst)
    {
        var ended = new TaskCompletionSource<(ushort Handle, byte Reason)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        HciHost? host = null;
        host = new HciHost(command =>
        {
            Assert.Equal(HciOpcode.Disconnect, (ushort)(command[0] | command[1] << 8));
            void End() => host!.ReceiveEventBytes([0x05, 0x04, 0x00, 0x40, 0x00, reason]);
            if (eventFirst) End();
            host!.ReceiveEventBytes([0x0F, 0x04, commandStatus, 0x01, command[0], command[1]]);
            if (!eventFirst) End();
        }, _ => { });
        var central = new LeCentral(host);
        central.Disconnected += (handle, observedReason) => ended.TrySetResult((handle, observedReason));

        var result = await A2854HidCapture.DisconnectAsync(central, 0x40, ended.Task);

        Assert.Equal(commandStatus == 0, result.CommandStatusSucceeded);
        Assert.Equal(commandStatus == 0 && reason == 0x16, result.LocalHostVerified);
        Assert.Equal(reason, result.Reason);
        if (commandStatus == 0) Assert.Null(result.CommandFailure);
        else Assert.Equal(commandStatus, Assert.IsType<HciException>(result.CommandFailure).Status);
        var outcome = A2854HidCaptureAssessment.Evaluate(59, 720,
            result.LocalHostVerified ? null : result.Reason);
        Assert.Equal(reason == 0x13 || result.LocalHostVerified
            ? A2854HidCaptureOutcome.BothReportsCaptured
            : A2854HidCaptureOutcome.UnexpectedLinkTermination, outcome);
    }

    [Fact]
    public async Task Command_status_without_disconnect_event_times_out()
    {
        HciHost? host = null;
        host = new HciHost(command => host!.ReceiveEventBytes(
            [0x0F, 0x04, 0x00, 0x01, command[0], command[1]]), _ => { });
        var pending = new TaskCompletionSource<(ushort Handle, byte Reason)>();

        await Assert.ThrowsAsync<TimeoutException>(() => A2854HidCapture.DisconnectAsync(
            new LeCentral(host), 0x40, pending.Task, eventTimeout: TimeSpan.Zero));
    }

    [Fact]
    public async Task User_cancellation_cannot_be_reclassified_as_successful_peer_end()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var central = new LeCentral(new HciHost(_ => Assert.Fail("No command after cancellation"), _ => { }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => A2854HidCapture.DisconnectAsync(
            central, 0x40, Task.FromResult<(ushort Handle, byte Reason)>((0x40, 0x13)), stop.Token));
    }

    [Fact]
    public async Task Cancellation_during_command_still_wins_over_a_completed_peer_event()
    {
        using var stop = new CancellationTokenSource();
        var ended = new TaskCompletionSource<(ushort Handle, byte Reason)>();
        HciHost? host = null;
        host = new HciHost(command =>
        {
            host!.ReceiveEventBytes([0x0F, 0x04, 0x00, 0x01, command[0], command[1]]);
            stop.Cancel();
            host.ReceiveEventBytes([0x05, 0x04, 0x00, 0x40, 0x00, 0x13]);
        }, _ => { });
        var central = new LeCentral(host);
        central.Disconnected += (handle, reason) => ended.TrySetResult((handle, reason));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => A2854HidCapture.DisconnectAsync(
            central, 0x40, ended.Task, stop.Token));
    }

    [Fact]
    public async Task Missing_command_status_does_not_invent_local_cleanup_after_peer_event()
    {
        var ended = new TaskCompletionSource<(ushort Handle, byte Reason)>();
        HciHost? host = null;
        host = new HciHost(_ => host!.ReceiveEventBytes(
            [0x05, 0x04, 0x00, 0x40, 0x00, 0x13]), _ => { }) { CommandTimeout = TimeSpan.Zero };
        var central = new LeCentral(host);
        central.Disconnected += (handle, reason) => ended.TrySetResult((handle, reason));
        var result = await A2854HidCapture.DisconnectAsync(central, 0x40, ended.Task);
        Assert.Equal((byte)0x13, result.Reason);
        Assert.IsType<TimeoutException>(result.CommandFailure);
        Assert.False(result.CommandStatusSucceeded);
        Assert.False(result.LocalHostVerified);
    }

    [Fact]
    public async Task Unmatched_disconnect_handle_is_not_accepted()
    {
        var central = new LeCentral(new HciHost(_ => { }, _ => { }));
        await Assert.ThrowsAsync<InvalidDataException>(() => A2854HidCapture.DisconnectAsync(
            central, 0x40, Task.FromResult<(ushort Handle, byte Reason)>((0x41, 0x13))));
    }

    [Theory]
    [InlineData(null, true, true)]
    [InlineData(0x003A, false, true)]
    [InlineData(0x003E, true, false)]
    public async Task Each_CCCD_disable_is_verified_only_after_its_Write_Response(
        int? rejectedHandle, bool expectedButtons, bool expectedTouch)
    {
        var remote = new FakeRemote(includeA2854ButtonInput: true)
        {
            RejectWriteHandle = rejectedHandle.HasValue ? (ushort)rejectedHandle.Value : null
        };
        var central = new LeCentral(remote.Hci);
        await central.InitializeAsync();
        ushort handle = await central.ConnectAsync(FakeRemote.Address);
        await central.EncryptAsync(handle, FakeRemote.Bond);
        var gatt = new GattClient(central, handle);
        try
        {
            var map = await A2854HidCapture.DiscoverAsync(gatt);
            var result = await A2854HidCapture.DisableWithResultAsync(gatt, map);
            Assert.Equal(expectedButtons, result.ButtonDisabled);
            Assert.Equal(expectedTouch, result.TouchDisabled);
            Assert.Equal(expectedButtons && expectedTouch, result.BothDisabled);
            Assert.Equal(new[] { "0x003A 0000", "0x003E 0000" }, remote.WriteAttempts);
            if (rejectedHandle is null) Assert.Null(result.Failure);
            else Assert.Equal((byte)0x03, Assert.IsType<AttException>(result.Failure).Error);
            Assert.Equal(0, remote.Pairings);
        }
        finally { gatt.Detach(); }
    }

    [Theory]
    [InlineData(0x003A, false)]
    [InlineData(0x003E, true)]
    public async Task Missing_CCCD_response_and_late_response_never_verify_cleanup(
        ushort suppressedHandle, bool buttonAlreadyConfirmed)
    {
        var remote = new FakeRemote(includeA2854ButtonInput: true);
        var central = new LeCentral(remote.Hci);
        await central.InitializeAsync();
        ushort handle = await central.ConnectAsync(FakeRemote.Address);
        await central.EncryptAsync(handle, FakeRemote.Bond);
        var gatt = new GattClient(central, handle) { RequestTimeout = TimeSpan.Zero };
        try
        {
            var map = await A2854HidCapture.DiscoverAsync(gatt);
            remote.SuppressWriteResponseHandle = suppressedHandle;
            var result = await A2854HidCapture.DisableWithResultAsync(gatt, map);
            Assert.Equal(buttonAlreadyConfirmed, result.ButtonDisabled);
            Assert.False(result.TouchDisabled);
            Assert.False(result.BothDisabled);
            Assert.IsType<TimeoutException>(result.Failure);
            int requests = remote.WriteAttempts.Count;
            remote.Att(0x13); // A late reply cannot revive the interrupted ATT client.
            var retry = await A2854HidCapture.DisableWithResultAsync(gatt, map);
            Assert.False(retry.BothDisabled);
            Assert.IsType<IOException>(retry.Failure);
            Assert.Equal(requests, remote.WriteAttempts.Count);
        }
        finally { gatt.Detach(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ATT_Write_Response_must_have_no_payload(int extraBytes)
    {
        int writes = 0;
        HciHost? host = null;
        host = new HciHost(_ => { }, _ =>
        {
            writes++;
            byte[] pdu = [0x13, .. new byte[extraBytes]];
            host!.ReceiveAclBytes([0x40, 0x20, (byte)(pdu.Length + 4), 0,
                (byte)pdu.Length, 0, 0x04, 0, .. pdu]);
        });
        var gatt = new GattClient(new LeCentral(host), 0x40);
        try
        {
            if (extraBytes == 0) await gatt.WriteAsync(0x003A, 0, 0);
            else
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => gatt.WriteAsync(0x003A, 0, 0));
                await Assert.ThrowsAsync<InvalidDataException>(() => gatt.WriteAsync(0x003E, 0, 0));
                Assert.Equal(1, writes);
            }
        }
        finally { gatt.Detach(); }
    }

    [Theory]
    [InlineData(0x0039, "", 0xFB)]
    [InlineData(0x0039, "010203", 0xFB)]
    [InlineData(0x003D, "320102", 0xFC)]
    [InlineData(0x003D, "000102030405060708090A", 0xFC)]
    public void Malformed_known_reports_do_not_qualify_as_valid_capture(
        ushort handle, string hex, byte expectedId)
    {
        var map = new A2854HidInputMap(0x0039, 0x003A, 0x003D, 0x003E, 0x004D);
        Assert.False(A2854HidCapture.TryDecode(new A2854Decoder(), map,
            handle, Convert.FromHexString(hex), out var events, out byte reportId));
        Assert.Equal(expectedId, reportId); // Known-but-malformed is distinct from an unknown handle.
        Assert.Empty(events);
    }
}
