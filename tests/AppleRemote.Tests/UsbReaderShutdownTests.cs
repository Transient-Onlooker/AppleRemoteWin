// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class UsbReaderShutdownTests
{
    [Fact]
    public async Task Held_reader_blocks_release_until_it_exits()
    {
        int alive = 1, released = 0;
        var abortSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shutdown = new UsbReaderShutdown(
            () => Volatile.Read(ref alive) != 0,
            () => abortSeen.TrySetResult(),
            () => Thread.Sleep(10),
            () => Interlocked.Increment(ref released));

        Task completion = shutdown.Begin();
        await abortSeen.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, Volatile.Read(ref released));
        Assert.False(completion.IsCompleted);
        Volatile.Write(ref alive, 0);
        await completion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, released);
    }

    [Fact]
    public async Task Abort_and_join_exceptions_do_not_free_active_reader()
    {
        int alive = 1, aborts = 0, waits = 0, released = 0;
        var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shutdown = new UsbReaderShutdown(
            () => Volatile.Read(ref alive) != 0,
            () => {
                if (Interlocked.Increment(ref aborts) == 1)
                    throw new InvalidOperationException("injected abort failure");
            },
            () => {
                if (Interlocked.Increment(ref waits) == 1) {
                    attempted.TrySetResult();
                    throw new InvalidOperationException("injected join failure");
                }
                Thread.Sleep(10);
            },
            () => Interlocked.Increment(ref released));

        Task completion = shutdown.Begin();
        await attempted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, Volatile.Read(ref released));
        Volatile.Write(ref alive, 0);
        await completion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, released);
        Assert.True(Volatile.Read(ref aborts) >= 1);
        Assert.True(Volatile.Read(ref waits) >= 1);
    }

    [Fact]
    public async Task Concurrent_calls_share_a_single_cleanup()
    {
        int released = 0;
        var shutdown = new UsbReaderShutdown(
            () => false, () => {}, () => {}, () => Interlocked.Increment(ref released));
        var tasks = new Task[16];
        await Task.WhenAll(Enumerable.Range(0, tasks.Length)
            .Select(i => Task.Run(() => { tasks[i] = shutdown.Begin(); })));
        Assert.All(tasks, task => Assert.Same(tasks[0], task));
        await tasks[0].WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Same(tasks[0], shutdown.Begin());
        Assert.Equal(1, released);
    }

    [Fact]
    public async Task Native_release_error_faults_completion_without_double_free()
    {
        int attempts = 0;
        var shutdown = new UsbReaderShutdown(
            () => false, () => {}, () => {},
            () => { Interlocked.Increment(ref attempts); throw new InvalidOperationException("injected native free"); });
        Task completion = shutdown.Begin();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await completion.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Same(completion, shutdown.Begin());
        Assert.Equal(1, attempts);
    }
}

