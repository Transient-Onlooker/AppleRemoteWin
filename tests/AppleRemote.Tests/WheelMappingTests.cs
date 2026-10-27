// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

/// <summary>
/// Wheel mappings are encoded as named, non-VK actions and tested without
/// making actual Windows SendInput calls or opening a Bluetooth device.
/// </summary>
public sealed class WheelMappingTests
{
    sealed class FakeWheelScheduler : IWheelRepeatScheduler
    {
        public sealed class Scheduled(Action tick, TimeSpan dueTime, TimeSpan period) : IDisposable
        {
            readonly Action _tick = tick;
            public TimeSpan DueTime { get; } = dueTime;
            public TimeSpan Period { get; } = period;
            public bool IsDisposed { get; private set; }
            public void Fire() => _tick(); // deliberately permits a queued callback after cancellation
            public void Dispose() => IsDisposed = true;
        }

        public readonly List<Scheduled> Timers = [];
        public IDisposable Schedule(Action tick, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Scheduled(tick, dueTime, period);
            Timers.Add(timer);
            return timer;
        }
    }

    sealed class FakeKeySink : IConfirmedKeySink
    {
        public readonly List<(KeyCode Key, bool Down)> Events = [];
        public int RejectFirstScrollUp { get; set; }
        public void Key(KeyCode key, bool down) => _ = TryKey(key, down);
        public bool TryKey(KeyCode key, bool down)
        {
            if (down && key == WheelActions.Up && RejectFirstScrollUp-- > 0)
                return false;
            Events.Add((key, down));
            return true;
        }
    }

    static ButtonsEvent Buttons(string[] press, string[] release) =>
        new(0, press, release, []);

    [Fact]
    public void Named_wheel_actions_are_distinct_from_arrow_virtual_keys()
    {
        Assert.Equal(WheelActions.Up, Keys.Names["ScrollUp"]);
        Assert.Equal(WheelActions.Down, Keys.Names["ScrollDown"]);
        Assert.Equal("ScrollUp", Keys.NameOf(WheelActions.Up));
        Assert.Equal("ScrollDown", Keys.NameOf(WheelActions.Down));
        Assert.True(WheelActions.Up.Vk > 254);
        Assert.True(WheelActions.Down.Vk > 254);
        Assert.NotEqual(Keys.Names["Up"], WheelActions.Up);
        Assert.NotEqual(Keys.Names["Down"], WheelActions.Down);
        Assert.False(WheelActions.TryDelta(Keys.Names["Up"], out _));
        Assert.False(WheelActions.TryDelta(Keys.Names["Down"], out _));
        Assert.True(WheelActions.TryDelta(WheelActions.Up, out int up));
        Assert.True(WheelActions.TryDelta(WheelActions.Down, out int down));
        Assert.Equal(120, up);
        Assert.Equal(-120, down);
    }

    [Fact]
    public void Directional_buttons_can_roundtrip_scroll_actions_through_KeyMap_JSON()
    {
        var map = KeyMap.FromJson("""{"up":"ScrollUp","down":"ScrollDown"}""");
        Assert.True(map.TryGet("up", out var up));
        Assert.Equal(WheelActions.Up, up);
        Assert.True(map.TryGet("down", out var down));
        Assert.Equal(WheelActions.Down, down);

        var loaded = KeyMap.FromJson(map.ToJson());
        Assert.True(loaded.TryGet("up", out var loadedUp));
        Assert.Equal(WheelActions.Up, loadedUp);
        Assert.True(loaded.TryGet("down", out var loadedDown));
        Assert.Equal(WheelActions.Down, loadedDown);
        Assert.True(loaded.TryGet("left", out var left));
        Assert.Equal(Keys.Names["Left"], left); // unchanged default
    }

    [Fact]
    public void Wheel_mapping_repeats_only_while_held()
    {
        var sink = new FakeKeySink();
        var scheduler = new FakeWheelScheduler();
        var binder = new KeyBinder(
            KeyMap.FromJson("""{"up":"ScrollUp","down":"ScrollDown"}"""), sink, scheduler);
        binder.OnEvent(Buttons(["up"], []));
        binder.OnEvent(Buttons(["up"], [])); // repeated HID report: not a new scroll
        Assert.Equal(TimeSpan.FromMilliseconds(400), scheduler.Timers[0].DueTime);
        Assert.Equal(TimeSpan.FromMilliseconds(80), scheduler.Timers[0].Period);
        scheduler.Timers[0].Fire();
        binder.OnEvent(Buttons([], ["up"]));
        scheduler.Timers[0].Fire(); // queued callback after release is harmless
        binder.OnEvent(Buttons(["down"], []));
        binder.OnEvent(Buttons([], ["down"]));
        Assert.Equal(new[]
        {
            (WheelActions.Up, true), (WheelActions.Up, true), (WheelActions.Down, true)
        }, sink.Events);
        Assert.True(scheduler.Timers[0].IsDisposed);
    }

    [Fact]
    public void Rejected_wheel_down_starts_no_timer_and_next_press_can_retry()
    {
        var sink = new FakeKeySink { RejectFirstScrollUp = 1 };
        var scheduler = new FakeWheelScheduler();
        var binder = new KeyBinder(KeyMap.FromJson("""{"up":"ScrollUp"}"""), sink, scheduler);
        binder.OnEvent(Buttons(["up"], [])); // first action fails
        Assert.Empty(scheduler.Timers);
        binder.OnEvent(Buttons([], ["up"]));
        Assert.Empty(sink.Events);
        binder.OnEvent(Buttons(["up"], []));
        Assert.Single(scheduler.Timers);
        binder.OnEvent(Buttons([], ["up"]));
        Assert.Equal(new[]
        {
            (WheelActions.Up, true)
        }, sink.Events);
        Assert.True(binder.ReleaseAll());
    }

    [Fact]
    public void Simultaneous_buttons_and_shared_producers_each_emit_one_step()
    {
        var sink = new FakeKeySink();
        var shared = new SharedKeyOwnershipSink(sink);
        var scheduler = new FakeWheelScheduler();
        var first = new KeyBinder(KeyMap.FromJson("""{"up":"ScrollUp","down":"ScrollUp"}"""), shared.Physical, scheduler);
        var second = new KeyBinder(KeyMap.FromJson("""{"up":"ScrollUp"}"""), shared.CreateProducer(), scheduler);
        first.OnEvent(Buttons(["up", "down"], []));
        first.OnEvent(Buttons(["up", "down"], []));
        second.OnEvent(Buttons(["up"], []));
        first.OnEvent(Buttons([], ["up", "down"]));
        second.OnEvent(Buttons([], ["up"]));
        Assert.Equal(3, sink.Events.Count);
        Assert.All(sink.Events, e => Assert.Equal((WheelActions.Up, true), e));
    }

    [Fact]
    public void Held_wheel_buttons_repeat_independently_and_release_all_cancels_them()
    {
        var sink = new FakeKeySink();
        var scheduler = new FakeWheelScheduler();
        var binder = new KeyBinder(KeyMap.FromJson("""{"up":"ScrollUp","down":"ScrollDown"}"""), sink, scheduler);
        binder.OnEvent(Buttons(["up", "down"], []));
        scheduler.Timers[0].Fire();
        Assert.Equal(3, sink.Events.Count);

        binder.OnEvent(Buttons([], ["up"]));
        scheduler.Timers[0].Fire();
        scheduler.Timers[1].Fire();
        Assert.Equal(new[]
        {
            (WheelActions.Up, true), (WheelActions.Down, true),
            (WheelActions.Up, true), (WheelActions.Down, true)
        }, sink.Events);

        Assert.True(binder.ReleaseAll()); // RemoteSession.Stop and Keyboard OFF use this cleanup path
        scheduler.Timers[1].Fire();
        Assert.Equal(4, sink.Events.Count);
        Assert.All(scheduler.Timers, timer => Assert.True(timer.IsDisposed));
    }

    [Fact]
    public void Remapping_while_wheel_is_held_cancels_queued_repeats()
    {
        var sink = new FakeKeySink();
        var scheduler = new FakeWheelScheduler();
        var binder = new KeyBinder(KeyMap.FromJson("""{"up":"ScrollUp"}"""), sink, scheduler);
        binder.OnEvent(Buttons(["up"], []));
        binder.Map = KeyMap.FromJson("""{"up":"A"}""");
        scheduler.Timers[0].Fire();
        Assert.Equal(new[] { (WheelActions.Up, true) }, sink.Events);
        Assert.True(scheduler.Timers[0].IsDisposed);
    }

    [Fact]
    public void Old_queued_tick_cannot_repeat_a_new_press_of_the_same_button()
    {
        var sink = new FakeKeySink();
        var scheduler = new FakeWheelScheduler();
        var binder = new KeyBinder(KeyMap.FromJson("""{"up":"ScrollUp"}"""), sink, scheduler);
        binder.OnEvent(Buttons(["up"], []));
        binder.OnEvent(Buttons([], ["up"]));
        binder.OnEvent(Buttons(["up"], []));
        scheduler.Timers[0].Fire();
        Assert.Equal(2, sink.Events.Count);
        scheduler.Timers[1].Fire();
        Assert.Equal(3, sink.Events.Count);
        binder.ReleaseAll();
    }

    [Fact]
    public void Rejected_step_is_not_retried_until_next_press()
    {
        var sink = new FakeKeySink { RejectFirstScrollUp = 1 };
        var binder = new KeyBinder(KeyMap.FromJson("""{"up":"ScrollUp"}"""), new SharedKeyOwnershipSink(sink).Physical, new FakeWheelScheduler());
        binder.OnEvent(Buttons(["up"], []));
        binder.OnEvent(Buttons(["up"], []));
        Assert.Empty(sink.Events);
        binder.OnEvent(Buttons([], ["up"]));
        binder.OnEvent(Buttons(["up"], []));
        Assert.Equal(new[] { (WheelActions.Up, true) }, sink.Events);
        binder.ReleaseAll();
    }

    [Fact]
    public void Existing_arrow_direction_bindings_remain_keyboard_actions()
    {
        var map = KeyMap.Default();
        Assert.True(map.TryGet("up", out var up));
        Assert.True(map.TryGet("down", out var down));
        Assert.Equal(Keys.Names["Up"], up);
        Assert.Equal(Keys.Names["Down"], down);
        Assert.False(WheelActions.TryDelta(up, out _));
        Assert.False(WheelActions.TryDelta(down, out _));
    }
}
