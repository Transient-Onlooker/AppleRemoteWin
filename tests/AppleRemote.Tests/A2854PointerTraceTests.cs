// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854PointerTraceTests
{
    sealed class FakePointer : IRelativePointerSink
    {
        public List<(int X, int Y)> Moves { get; } = [];
        public List<bool> Clicks { get; } = [];
        public void Move(int dx, int dy) => Moves.Add((dx, dy));
        public bool LeftButton(bool down) { Clicks.Add(down); return true; }
    }

    static TouchEvent Touch(int x, int y) => new(0, x, y, true, [new Finger(x, y, 4)], []);

    static TouchEvent Pressure(int x, byte pressure)
    {
        int packed = x & 0xFFF;
        byte[] raw = [0xFC, 0x32, 0, 0, 1, (byte)packed, (byte)(packed >> 8), 0, 0x40, 0x20, pressure, 0];
        return new(0, x, 0, true, [new Finger(x, 0, 4)], raw);
    }

    static void Expect(A2854PointerMapper mapper, A2854TouchObservationKind kind, int x = 0, int y = 0) =>
        Assert.Equal(new A2854TouchObservation(kind, x, y), mapper.LastTouchObservation);

    [Fact]
    public void Touch_outcomes_distinguish_reset_anchor_jump_and_output_signs()
    {
        var sink = new FakePointer();
        var mapper = new A2854PointerMapper(sink, 0.2);
        Expect(mapper, A2854TouchObservationKind.None);
        mapper.OnEvent(Touch(0, 0));
        Expect(mapper, A2854TouchObservationKind.Anchor);
        mapper.OnEvent(Touch(1, 1));
        Expect(mapper, A2854TouchObservationKind.NoPixelMove);
        mapper.OnEvent(Touch(351, -349));
        Expect(mapper, A2854TouchObservationKind.Move, 1, 1);
        Assert.Equal((60, 60), Assert.Single(sink.Moves));
        mapper.OnEvent(Touch(702, -349));
        Expect(mapper, A2854TouchObservationKind.JumpReanchor);
        mapper.OnEvent(Touch(692, -339));
        Expect(mapper, A2854TouchObservationKind.Move, -1, -1);
        Assert.Equal((-2, -2), sink.Moves[^1]);
        mapper.OnEvent(new TouchEvent(0, 0, 0, false, [], []));
        Expect(mapper, A2854TouchObservationKind.TrackingReset);
        mapper.OnEvent(Touch(20, 20));
        Expect(mapper, A2854TouchObservationKind.Anchor);
        mapper.OnEvent(new TouchEvent(0, 0, 0, true, [new Finger(0, 0, 7)], []));
        Expect(mapper, A2854TouchObservationKind.TrackingReset);
        mapper.OnEvent(new TouchEvent(0, 0, 0, true, [new Finger(0, 0, 4), new Finger(1, 1, 4)], []));
        Expect(mapper, A2854TouchObservationKind.TrackingReset);
        Assert.Equal(2, sink.Moves.Count);
    }

    [Fact]
    public void Click_guard_drag_and_post_click_guard_preserve_output()
    {
        var sink = new FakePointer();
        var mapper = new A2854PointerMapper(sink, 0.2);
        mapper.OnEvent(Touch(0, 0));
        mapper.OnEvent(new ButtonsEvent(0, ["click"], [], []));
        Expect(mapper, A2854TouchObservationKind.Anchor);
        mapper.OnEvent(Touch(89, 0));
        Expect(mapper, A2854TouchObservationKind.ClickGuard);
        mapper.OnEvent(Touch(90, 0));
        Expect(mapper, A2854TouchObservationKind.DragStarted);
        mapper.OnEvent(Touch(110, 0));
        Expect(mapper, A2854TouchObservationKind.Move, 1, 0);
        Assert.Equal((4, 0), Assert.Single(sink.Moves));
        mapper.OnEvent(new ButtonsEvent(0, [], ["click"], []));
        Expect(mapper, A2854TouchObservationKind.Move, 1, 0);
        mapper.OnEvent(Touch(120, 0));
        Expect(mapper, A2854TouchObservationKind.Anchor);
        mapper.OnEvent(Touch(130, 0));
        Expect(mapper, A2854TouchObservationKind.PostClickGuard);
        mapper.OnEvent(Touch(140, 0));
        Expect(mapper, A2854TouchObservationKind.PostClickGuard);
        mapper.OnEvent(Touch(150, 0));
        Expect(mapper, A2854TouchObservationKind.Move, 1, 0);
        Assert.Equal((2, 0), sink.Moves[^1]);
        Assert.Equal(new[] { true, false }, sink.Clicks);
    }

    [Fact]
    public void Pressure_guard_reports_both_guard_causes_when_they_overlap()
    {
        var sink = new FakePointer();
        var mapper = new A2854PointerMapper(sink, 0.2);
        mapper.OnEvent(Pressure(0, 9));
        mapper.OnEvent(Pressure(10, 33));
        Expect(mapper, A2854TouchObservationKind.PressureGuard);
        mapper.OnEvent(Pressure(20, 33));
        Expect(mapper, A2854TouchObservationKind.PressureGuard);
        mapper.OnEvent(Pressure(30, 33));
        Expect(mapper, A2854TouchObservationKind.Move, 1, 0);
        mapper.OnEvent(new ButtonsEvent(0, ["click"], [], []));
        mapper.OnEvent(new ButtonsEvent(0, [], ["click"], []));
        mapper.OnEvent(Pressure(40, 9));
        mapper.OnEvent(Pressure(50, 33));
        Expect(mapper, A2854TouchObservationKind.PressureAndPostClickGuard);
        mapper.OnEvent(Pressure(60, 33));
        Expect(mapper, A2854TouchObservationKind.PressureAndPostClickGuard);
        mapper.OnEvent(Pressure(70, 33));
        Expect(mapper, A2854TouchObservationKind.Move, 1, 0);
        Assert.Equal(new[] { (2, 0), (2, 0) }, sink.Moves);
    }
}
