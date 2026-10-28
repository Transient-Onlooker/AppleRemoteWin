// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854PowerStopCoordinatorTests
{
    [Fact]
    public void Requests_are_coalesced_and_stop_is_dispatched_once()
    {
        bool running = true;
        var queued = new Queue<Action>();
        int stops = 0;
        using var coordinator = new A2854PowerStopCoordinator(
            () => running, queued.Enqueue, () => { stops++; running = false; });
        coordinator.RequestStop();
        coordinator.RequestStop();
        Assert.Single(queued);
        Assert.Equal(0, stops);
        queued.Dequeue()();
        Assert.Equal(1, stops);
        coordinator.RequestStop();
        Assert.Empty(queued);
    }

    [Fact]
    public void Rechecks_state_on_ui_callback_and_supports_later_running_session()
    {
        bool running = true;
        var queued = new Queue<Action>();
        int stops = 0;
        using var coordinator = new A2854PowerStopCoordinator(() => running, queued.Enqueue, () => stops++);
        coordinator.RequestStop();
        running = false;
        queued.Dequeue()();
        Assert.Equal(0, stops);
        running = true;
        coordinator.RequestStop();
        queued.Dequeue()();
        Assert.Equal(1, stops);
    }

    [Fact]
    public void Dispose_suppresses_queued_callback()
    {
        bool running = true;
        var queued = new Queue<Action>();
        int stops = 0;
        var coordinator = new A2854PowerStopCoordinator(() => running, queued.Enqueue, () => stops++);
        coordinator.RequestStop();
        coordinator.Dispose();
        queued.Dequeue()();
        Assert.Equal(0, stops);
    }

    [Fact]
    public void Non_a2854_or_stopped_session_does_not_schedule()
    {
        bool isA2854 = false, running = true;
        var queued = new Queue<Action>();
        using var coordinator = new A2854PowerStopCoordinator(
            () => isA2854 && running, queued.Enqueue, () => throw new Xunit.Sdk.XunitException("unexpected stop"));
        coordinator.RequestStop();
        isA2854 = true;
        running = false;
        coordinator.RequestStop();
        Assert.Empty(queued);
    }

    [Fact]
    public void Delayed_stop_does_not_stop_a_new_session()
    {
        long generation = 1;
        var queued = new Queue<Action>();
        int stops = 0;
        using var coordinator = new A2854PowerStopCoordinator(() => true, queued.Enqueue, () => stops++, () => generation);
        coordinator.RequestStop();
        generation++;
        queued.Dequeue()();
        Assert.Equal(0, stops);
        coordinator.RequestStop();
        queued.Dequeue()();
        Assert.Equal(1, stops);
    }
}
