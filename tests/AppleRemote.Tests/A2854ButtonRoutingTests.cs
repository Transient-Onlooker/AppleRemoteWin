// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854ButtonRoutingTests
{
    sealed class FakeKeys : IKeySink
    {
        public List<(KeyCode Key, bool Down)> Events { get; } = [];
        public void Key(KeyCode key, bool down) => Events.Add((key, down));
    }

    static ButtonsEvent Buttons(int mask, string[] pressed, string[] released) =>
        new(mask, pressed, released, []);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void Physical_click_has_exclusive_pointer_ownership(
        bool pointerEnabled, bool clickAsKeyboard, bool expectedPointerClick)
    {
        Assert.Equal(expectedPointerClick, A2854ButtonRouting.PointerOwnsClick(pointerEnabled, clickAsKeyboard));
        Assert.Equal(!expectedPointerClick,
            A2854ButtonRouting.AllowKeyboardButton("click", pointerEnabled, clickAsKeyboard));
    }

    [Fact]
    public void Pointer_mode_default_filters_click_but_preserves_other_bindings()
    {
        var sink = new FakeKeys();
        var binder = new KeyBinder(KeyMap.Default(), sink);
        binder.OnEvent(A2854ButtonRouting.ForKeyboard(
            Buttons(0x0048, ["click", "menu"], []), pointerEnabled: true, clickAsKeyboard: false));
        binder.OnEvent(A2854ButtonRouting.ForKeyboard(
            Buttons(0, [], ["click", "menu"]), pointerEnabled: true, clickAsKeyboard: false));
        Assert.Equal(new[] { (Keys.Names["Escape"], true), (Keys.Names["Escape"], false) }, sink.Events);
    }

    [Fact]
    public void Pointer_movement_mode_can_send_custom_center_key_without_mouse_click()
    {
        var sink = new FakeKeys();
        var binder = new KeyBinder(KeyMap.FromJson("""{"click":"F5"}"""), sink);
        Assert.False(A2854ButtonRouting.PointerOwnsClick(true, true));
        binder.OnEvent(A2854ButtonRouting.ForKeyboard(
            Buttons(0x0008, ["click"], []), pointerEnabled: true, clickAsKeyboard: true));
        binder.OnEvent(A2854ButtonRouting.ForKeyboard(
            Buttons(0x0008, [], []), pointerEnabled: true, clickAsKeyboard: true));
        binder.OnEvent(A2854ButtonRouting.ForKeyboard(
            Buttons(0, [], ["click"]), pointerEnabled: true, clickAsKeyboard: true));
        Assert.Equal(new[] { (Keys.Names["F5"], true), (Keys.Names["F5"], false) }, sink.Events);
    }

    [Fact]
    public void Pointer_off_keeps_existing_center_keyboard_mapping()
    {
        var sink = new FakeKeys();
        var binder = new KeyBinder(KeyMap.FromJson("""{"click":"Space"}"""), sink);
        binder.OnEvent(A2854ButtonRouting.ForKeyboard(Buttons(8, ["click"], []), false, false));
        binder.OnEvent(A2854ButtonRouting.ForKeyboard(Buttons(0, [], ["click"]), false, false));
        Assert.Equal(new[] { (Keys.Names["Space"], true), (Keys.Names["Space"], false) }, sink.Events);
    }

    [Fact]
    public void Sensitive_A2854_buttons_remain_display_only_in_both_modes()
    {
        foreach (bool clickAsKeyboard in new[] { false, true })
        {
            var b = A2854ButtonRouting.ForKeyboard(
                Buttons(0x31, ["tv", "siri", "power", "click"], ["tv", "siri", "power"]),
                pointerEnabled: true, clickAsKeyboard: clickAsKeyboard);
            Assert.DoesNotContain("tv", b.Pressed);
            Assert.DoesNotContain("siri", b.Pressed);
            Assert.DoesNotContain("power", b.Pressed);
            Assert.Empty(b.Released);
            Assert.Equal(clickAsKeyboard, b.Pressed.Contains("click"));
        }
    }

    [Fact]
    public void Releasing_held_key_on_mode_switch_prevents_stuck_input()
    {
        var sink = new FakeKeys();
        var binder = new KeyBinder(KeyMap.FromJson("""{"click":"F6"}"""), sink);
        binder.OnEvent(A2854ButtonRouting.ForKeyboard(Buttons(8, ["click"], []), true, true));
        binder.ReleaseAll(); // RemoteSession route setter must do this before switching
        binder.OnEvent(A2854ButtonRouting.ForKeyboard(Buttons(0, [], ["click"]), true, false));
        Assert.Equal(new[] { (Keys.Names["F6"], true), (Keys.Names["F6"], false) }, sink.Events);
    }
}
