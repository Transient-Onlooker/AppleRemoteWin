// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854TouchBoundaryTraceTests
{
    sealed class FakePointer : IRelativePointerSink
    {
        public readonly List<(int X, int Y)> Moves = [];
        public void Move(int dx, int dy) => Moves.Add((dx, dy));
        public bool LeftButton(bool down) => true;
    }

    static void Record(A2854TouchBoundaryTrace trace, ushort sequence, double milliseconds,
        A2854TraceContact contact = A2854TraceContact.Single,
        A2854TouchObservationKind outcome = A2854TouchObservationKind.Move,
        A2854TraceSign x = A2854TraceSign.Positive, A2854TraceSign y = A2854TraceSign.Negative) =>
        trace.Record(sequence, TimeSpan.FromMilliseconds(milliseconds), contact, outcome, x, y);

    [Fact]
    public void Snapshot_expires_old_events_even_without_new_reports()
    {
        var trace = new A2854TouchBoundaryTrace();
        Record(trace, 10, 0);
        Record(trace, 11, 1000);
        Assert.Equal(2, trace.Snapshot(TimeSpan.FromMilliseconds(2000)).Count);
        var lines = trace.Snapshot(TimeSpan.FromMilliseconds(2001));
        Assert.Single(lines);
        Assert.Contains("age_ms=1001", lines[0]);
        Assert.Empty(trace.Snapshot(TimeSpan.FromMilliseconds(3001)));
    }

    [Fact]
    public void Decoded_contact_release_and_recontact_link_sequence_to_actual_pointer_outcome()
    {
        var decoder = new A2854Decoder();
        var sink = new FakePointer();
        var mapper = new A2854PointerMapper(sink, 0.2);
        var trace = new A2854TouchBoundaryTrace();
        byte[] Frame(ushort sequence, int x, bool release)
        {
            int packed = x & 0xFFF;
            return [0xFC, 0x32, (byte)sequence, (byte)(sequence >> 8), 1,
                (byte)packed, (byte)(packed >> 8), 0,
                release ? (byte)0 : (byte)64, release ? (byte)0 : (byte)32,
                release ? (byte)0 : (byte)9, release ? (byte)0x67 : (byte)0x6C];
        }
        void Feed(ushort sequence, int x, bool release, int ms)
        {
            byte[] report = Frame(sequence, x, release);
            var frame = Assert.IsType<A2854TouchFrame>(A2854Decoder.ParseTouch(report.AsSpan(1)));
            var touch = Assert.IsType<TouchEvent>(Assert.Single(decoder.Decode(report)));
            mapper.OnEvent(touch);
            var o = mapper.LastTouchObservation;
            trace.Record(frame.Sequence, TimeSpan.FromMilliseconds(ms),
                touch.Down ? A2854TraceContact.Single : A2854TraceContact.Release,
                o.Kind, (A2854TraceSign)o.XSign, (A2854TraceSign)o.YSign);
        }

        Feed(65520, 100, false, 0);
        Feed(14, 150, false, 15);
        Feed(44, 50, true, 30);
        Feed(74, 50, false, 45);
        Feed(104, 100, false, 60);
        var lines = trace.Snapshot(TimeSpan.FromMilliseconds(60));
        Assert.Contains("contact=single outcome=anchor x=0 y=0", lines[0]);
        Assert.Contains("seq=forward delta=30 contact=single outcome=move x=+ y=0", lines[1]);
        Assert.Contains("contact=release outcome=trackingreset x=0 y=0", lines[2]);
        Assert.Contains("contact=single outcome=anchor x=0 y=0", lines[3]);
        Assert.Contains("contact=single outcome=move x=+ y=0", lines[4]);
        Assert.Equal(new[] { (10, 0), (10, 0) }, sink.Moves);
    }

    [Fact]
    public void Ring_overwrites_oldest_and_reset_clears_sequence_and_clock()
    {
        var trace = new A2854TouchBoundaryTrace();
        for (int i = 0; i < A2854TouchBoundaryTrace.Capacity + 2; i++)
            Record(trace, (ushort)i, i);
        var lines = trace.Snapshot(TimeSpan.FromMilliseconds(A2854TouchBoundaryTrace.Capacity + 1));
        Assert.Equal(A2854TouchBoundaryTrace.Capacity, lines.Count);
        Assert.Contains("age_ms=511", lines[0]);
        Assert.DoesNotContain("seq=first", string.Join('\n', lines));
        trace.Reset();
        Assert.Empty(trace.Snapshot(TimeSpan.Zero));
        Record(trace, 123, 0);
        Assert.Contains("seq=first delta=0", Assert.Single(trace.Snapshot(TimeSpan.Zero)));
    }

    [Fact]
    public void Sequence_wrap_duplicate_and_stale_preserve_forward_baseline()
    {
        var trace = new A2854TouchBoundaryTrace();
        Record(trace, 65534, 0);
        Record(trace, 1, 1);
        Record(trace, 1, 2);
        Record(trace, 65535, 3);
        Record(trace, 2, 4);
        Record(trace, 32770, 5);
        var lines = trace.Snapshot(TimeSpan.FromMilliseconds(5));
        Assert.Contains("seq=forward delta=3", lines[1]);
        Assert.Contains("seq=duplicate delta=0", lines[2]);
        Assert.Contains("seq=backward_or_large_gap delta=-2", lines[3]);
        Assert.Contains("seq=forward delta=1", lines[4]);
        Assert.Contains("seq=backward_or_large_gap delta=-32768", lines[5]);
    }

    [Fact]
    public void Output_contains_only_fixed_categories_relative_time_delta_and_signs()
    {
        var trace = new A2854TouchBoundaryTrace();
        Record(trace, 61234, 123456, A2854TraceContact.Hover,
            A2854TouchObservationKind.TrackingReset, A2854TraceSign.Zero, A2854TraceSign.Negative);
        string line = Assert.Single(trace.Snapshot(TimeSpan.FromMilliseconds(123457)));
        Assert.Equal("age_ms=1 seq=first delta=0 contact=hover outcome=trackingreset x=0 y=-", line);
        Assert.DoesNotContain("61234", line);
        Assert.DoesNotContain("123456", line);
        Assert.DoesNotContain("address", line);
        Assert.DoesNotContain("pressure", line);
        Assert.DoesNotContain("key", line);
        Assert.Throws<ArgumentOutOfRangeException>(() => Record(trace, 1, 123458, (A2854TraceContact)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Record(trace, 1, 123458, x: (A2854TraceSign)99));
    }

    [Fact]
    public void Allows_snapshot_and_delayed_arrival_to_cross_without_throwing()
    {
        var trace = new A2854TouchBoundaryTrace();
        Assert.Throws<ArgumentOutOfRangeException>(() => Record(trace, 1, -1));
        Record(trace, 1, 5);
        Assert.Single(trace.Snapshot(TimeSpan.FromMilliseconds(7)));
        trace.Record(2, TimeSpan.FromMilliseconds(4), A2854TraceContact.Single,
            A2854TouchObservationKind.Move, A2854TraceSign.Positive, A2854TraceSign.Zero,
            TimeSpan.FromMilliseconds(8)); // captured before the snapshot, delivered afterward
        var lines = trace.Snapshot(TimeSpan.FromMilliseconds(8));
        Assert.Equal(2, lines.Count);
        Assert.Contains("age_ms=4 seq=forward delta=1", lines[1]);
    }
}
