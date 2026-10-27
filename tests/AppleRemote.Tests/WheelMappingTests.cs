// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

/// <summary>
/// Wheel mappings are encoded as named, non-VK actions and tested without
/// making actual Windows SendInput calls or opening a Bluetooth device.
/// </summary>
public sealed class WheelMappingTests
{
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
    public void Wheel_mapping_emits_one_down_action_per_distinct_physical_press()
    {
        var sink = new FakeKeySink();
        var binder = new KeyBinder(
            KeyMap.FromJson("""{"up":"ScrollUp","down":"ScrollDown"}"""), sink);
        binder.OnEvent(Buttons(["up"], []));
        binder.OnEvent(Buttons(["up"], [])); // repeated HID report: not a new scroll
        binder.OnEvent(Buttons([], ["up"]));
        binder.OnEvent(Buttons(["down"], []));
        binder.OnEvent(Buttons([], ["down"]));
        Assert.Equal(new[]
        {
            (WheelActions.Up, true), (WheelActions.Up, false),
            (WheelActions.Down, true), (WheelActions.Down, false)
        }, sink.Events);
        // KeyboardInjector consumes the above as a mouse wheel on each down
        // and no OS event on the corresponding up.
    }

    [Fact]
    public void Rejected_wheel_down_is_not_recorded_as_held_and_can_be_retried()
    {
        var sink = new FakeKeySink { RejectFirstScrollUp = 1 };
        var binder = new KeyBinder(KeyMap.FromJson("""{"up":"ScrollUp"}"""), sink);
        binder.OnEvent(Buttons(["up"], [])); // first SendInput failed, no owner
        binder.OnEvent(Buttons([], ["up"]));
        Assert.Empty(sink.Events);
        binder.OnEvent(Buttons(["up"], []));
        binder.OnEvent(Buttons([], ["up"]));
        Assert.Equal(new[]
        {
            (WheelActions.Up, true), (WheelActions.Up, false)
        }, sink.Events);
        Assert.True(binder.ReleaseAll());
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
