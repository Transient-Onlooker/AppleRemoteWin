// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using Xunit;

namespace AppleRemote.Tests;

public sealed class GattShutdownTests
{
    [Theory]
    [InlineData(0x02)]
    [InlineData(0x0A)]
    public async Task Reentrant_detach_in_peer_request_does_not_send_a_reply(byte opcode)
    {
        var packets = new ConcurrentQueue<byte[]>();
        var host = new HciHost(_ => { }, packets.Enqueue);
        var gatt = new GattClient(new LeCentral(host), 0x40);
        gatt.PeerRequest += _ => gatt.Detach();
        host.ReceiveAclBytes([0x40, 0x20, 7, 0, 3, 0, 4, 0, opcode, 1, 0]);
        await gatt.DetachAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Empty(packets);
    }

    [Fact]
    public async Task Async_detach_drains_admitted_send_before_transport_release()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int sends = 0;
        var host = new HciHost(_ => { }, _ =>
        {
            Interlocked.Increment(ref sends);
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Fake dispatch deadline");
        });
        var gatt = new GattClient(new LeCentral(host), 0x40);
        var read = Task.Run(() => gatt.ReadAsync(1));
        Task? drain = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            await Task.Run(gatt.Detach).WaitAsync(TimeSpan.FromSeconds(2));
            drain = gatt.DetachAsync().AsTask();
            Assert.False(drain.IsCompleted);
            await Assert.ThrowsAnyAsync<Exception>(() => gatt.WriteAsync(0x3A, 0, 0));
            Assert.Equal(1, Volatile.Read(ref sends));
        }
        finally
        {
            release.Set();
            await Assert.ThrowsAnyAsync<Exception>(() => read.WaitAsync(TimeSpan.FromSeconds(2)));
            await (drain ?? gatt.DetachAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(2));
        }
        await gatt.DetachAsync();
        Assert.Equal(1, sends);
    }

    [Fact]
    public async Task Three_hundred_send_admission_and_detach_races_drain_without_late_sends()
    {
        for (int iteration = 0; iteration < 300; iteration++)
        {
            int sends = 0;
            var host = new HciHost(_ => { }, _ => Interlocked.Increment(ref sends));
            var gatt = new GattClient(new LeCentral(host), 0x40);
            using var start = new ManualResetEventSlim();
            var read = Task.Run(async () =>
            {
                if (!start.Wait(TimeSpan.FromSeconds(2))) throw new TimeoutException();
                await gatt.ReadAsync(1);
            });
            var detach = Task.Run(async () =>
            {
                if (!start.Wait(TimeSpan.FromSeconds(2))) throw new TimeoutException();
                await gatt.DetachAsync();
            });
            start.Set();
            await detach.WaitAsync(TimeSpan.FromSeconds(2));
            int atDrain = Volatile.Read(ref sends);
            await Assert.ThrowsAnyAsync<Exception>(() => read.WaitAsync(TimeSpan.FromSeconds(2)));
            await Assert.ThrowsAnyAsync<Exception>(() => gatt.WriteAsync(0x3A, 0, 0));
            host.ReceiveAclBytes([0x40, 0x20, 7, 0, 3, 0, 4, 0, 0x02, 23, 0]);
            Assert.InRange(atDrain, 0, 1);
            Assert.Equal(atDrain, Volatile.Read(ref sends));
            Assert.True(read.IsCompleted);
        }
    }

    [Fact]
    public async Task Transport_failure_preserves_original_exception_and_forbids_queued_write()
    {
        int sends = 0;
        var original = new IOException("Fake partial dispatch");
        var host = new HciHost(_ => { }, _ => { sends++; throw original; });
        var gatt = new GattClient(new LeCentral(host), 0x40);
        Assert.Same(original, await Assert.ThrowsAsync<IOException>(() => gatt.ReadAsync(1)));
        Assert.False(gatt.CanSendRequests);
        host.ReceiveAclBytes([0x40, 0x20, 6, 0, 2, 0, 4, 0, 0x0B, 55]);
        await Assert.ThrowsAsync<IOException>(() => gatt.WriteAsync(0x3A, 0, 0));
        Assert.Equal(1, sends);
        await gatt.DetachAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Six_hundred_cancel_detach_disconnect_and_reply_races_complete(int mode)
    {
        for (int iteration = 0; iteration < 200; iteration++)
        {
            var packets = new ConcurrentQueue<byte[]>();
            var host = new HciHost(_ => { }, packets.Enqueue);
            var gatt = new GattClient(new LeCentral(host), 0x40);
            using var cancel = new CancellationTokenSource();
            var read = gatt.ReadAsync(1, cancel.Token);
            var queued = gatt.WriteAsync(0x3A, 0, 0);
            Assert.Single(packets);
            using var start = new ManualResetEventSlim();
            var termination = Task.Run(() =>
            {
                if (!start.Wait(TimeSpan.FromSeconds(2))) throw new TimeoutException();
                if (mode == 0) cancel.Cancel();
                else if (mode == 1) gatt.Detach();
                else host.ReceiveEventBytes([5, 4, 0, 0x40, 0, 0x13]);
            });
            var observation = Task.Run(() =>
            {
                if (!start.Wait(TimeSpan.FromSeconds(2))) throw new TimeoutException();
                _ = gatt.CanSendRequests;
                if (mode == 0) gatt.Detach();
            });
            start.Set();
            await Task.WhenAll(termination, observation).WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<Exception>(() => read.WaitAsync(TimeSpan.FromSeconds(2)));
            await Assert.ThrowsAnyAsync<Exception>(() => queued.WaitAsync(TimeSpan.FromSeconds(2)));
            host.ReceiveAclBytes([0x40, 0x20, 6, 0, 2, 0, 4, 0, 0x0B, 55]);
            await Assert.ThrowsAnyAsync<Exception>(() => gatt.WriteAsync(0x3E, 0, 0));
            await gatt.DetachAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(gatt.CanSendRequests);
            Assert.Single(packets);
            var gate = (SemaphoreSlim)typeof(GattClient).GetField("_gate",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(gatt)!;
            Assert.Equal(1, gate.CurrentCount);
            Assert.Null(typeof(GattClient).GetField("_pending",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(gatt));
        }
    }
}
