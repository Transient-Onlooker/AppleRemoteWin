// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class UsbAdapterOpenGateTests
{
    const string Barrot = "USB\\VID_33FA&PID_0001\\EXTERNAL";
    const string Legacy = "USB\\VID_2357&PID_0604\\OTHER";

    [Fact]
    public void Partial_startup_failure_blocks_same_adapter_until_native_cleanup()
    {
        var gate = new UsbAdapterOpenGate();
        var partial = Assert.IsType<UsbAdapterOpenGate.Lease>(gate.TryAcquire(Barrot));
        Assert.Null(gate.TryAcquire(Barrot));
        partial.Release();
        Assert.NotNull(gate.TryAcquire(Barrot));
    }

    [Fact]
    public void Failed_or_stuck_cleanup_keeps_same_adapter_reserved()
    {
        var gate = new UsbAdapterOpenGate();
        Assert.NotNull(gate.TryAcquire(Barrot));
        Assert.Null(gate.TryAcquire(Barrot));
        Assert.Null(gate.TryAcquire(Barrot));
    }

    [Fact]
    public void Reservation_is_case_insensitive_for_windows_instance_ids()
    {
        var gate = new UsbAdapterOpenGate();
        Assert.NotNull(gate.TryAcquire(Barrot));
        Assert.Null(gate.TryAcquire(Barrot.ToLowerInvariant()));
    }

    [Fact]
    public void Failed_barrot_cleanup_does_not_block_unrelated_legacy_adapter()
    {
        var gate = new UsbAdapterOpenGate();
        Assert.NotNull(gate.TryAcquire(Barrot));
        Assert.NotNull(gate.TryAcquire(Legacy));
    }

    [Fact]
    public void Concurrent_open_attempts_grant_only_one_lease()
    {
        var gate = new UsbAdapterOpenGate();
        var attempts = new UsbAdapterOpenGate.Lease?[32];
        Parallel.For(0, attempts.Length, i => attempts[i] = gate.TryAcquire(Barrot));
        var winner = Assert.Single(attempts.Where(x => x is not null));
        Assert.Null(gate.TryAcquire(Barrot));
        winner!.Release();
        Assert.NotNull(gate.TryAcquire(Barrot));
    }

    [Fact]
    public void Repeated_release_cannot_unlock_another_open()
    {
        var gate = new UsbAdapterOpenGate();
        var first = Assert.IsType<UsbAdapterOpenGate.Lease>(gate.TryAcquire(Barrot));
        first.Release();
        var second = Assert.IsType<UsbAdapterOpenGate.Lease>(gate.TryAcquire(Barrot));
        first.Release();
        Assert.Null(gate.TryAcquire(Barrot));
        second.Release();
        Assert.NotNull(gate.TryAcquire(Barrot));
    }
}

