// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class MouseMappingTests
{
    sealed class Backend : IConfirmedKeySink, IRelativePointerSink
    {
        public readonly List<(KeyCode, bool)> Events = [];
        public int RefuseUps;
        public void Key(KeyCode key, bool down) => _ = TryKey(key, down);
        public bool TryKey(KeyCode key, bool down)
        {
            if (!down && RefuseUps > 0) { RefuseUps--; return false; }
            Events.Add((key, down));
            return true;
        }
        public void Move(int dx, int dy) { }
        public bool LeftButton(bool down) => throw new InvalidOperationException("Click must use the shared producer");
    }

    static ButtonsEvent Button(string[] pressed, string[] released) => new(0, pressed, released, []);

    [Fact]
    public void Mouse_names_roundtrip_and_do_not_overlap_wheel_or_vk()
    {
        var map = KeyMap.FromJson("""{"up":"MouseLeft","down":"MouseRight"}""");
        var loaded = KeyMap.FromJson(map.ToJson());
        Assert.True(loaded.TryGet("up", out var left));
        Assert.True(loaded.TryGet("down", out var right));
        Assert.Equal(MouseActions.Left, left);
        Assert.Equal(MouseActions.Right, right);
        Assert.True(left.Vk > 254 && right.Vk > 254);
        Assert.NotEqual(left, right);
        Assert.NotEqual(left, WheelActions.Up);
        Assert.NotEqual(right, WheelActions.Down);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pointer_and_mapped_click_keep_button_down_until_last_owner(bool pointerFirst)
    {
        var backend = new Backend();
        var shared = new SharedKeyOwnershipSink(backend);
        var pointer = new A2854PointerMapper(new SharedPointerSink(backend, shared.CreateProducer()));
        var binder = new KeyBinder(KeyMap.FromJson("""{"up":"MouseLeft"}"""), shared.Physical);
        pointer.OnEvent(Button(["click"], []));
        binder.OnEvent(Button(["up"], []));
        if (pointerFirst) pointer.OnEvent(Button([], ["click"]));
        else binder.OnEvent(Button([], ["up"]));
        Assert.Equal(new[] { (MouseActions.Left, true) }, backend.Events);
        if (pointerFirst) binder.OnEvent(Button([], ["up"]));
        else pointer.OnEvent(Button([], ["click"]));
        Assert.Equal(new[] { (MouseActions.Left, true), (MouseActions.Left, false) }, backend.Events);
    }

    [Fact]
    public void Rejected_pointer_up_remains_held_until_reset_retry()
    {
        var backend = new Backend { RefuseUps = 4 };
        var shared = new SharedKeyOwnershipSink(backend);
        var pointer = new A2854PointerMapper(new SharedPointerSink(backend, shared.CreateProducer()));
        pointer.OnEvent(Button(["click"], []));
        pointer.OnEvent(Button([], ["click"]));
        Assert.True(pointer.HasHeldClick);
        Assert.False(pointer.Reset());
        Assert.True(pointer.HasHeldClick);
        Assert.True(pointer.Reset());
        Assert.False(pointer.HasHeldClick);
        Assert.Equal(new[] { (MouseActions.Left, true), (MouseActions.Left, false) }, backend.Events);
    }

    [Fact]
    public void Mapped_right_release_retries_after_refusal()
    {
        var backend = new Backend { RefuseUps = 1 };
        var binder = new KeyBinder(KeyMap.FromJson("""{"up":"MouseRight"}"""), new SharedKeyOwnershipSink(backend).Physical);
        binder.OnEvent(Button(["up"], []));
        binder.OnEvent(Button([], ["up"]));
        Assert.True(binder.HasPendingReleases);
        Assert.True(binder.ReleaseAll());
        Assert.Equal(new[] { (MouseActions.Right, true), (MouseActions.Right, false) }, backend.Events);
    }
}
