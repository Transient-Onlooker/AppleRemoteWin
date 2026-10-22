// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854InputGateTests
{
    [Fact]
    public void Peer_end_issues_one_release_and_drops_late_notifications()
    {
        var delivered = new List<byte[]>();
        var gate = new A2854InputGate(data => delivered.Add(data ?? []));
        Assert.True(gate.TryForward([0xFB, 0x08, 0x00])); // click pressed
        gate.Close();
        gate.Close(); // duplicate peer-end or caller-finally
        Assert.True(gate.IsClosed);
        Assert.False(gate.TryForward([0xFB, 0x08, 0x00])); // paused callback resumed
        Assert.Collection(delivered,
            first => Assert.Equal(new byte[] { 0xFB, 0x08, 0x00 }, first),
            released => Assert.Empty(released));
    }

    [Fact]
    public async Task Close_waits_for_inflight_report_and_orders_release_last()
    {
        using var entered = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var order = new List<string>();
        var gate = new A2854InputGate(data =>
        {
            if (data is { Length: > 0 })
            {
                entered.Set();
                if (!resume.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Fake report remained blocked.");
                order.Add("press");
            }
            else order.Add("release");
        });

        Task<bool> reporting = Task.Run(() => gate.TryForward([0xFB, 0x08, 0x00]));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Task closing = Task.Run(gate.Close);
        try
        {
            Assert.False(closing.IsCompleted); // blocked by the in-flight callback
        }
        finally { resume.Set(); }

        Assert.True(await reporting);
        await closing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "press", "release" }, order);
        Assert.False(gate.TryForward([0xFB, 0x08, 0x00]));
        Assert.Equal(2, order.Count);
    }

    [Fact]
    public async Task Concurrent_stop_and_usb_failure_have_one_terminal_release()
    {
        int releases = 0;
        var gate = new A2854InputGate(data =>
        {
            if (data is { Length: 0 }) Interlocked.Increment(ref releases);
        });
        await Task.WhenAll(Task.Run(gate.Close), Task.Run(gate.Close));
        Assert.Equal(1, releases);
        Assert.False(gate.TryForward([0xFC, 0x32]));
    }

    [Fact]
    public void Terminal_receiver_exception_still_executes_emergency_pointer_release()
    {
        bool simulatedButtonHeld = true;
        int emergencyReleases = 0;
        var gate = new A2854InputGate(
            report =>
            {
                if (report is { Length: 0 })
                    throw new InvalidOperationException("GUI subscriber threw during terminal event.");
            },
            () =>
            {
                simulatedButtonHeld = false;
                Interlocked.Increment(ref emergencyReleases);
            });

        Assert.Throws<InvalidOperationException>(gate.Close);
        Assert.False(simulatedButtonHeld);
        Assert.True(gate.IsClosed);
        Assert.Equal(1, emergencyReleases);
        gate.Close();
        Assert.Equal(1, emergencyReleases);
        Assert.False(gate.TryForward([0xFB, 0x08, 0x00]));
    }

    [Fact]
    public void Terminal_emergency_release_runs_once_under_concurrent_close()
    {
        int normalReleases = 0, backupReleases = 0;
        var gate = new A2854InputGate(
            report =>
            {
                if (report is { Length: 0 }) Interlocked.Increment(ref normalReleases);
            },
            () => Interlocked.Increment(ref backupReleases));

        gate.Close();
        gate.Close();
        Assert.Equal(1, normalReleases);
        Assert.Equal(1, backupReleases);
    }

    [Fact]
    public void Null_report_cannot_enter_gate()
    {
        var gate = new A2854InputGate(_ => { });
        Assert.Throws<ArgumentNullException>(() => gate.TryForward(null!));
    }
}
