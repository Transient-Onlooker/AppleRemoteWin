// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 AnthoDingo

using Xunit;

namespace AppleRemote.Tests;

public class KeyTests
{
    sealed class FakeSink : IKeySink
    {
        public readonly List<(KeyCode Key, bool Down)> Log = new();
        public void Key(KeyCode key, bool down) => Log.Add((key, down));
    }

    static readonly KeyCode Up = Keys.Names["Up"], Enter = Keys.Names["Enter"];

    static ButtonsEvent Buttons(int mask, string[] pressed, string[] released) =>
        new(mask, pressed, released, Array.Empty<byte>());

    [Fact]
    public void Key_table_uses_windows_virtual_key_codes()
    {
        Assert.Equal(new KeyCode(0x26, true), Keys.Names["Up"]);
        Assert.Equal(new KeyCode(0x0D), Keys.Names["Enter"]);
        Assert.Equal(new KeyCode(0x41), Keys.Names["a"]);          // case-insensitive
        Assert.Equal(new KeyCode(0x30), Keys.Names["D0"]);
        Assert.Equal(new KeyCode(0x70), Keys.Names["F1"]);
        Assert.Equal(new KeyCode(0x7B), Keys.Names["F12"]);
        Assert.Equal(new KeyCode(0xB3), Keys.Names["MediaPlayPause"]);
    }

    [Fact]
    public void Default_map()
    {
        var m = KeyMap.Default();
        Assert.True(m.TryGet("up", out var up) && up == Up);
        Assert.True(m.TryGet("click", out var click) && click == Enter);
        Assert.True(m.TryGet("menu", out var menu) && menu == Keys.Names["Escape"]);
        Assert.True(m.TryGet("tv", out var tv) && tv == Keys.Names["BrowserHome"]);
        Assert.False(m.TryGet("siri", out _));                      // Siri: nothing by default
        Assert.True(m.TryGet("mute", out var mute) && mute == Keys.Names["VolumeMute"]);
    }

    [Fact]
    public void ToJson_round_trips()
    {
        var m = KeyMap.Default();
        m.Set("siri", Keys.Names["C"]);
        m.Set("tv", null);
        var back = KeyMap.FromJson(m.ToJson());
        Assert.True(back.TryGet("siri", out var siri) && siri == Keys.Names["C"]);
        Assert.False(back.TryGet("tv", out _));
        Assert.True(back.TryGet("menu", out var menu) && menu == Keys.Names["Escape"]);
    }

    [Fact]
    public void Json_overrides_defaults()
    {
        var m = KeyMap.FromJson("""{"siri": "C", "tv": null, "click": 32, "Volup": "VolumeMute"}""");
        Assert.True(m.TryGet("siri", out var siri) && siri == Keys.Names["C"]);
        Assert.True(m.TryGet("click", out var click) && click == new KeyCode(32));
        Assert.True(m.TryGet("volup", out var volup) && volup == Keys.Names["VolumeMute"]);
        Assert.False(m.TryGet("tv", out _));                        // null: input disabled
        Assert.True(m.TryGet("up", out var up) && up == Up);        // the rest keeps its default
    }

    [Theory]
    [InlineData("""{"jump": "A"}""")]
    [InlineData("""{"click": "NOPE"}""")]
    [InlineData("""{"click": true}""")]
    [InlineData("""{"click": [1]}""")]
    [InlineData("""{"click": 0}""")]
    [InlineData("""{"click": 255}""")]
    [InlineData("""["click"]""")]
    public void Invalid_json_is_rejected(string json) =>
        Assert.Throws<ArgumentException>(() => KeyMap.FromJson(json));

    [Fact]
    public void A2854_mute_is_configurable_and_json_round_trips()
    {
        var map = KeyMap.Default();
        Assert.Contains("mute", KeyMap.Inputs);
        Assert.Equal(Keys.Names["VolumeMute"], map.Entries["mute"]);
        map.Set("mute", Keys.Names["M"]);
        var reloaded = KeyMap.FromJson(map.ToJson());
        Assert.Equal(Keys.Names["M"], reloaded.Entries["mute"]);
        reloaded.Set("mute", null);
        Assert.False(KeyMap.FromJson(reloaded.ToJson()).TryGet("mute", out _));
    }

    [Fact]
    public void A2854_mute_press_and_release_injects_once_when_key_sink_enabled()
    {
        var sink = new FakeSink();
        var binder = new KeyBinder(KeyMap.Default(), sink);
        var mute = Keys.Names["VolumeMute"];
        binder.OnEvent(Buttons(0x0080, ["mute"], []));
        binder.OnEvent(Buttons(0x0080, [], [])); // duplicate state must not repeat
        binder.OnEvent(Buttons(0, [], ["mute"]));
        Assert.Equal(new[] { (mute, true), (mute, false) }, sink.Log);
    }

    [Fact]
    public void Existing_json_without_mute_gets_default_mute_mapping()
    {
        var map = KeyMap.FromJson("""{"click":"Space","menu":"Escape"}""");
        Assert.Equal(Keys.Names["VolumeMute"], map.Entries["mute"]);
        Assert.Equal(Keys.Names["Space"], map.Entries["click"]);
    }

    [Fact]
    public void Binder_translates_buttons_and_steps()
    {
        var sink = new FakeSink();
        var binder = new KeyBinder(KeyMap.Default(), sink);

        binder.OnEvent(Buttons(0x80, new[] { "click" }, Array.Empty<string>()));
        binder.OnEvent(Buttons(0x10, new[] { "siri" }, new[] { "click" }));   // siri: not mapped
        binder.OnGesture(new MoveGesture(Direction.Up, MoveSource.Slide));

        Assert.Equal(new[] { (Enter, true), (Enter, false), (Up, true), (Up, false) }, sink.Log);
    }

    [Fact]
    public void Binder_ignores_non_move_gestures_and_unmapped_directions()
    {
        var sink = new FakeSink();
        var map = KeyMap.FromJson("""{"left": null}""");
        var binder = new KeyBinder(map, sink);

        binder.OnGesture(new TouchGesture(true));
        binder.OnGesture(new TiltGesture(.5, .5));
        binder.OnGesture(new MoveGesture(Direction.Left, MoveSource.Flick));

        Assert.Empty(sink.Log);
    }

    [Fact]
    public void ReleaseAll_lifts_held_keys_once()
    {
        var sink = new FakeSink();
        var binder = new KeyBinder(KeyMap.Default(), sink);
        binder.OnEvent(Buttons(0x80, new[] { "click" }, Array.Empty<string>()));

        binder.ReleaseAll();
        binder.ReleaseAll();

        Assert.Equal(new[] { (Enter, true), (Enter, false) }, sink.Log);
    }

    [Fact]
    public void Changing_the_map_releases_held_keys()
    {
        var sink = new FakeSink();
        var binder = new KeyBinder(KeyMap.Default(), sink);
        binder.OnEvent(Buttons(0x80, new[] { "click" }, Array.Empty<string>()));

        binder.Map = KeyMap.FromJson("""{"click": "Space"}""");
        binder.OnEvent(Buttons(0, Array.Empty<string>(), new[] { "click" }));   // already released: no double release

        Assert.Equal(new[] { (Enter, true), (Enter, false) }, sink.Log);
    }
    sealed class RejectingSink : IConfirmedKeySink
    {
        public int FailedUps { get; set; }
        public int FailedDowns { get; set; }
        public readonly List<(KeyCode Key, bool Down)> Accepted = new();
        public void Key(KeyCode key, bool down) => _ = TryKey(key, down);
        public bool TryKey(KeyCode key, bool down)
        {
            if (!down && FailedUps-- > 0) return false;
            if (down && FailedDowns-- > 0) return false;
            Accepted.Add((key, down));
            return true;
        }
    }

    [Fact]
    public void Rejected_ordinary_keyup_is_retryable_without_duplicate_down()
    {
        var sink = new RejectingSink { FailedUps = 1 };
        var binder = new KeyBinder(KeyMap.FromJson("""{"click":"F5"}"""), sink);
        binder.OnEvent(Buttons(8, ["click"], []));
        binder.OnEvent(Buttons(0, [], ["click"])); // failed key-up
        Assert.True(binder.HasPendingReleases);
        binder.OnEvent(Buttons(8, ["click"], [])); // retries old up, then new down
        Assert.False(binder.HasPendingReleases);
        binder.OnEvent(Buttons(0, [], ["click"]));
        Assert.Equal(new[]
        {
            (Keys.Names["F5"], true), (Keys.Names["F5"], false),
            (Keys.Names["F5"], true), (Keys.Names["F5"], false)
        }, sink.Accepted);
    }

    [Fact]
    public void Rejected_keyup_on_map_change_uses_original_virtual_key_at_stop()
    {
        var sink = new RejectingSink { FailedUps = 2 };
        var binder = new KeyBinder(KeyMap.FromJson("""{"click":"F5"}"""), sink);
        binder.OnEvent(Buttons(8, ["click"], []));
        binder.Map = KeyMap.FromJson("""{"click":"Space"}"""); // first failure
        Assert.True(binder.HasPendingReleases);
        Assert.False(binder.ReleaseAll()); // second failure during Stop
        Assert.True(binder.HasPendingReleases);
        Assert.True(binder.ReleaseAll());
        Assert.False(binder.HasPendingReleases);
        Assert.Equal(new[] { (Keys.Names["F5"], true), (Keys.Names["F5"], false) }, sink.Accepted);
        binder.OnEvent(Buttons(8, ["click"], []));
        Assert.Equal((Keys.Names["Space"], true), sink.Accepted[^1]);
    }

    [Fact]
    public void Keyboard_off_release_failure_remains_for_later_stop_retry()
    {
        var sink = new RejectingSink { FailedUps = 1 };
        var binder = new KeyBinder(KeyMap.Default(), sink);
        binder.OnEvent(Buttons(8, ["click"], []));
        Assert.False(binder.ReleaseAll()); // KeyboardEnabled = false
        Assert.True(binder.HasPendingReleases);
        Assert.True(binder.ReleaseAll()); // Stop, even when keyboard is off
        Assert.Equal(new[] { (Enter, true), (Enter, false) }, sink.Accepted);
    }

    [Fact]
    public void Rejected_key_down_is_not_recorded_as_held()
    {
        var sink = new RejectingSink { FailedDowns = 1 };
        var binder = new KeyBinder(KeyMap.Default(), sink);
        binder.OnEvent(Buttons(8, ["click"], []));
        Assert.True(binder.ReleaseAll());
        binder.OnEvent(Buttons(8, ["click"], []));
        Assert.True(binder.ReleaseAll());
        Assert.Equal(new[] { (Enter, true), (Enter, false) }, sink.Accepted);
    }

    [Fact]
    public void Rejected_gesture_keyup_can_be_retried_during_stop()
    {
        var sink = new RejectingSink { FailedUps = 1 };
        var binder = new KeyBinder(KeyMap.Default(), sink);
        binder.OnGesture(new MoveGesture(Direction.Up, MoveSource.Slide));
        Assert.True(binder.HasPendingReleases);
        Assert.True(binder.ReleaseAll());
        Assert.Equal(new[] { (Up, true), (Up, false) }, sink.Accepted);
    }
}


