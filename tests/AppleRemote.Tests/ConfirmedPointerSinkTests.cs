// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class ConfirmedPointerSinkTests
{
    sealed class Legacy : IRelativePointerSink
    {
        public readonly List<(int, int)> Moves = [];
        public void Move(int dx, int dy) => Moves.Add((dx, dy));
        public bool LeftButton(bool down) => true;
    }

    sealed class Confirmed(bool accepted, bool fault = false) : IConfirmedRelativePointerSink
    {
        public readonly List<(int, int)> Moves = [];
        public void Move(int dx, int dy) => _ = TryMove(dx, dy);
        public bool TryMove(int dx, int dy)
        {
            Moves.Add((dx, dy));
            if (fault) throw new InvalidOperationException("fake");
            return accepted;
        }
        public bool LeftButton(bool down) => true;
    }

    sealed class Click : IConfirmedKeySink
    {
        public bool TryKey(KeyCode key, bool down) => true;
        public void Key(KeyCode key, bool down) { }
    }

    static TouchEvent Touch(int x, int y) => new(0, x, y, true, [new Finger(x, y, 4)], []);

    [Theory]
    [InlineData(true, A2854MoveAcceptance.Accepted)]
    [InlineData(false, A2854MoveAcceptance.Rejected)]
    public void Confirmed_result_is_reported_without_replay(bool accepted, A2854MoveAcceptance expected)
    {
        var sink = new Confirmed(accepted);
        var mapper = new A2854PointerMapper(new SharedPointerSink(sink, new Click()), 0.2);
        mapper.OnEvent(Touch(0, 0));
        Assert.Equal(A2854MoveAcceptance.NotAttempted, mapper.LastTouchObservation.Acceptance);
        mapper.OnEvent(Touch(10, -10));
        Assert.Equal(new A2854TouchObservation(A2854TouchObservationKind.Move, 1, 1, expected), mapper.LastTouchObservation);
        mapper.OnEvent(Touch(10, -10));
        Assert.Equal(A2854MoveAcceptance.NotAttempted, mapper.LastTouchObservation.Acceptance);
        Assert.Equal((2, 2), Assert.Single(sink.Moves));
    }

    [Fact]
    public void Exceptions_are_recorded_and_rethrown()
    {
        var sink = new Confirmed(false, true);
        var mapper = new A2854PointerMapper(new SharedPointerSink(sink, new Click()), 0.2);
        mapper.OnEvent(Touch(0, 0));
        var error = Assert.Throws<InvalidOperationException>(() => mapper.OnEvent(Touch(-10, 10)));
        Assert.Equal("fake", error.Message);
        Assert.Equal(new A2854TouchObservation(A2854TouchObservationKind.Move, -1, -1, A2854MoveAcceptance.Faulted), mapper.LastTouchObservation);
        Assert.Single(sink.Moves);
    }

    [Fact]
    public void Legacy_and_confirmed_paths_emit_identical_moves_and_legacy_stays_unknown()
    {
        var legacy = new Legacy();
        var confirmed = new Confirmed(true);
        var sharedLegacy = new SharedPointerSink(legacy, new Click());
        Assert.False(sharedLegacy.CanConfirmMovement);
        var oldMapper = new A2854PointerMapper(sharedLegacy, 0.2);
        var newMapper = new A2854PointerMapper(new SharedPointerSink(confirmed, new Click()), 0.2);
        foreach (var point in new[] { (0, 0), (1, 1), (10, -10), (350, -340), (701, -340), (691, -330) })
        {
            oldMapper.OnEvent(Touch(point.Item1, point.Item2));
            newMapper.OnEvent(Touch(point.Item1, point.Item2));
            Assert.Equal(oldMapper.LastTouchObservation.Kind, newMapper.LastTouchObservation.Kind);
            if (oldMapper.LastTouchObservation.Kind == A2854TouchObservationKind.Move)
                Assert.Equal(A2854MoveAcceptance.Unknown, oldMapper.LastTouchObservation.Acceptance);
        }
        Assert.Equal(legacy.Moves, confirmed.Moves);

        var direct = new A2854PointerMapper(legacy, 0.2);
        direct.OnEvent(Touch(0, 0));
        direct.OnEvent(Touch(10, 0));
        Assert.Equal(A2854MoveAcceptance.Unknown, direct.LastTouchObservation.Acceptance);
    }

    [Fact]
    public void Incident_capture_does_not_change_pointer_output()
    {
        var offSink = new Confirmed(true);
        var onSink = new Confirmed(true);
        var off = new A2854PointerMapper(offSink, 0.2);
        var on = new A2854PointerMapper(onSink, 0.2);
        var trace = new A2854TouchBoundaryTrace();
        trace.SetIncidentCaptureEnabled(true);
        for (int i = 0; i < 1000; i++)
        {
            var touch = Touch(i % 200, i % 100);
            off.OnEvent(touch);
            on.OnEvent(touch);
            var observed = on.LastTouchObservation;
            trace.Record((ushort)i, TimeSpan.FromMilliseconds(i * 15), A2854TraceContact.Single,
                observed.Kind, (A2854TraceSign)observed.XSign, (A2854TraceSign)observed.YSign,
                acceptance: observed.Acceptance);
        }
        Assert.Equal(offSink.Moves, onSink.Moves);
    }
}
