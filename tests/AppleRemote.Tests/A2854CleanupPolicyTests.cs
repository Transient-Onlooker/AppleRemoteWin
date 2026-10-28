// SPDX-License-Identifier: AGPL-3.0-or-later
using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854CleanupPolicyTests
{
    [Fact]
    public void Pending_native_cleanup_blocks_start_until_its_own_success()
    {
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.Equal(A2854CleanupState.Pending, A2854CleanupPolicy.Assess(cleanup.Task));
        Assert.False(A2854CleanupPolicy.CanStart(A2854CleanupPolicy.Assess(cleanup.Task)));
        cleanup.SetResult();
        Assert.Equal(A2854CleanupState.Succeeded, A2854CleanupPolicy.Assess(cleanup.Task));
        Assert.True(A2854CleanupPolicy.CanStart(A2854CleanupPolicy.Assess(cleanup.Task)));
    }

    [Fact]
    public void Cancelled_BLE_is_independent_of_successful_USB_cleanup()
    {
        var ble = Task.FromCanceled(new CancellationToken(true));
        Assert.True(ble.IsCanceled);
        Assert.True(A2854CleanupPolicy.CanStart(A2854CleanupPolicy.Assess(Task.CompletedTask)));
    }

    [Fact]
    public void Failed_or_cancelled_native_cleanup_stays_blocked()
    {
        var failure = Task.FromException(new IOException("Fake cleanup failure"));
        Assert.Equal(A2854CleanupState.Failed, A2854CleanupPolicy.Assess(failure));
        Assert.False(A2854CleanupPolicy.CanStart(A2854CleanupPolicy.Assess(failure)));
        _ = failure.Exception;
        Assert.False(A2854CleanupPolicy.CanStart(A2854CleanupPolicy.Assess(Task.FromCanceled(new CancellationToken(true)))));
        Assert.True(A2854CleanupPolicy.CanStart(A2854CleanupPolicy.Assess(null)));
    }
}
