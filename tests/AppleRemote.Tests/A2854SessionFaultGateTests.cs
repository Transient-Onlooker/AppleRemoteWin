// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854SessionFaultGateTests
{
    [Fact]
    public void USB_failure_revokes_held_input_and_cancels_disconnect_wait()
    {
        int releases = 0;
        using var gate = new A2854SessionFaultGate(CancellationToken.None,
            () => Interlocked.Increment(ref releases));
        Assert.False(gate.Token.IsCancellationRequested);
        Assert.True(gate.Fail());
        Assert.True(gate.Token.IsCancellationRequested);
        Assert.Equal(1, releases);
        Assert.False(gate.Fail());
        Assert.Equal(1, releases);
    }

    [Fact]
    public async Task Blocked_link_wait_is_cancelled_without_HCI_disconnection_event()
    {
        using var gate = new A2854SessionFaultGate(CancellationToken.None, () => { });
        Task neverCompletes = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously).Task;
        Task waiting = neverCompletes.WaitAsync(gate.Token);
        gate.Fail();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public void Cancellation_occurs_even_when_forced_release_callback_throws()
    {
        using var gate = new A2854SessionFaultGate(CancellationToken.None,
            () => throw new InvalidOperationException("Synthetic fake pointer release failure."));
        Assert.Throws<InvalidOperationException>(() => gate.Fail());
        Assert.True(gate.Token.IsCancellationRequested);
        Assert.False(gate.Fail()); // no second call to failing release callback
    }

    [Fact]
    public async Task Simultaneous_USB_errors_run_terminal_cleanup_at_most_once()
    {
        int releases = 0;
        using var gate = new A2854SessionFaultGate(CancellationToken.None,
            () => Interlocked.Increment(ref releases));
        bool[] results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Task.Run(gate.Fail)));
        Assert.Equal(1, results.Count(x => x));
        Assert.Equal(1, releases);
    }

    [Fact]
    public void USB_error_after_disposal_cannot_mutate_new_session()
    {
        int releases = 0;
        var gate = new A2854SessionFaultGate(CancellationToken.None,
            () => Interlocked.Increment(ref releases));
        gate.Dispose();
        Assert.False(gate.Fail());
        Assert.Equal(0, releases);
    }

    [Fact]
    public void User_Stop_also_cancels_linked_controller_task()
    {
        using var stop = new CancellationTokenSource();
        using var gate = new A2854SessionFaultGate(stop.Token, () => { });
        stop.Cancel();
        Assert.True(gate.Token.IsCancellationRequested);
    }
}
