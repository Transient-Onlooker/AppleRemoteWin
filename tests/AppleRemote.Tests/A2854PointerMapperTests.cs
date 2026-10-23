// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854PointerMapperTests
{
    sealed class FakePointer : IRelativePointerSink
    {
        public List<(int X, int Y)> Moves { get; } = [];
        public List<bool> Clicks { get; } = [];
        public int FailedReleases { get; set; }
        public int FailedPresses { get; set; }
        public void Move(int dx, int dy) => Moves.Add((dx, dy));
        public bool LeftButton(bool down)
        {
            if (down && FailedPresses-- > 0) return false;
            if (!down && FailedReleases-- > 0) return false;
            Clicks.Add(down);
            return true;
        }
    }

    static TouchEvent Touch(int x, int y, bool down, params Finger[] fingers) =>
        new(0, x, y, down, fingers, []);

    static Finger Contact(int x, int y) => new(x, y, 4);
    static Finger Hover(int x, int y) => new(x, y, 7);
    static ButtonsEvent Press(params string[] names) => new(0, names, [], []);
    static ButtonsEvent Release(params string[] names) => new(0, [], names, []);

    [Fact]
    public void Failed_mouse_up_keeps_held_state_until_backend_confirms_release()
    {
        var sink = new FakePointer { FailedReleases = 1 };
        var mapper = new A2854PointerMapper(sink);
        mapper.OnEvent(Press("click"));
        Assert.True(mapper.HasHeldClick);
        mapper.OnEvent(Release("click")); // failed OS mouse-up
        Assert.True(mapper.HasHeldClick);
        Assert.Equal(new[] { true }, sink.Clicks);
        Assert.True(mapper.Reset()); // retry mouse-up
        Assert.False(mapper.HasHeldClick);
        Assert.Equal(new[] { true, false }, sink.Clicks);
    }

    [Fact]
    public void Consecutive_mouse_up_failures_keep_state_for_next_cleanup_attempt()
    {
        var sink = new FakePointer { FailedReleases = 4 };
        var mapper = new A2854PointerMapper(sink);
        mapper.OnEvent(Press("click"));
        mapper.OnEvent(Release("click")); // 1st failure
        Assert.False(mapper.Reset()); // next 3 failed attempts
        Assert.True(mapper.HasHeldClick);
        Assert.True(mapper.Reset()); // next try succeeds
        Assert.False(mapper.HasHeldClick);
        Assert.Equal(new[] { true, false }, sink.Clicks);
    }

    [Fact]
    public void Mouse_down_rejected_by_backend_is_not_assumed_held()
    {
        var sink = new FakePointer { FailedPresses = 1 };
        var mapper = new A2854PointerMapper(sink);
        mapper.OnEvent(Press("click"));
        Assert.False(mapper.HasHeldClick);
        mapper.OnEvent(Press("click"));
        Assert.True(mapper.HasHeldClick);
        mapper.OnEvent(Release("click"));
        Assert.Equal(new[] { true, false }, sink.Clicks);
    }

    [Fact]
    public void First_contact_is_anchor_then_relative_motion_not_absolute_cursor_jump()
    {
        var sink = new FakePointer();
        var pointer = new A2854PointerMapper(sink, sensitivity: 0.2);
        pointer.OnEvent(Touch(-1500, 850, true, Contact(-1500, 850)));
        Assert.Empty(sink.Moves);
        pointer.OnEvent(Touch(-1400, 800, true, Contact(-1400, 800)));
        Assert.Equal(new[] { (20, 10) }, sink.Moves);
    }

    [Fact]
    public void Hover_release_and_two_contacts_reset_the_anchor_without_move()
    {
        var sink = new FakePointer();
        var mapper = new A2854PointerMapper(sink, 0.2);
        mapper.OnEvent(Touch(0, 0, false, Hover(0, 0)));
        mapper.OnEvent(Touch(100, 100, true, Contact(100, 100)));
        mapper.OnEvent(Touch(140, 60, true, Contact(140, 60)));
        Assert.Single(sink.Moves);
        mapper.OnEvent(Touch(300, 300, false, Hover(300, 300)));
        mapper.OnEvent(Touch(300, 300, true, Contact(300, 300)));
        mapper.OnEvent(Touch(310, 310, true, Contact(310, 310), Contact(311, 311)));
        mapper.OnEvent(Touch(500, 500, true, Contact(500, 500)));
        mapper.OnEvent(Touch(520, 500, true, Contact(520, 500)));
        Assert.Equal(2, sink.Moves.Count);
        mapper.OnEvent(Touch(520, 500, false));
        mapper.OnEvent(Touch(1800, -1200, true, Contact(1800, -1200)));
        Assert.Equal(2, sink.Moves.Count);
    }

    [Fact]
    public void Large_coordinate_discontinuity_is_discarded_and_does_not_build_up_motion()
    {
        var sink = new FakePointer();
        var mapper = new A2854PointerMapper(sink, 3.0);
        mapper.OnEvent(Touch(0, 0, true, Contact(0, 0)));
        mapper.OnEvent(Touch(500, 500, true, Contact(500, 500))); // rejected jump
        Assert.Empty(sink.Moves);
        mapper.OnEvent(Touch(800, 500, true, Contact(800, 500))); // clamped, backlog discarded
        Assert.Equal((60, 0), Assert.Single(sink.Moves));
        mapper.OnEvent(Touch(800, 500, true, Contact(800, 500)));
        Assert.Single(sink.Moves); // no phantom movement on unchanged location
    }

    [Fact]
    public void Click_is_edge_triggered_and_link_loss_or_disable_releases_it()
    {
        var sink = new FakePointer();
        var mapper = new A2854PointerMapper(sink);
        mapper.OnEvent(Press("click"));
        mapper.OnEvent(Press("click"));
        Assert.Equal(new[] { true }, sink.Clicks);
        mapper.Reset();
        mapper.Reset();
        Assert.Equal(new[] { true, false }, sink.Clicks);
        mapper.OnEvent(Press("click"));
        mapper.OnEvent(Release("click"));
        Assert.Equal(new[] { true, false, true, false }, sink.Clicks);
    }

    [Fact]
    public void Unrelated_media_buttons_do_not_generate_mouse_input()
    {
        var sink = new FakePointer();
        var mapper = new A2854PointerMapper(sink);
        mapper.OnEvent(Press("volup", "playpause", "menu", "power"));
        mapper.OnEvent(Release("volup", "playpause", "menu", "power"));
        Assert.Empty(sink.Moves);
        Assert.Empty(sink.Clicks);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_sensitivity_is_rejected(double sensitivity)
    {
        var sink = new FakePointer();
        Assert.Throws<ArgumentOutOfRangeException>(() => new A2854PointerMapper(sink, sensitivity));
    }

    [Theory]
    [InlineData(0.02)]
    [InlineData(0.18)]
    [InlineData(0.8)]
    [InlineData(3.0)]
    public void GUI_supported_sensitivity_range_matches_core_validation(double value)
    {
        var pointer = new A2854PointerMapper(new FakePointer(), value);
        Assert.Equal(value, pointer.Sensitivity);
        pointer.Sensitivity = value;
        Assert.Equal(value, pointer.Sensitivity);
    }

    [Fact]
    public void Optional_physical_calibration_corrects_sensor_axis_direction_for_cursor()
    {
        var measured = new Dictionary<string, A2854TouchReference>
        {
            ["center"] = new(300, 250),
            ["left"] = new(1200, 250),
            ["right"] = new(-900, 250),
            ["top"] = new(300, -700),
            ["bottom"] = new(300, 1300)
        };
        Assert.True(A2854TouchCalibration.TryCreate(measured, out var calibration));
        var sink = new FakePointer();
        var mapper = new A2854PointerMapper(sink, 0.2);
        mapper.Calibration = calibration;
        mapper.OnEvent(Touch(300, 250, true, Contact(300, 250))); // anchor
        mapper.OnEvent(Touch(180, 155, true, Contact(180, 155))); // physical right and up
        Assert.Equal((20, -20), Assert.Single(sink.Moves));
    }

    [Fact]
    public void Switching_pointer_calibration_resets_anchor_and_never_clicks()
    {
        var measured = new Dictionary<string, A2854TouchReference>
        {
            ["center"] = new(300, 250),
            ["left"] = new(1200, 250),
            ["right"] = new(-900, 250),
            ["top"] = new(300, -700),
            ["bottom"] = new(300, 1300)
        };
        Assert.True(A2854TouchCalibration.TryCreate(measured, out var calibration));
        var sink = new FakePointer();
        var mapper = new A2854PointerMapper(sink, 0.2);
        Assert.Null(mapper.Calibration); // default has no synthesized OS effects
        mapper.OnEvent(Touch(300, 250, true, Contact(300, 250)));
        mapper.Calibration = calibration; // change mapping mid-contact
        mapper.OnEvent(Touch(180, 155, true, Contact(180, 155)));
        Assert.Empty(sink.Moves); // fresh anchor, not a jump
        mapper.OnEvent(Touch(60, 155, true, Contact(60, 155)));
        Assert.Equal((20, 0), Assert.Single(sink.Moves));
        mapper.Calibration = null;
        mapper.OnEvent(Touch(-60, 155, true, Contact(-60, 155)));
        Assert.Single(sink.Moves); // reset anchor again
        Assert.Empty(sink.Clicks);
    }

    [Fact]
    public void Changing_sensitivity_resets_motion_remainders()
    {
        var sink = new FakePointer();
        var mapper = new A2854PointerMapper(sink, 0.03);
        mapper.OnEvent(Touch(0, 0, true, Contact(0, 0)));
        mapper.OnEvent(Touch(10, 0, true, Contact(10, 0)));
        Assert.Empty(sink.Moves); // 0.3px buffered
        mapper.Sensitivity = 0.2;
        mapper.OnEvent(Touch(1000, 200, true, Contact(1000, 200)));
        Assert.Empty(sink.Moves); // new first touch
    }
}
