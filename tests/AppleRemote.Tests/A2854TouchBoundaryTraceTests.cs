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

    [Fact]
    public void Incident_captures_first_gap_pre_and_post_window_then_remains_until_cleared()
    {
        var trace = new A2854TouchBoundaryTrace();
        trace.SetIncidentCaptureEnabled(true);
        Record(trace, 100, 0);
        Record(trace, 101, 1000);
        Record(trace, 102, 2000);
        Record(trace, 99, 2001);
        var first = trace.SnapshotIncident(TimeSpan.FromMilliseconds(2001))!;
        Assert.True(first.Capturing);
        Assert.Equal(A2854TraceIncidentTrigger.BackwardOrLargeGap, first.Trigger);
        Assert.Equal(3, first.Lines.Count);
        Assert.Contains("report=2 offset_ms=-1001", first.Lines[0]);
        Assert.Contains("report=4 offset_ms=0", first.Lines[2]);
        Record(trace, 103, 3001);
        Record(trace, 104, 3002);
        var complete = trace.SnapshotIncident(TimeSpan.FromMilliseconds(6000))!;
        Assert.False(complete.Capturing);
        Assert.Equal(4, complete.Lines.Count);
        Assert.Contains("report=5 offset_ms=1000", complete.Lines[3]);
        Record(trace, 100, 6001);
        Assert.Equal(complete.Lines, trace.SnapshotIncident(TimeSpan.FromMilliseconds(6001))!.Lines);
        trace.ClearIncident();
        Assert.Null(trace.SnapshotIncident(TimeSpan.FromMilliseconds(6001)));
        Record(trace, 99, 6002);
        Assert.NotNull(trace.SnapshotIncident(TimeSpan.FromMilliseconds(6002)));
    }

    [Fact]
    public void Acceptance_trigger_is_opt_in_and_fault_does_not_trigger()
    {
        var trace = new A2854TouchBoundaryTrace();
        void Add(ushort sequence, int ms, A2854MoveAcceptance acceptance) => trace.Record(sequence,
            TimeSpan.FromMilliseconds(ms), A2854TraceContact.Single, A2854TouchObservationKind.Move,
            A2854TraceSign.Positive, A2854TraceSign.Zero, acceptance: acceptance);
        Add(1, 0, A2854MoveAcceptance.Rejected);
        Assert.Null(trace.SnapshotIncident(TimeSpan.Zero));
        trace.SetIncidentCaptureEnabled(true);
        Add(2, 1, A2854MoveAcceptance.Faulted);
        Assert.Null(trace.SnapshotIncident(TimeSpan.FromMilliseconds(1)));
        Add(3, 2, A2854MoveAcceptance.Rejected);
        var incident = trace.SnapshotIncident(TimeSpan.FromMilliseconds(2))!;
        Assert.Equal(A2854TraceIncidentTrigger.RejectedPointerMove, incident.Trigger);
        Assert.Contains("acceptance=faulted", incident.Lines[1]);
        Assert.Contains("acceptance=rejected", incident.Lines[2]);
        trace.SetIncidentCaptureEnabled(false);
        Add(4, 3, A2854MoveAcceptance.Rejected);
        Assert.False(trace.SnapshotIncident(TimeSpan.FromMilliseconds(3))!.Capturing);
        Assert.Equal(3, trace.SnapshotIncident(TimeSpan.FromMilliseconds(3))!.Lines.Count);
        trace.Reset();
        Assert.Null(trace.SnapshotIncident(TimeSpan.Zero));
        Add(1, 0, A2854MoveAcceptance.Rejected);
        Assert.Null(trace.SnapshotIncident(TimeSpan.Zero));
    }

    [Fact]
    public void Incident_capacity_preserves_first_entries_and_flags_truncation()
    {
        var trace = new A2854TouchBoundaryTrace();
        trace.SetIncidentCaptureEnabled(true);
        for (int i = 0; i < 512; i++) Record(trace, (ushort)i, i);
        Record(trace, 0, 512);
        for (int i = 0; i < 300; i++) Record(trace, (ushort)(512 + i), 513 + i);
        var incident = trace.SnapshotIncident(TimeSpan.FromMilliseconds(2000))!;
        Assert.Equal(A2854TouchBoundaryTrace.IncidentCapacity, incident.Lines.Count);
        Assert.True(incident.Truncated);
        Assert.Contains("report=1 ", incident.Lines[0]);
        Assert.Contains("report=768 ", incident.Lines[^1]);
        Assert.False(incident.Capturing);
    }

    [Fact]
    public void Wrap_is_forward_and_concurrent_recording_keeps_delivery_ids_and_safe_output()
    {
        var trace = new A2854TouchBoundaryTrace();
        trace.SetIncidentCaptureEnabled(true);
        Record(trace, 65535, 0);
        Record(trace, 0, 1);
        Assert.Null(trace.SnapshotIncident(TimeSpan.FromMilliseconds(1)));
        trace.Record(1, TimeSpan.FromMilliseconds(2), A2854TraceContact.Hover,
            A2854TouchObservationKind.Move, A2854TraceSign.Negative, A2854TraceSign.Zero,
            acceptance: A2854MoveAcceptance.Rejected);
        Parallel.For(0, 100, i => trace.Record(1, TimeSpan.FromMilliseconds(3 + i),
            A2854TraceContact.Single, A2854TouchObservationKind.Move,
            A2854TraceSign.Zero, A2854TraceSign.Positive));
        var incident = trace.SnapshotIncident(TimeSpan.FromMilliseconds(1002))!;
        Assert.Equal(103, incident.Lines.Count);
        for (int i = 0; i < incident.Lines.Count; i++)
            Assert.StartsWith($"report={i + 1} ", incident.Lines[i]);
        string output = string.Join('\n', incident.Lines);
        Assert.DoesNotContain("65535", output);
        Assert.DoesNotContain("address", output);
        Assert.DoesNotContain("pressure", output);
        Assert.DoesNotContain("key", output);
        Assert.DoesNotContain("payload", output);
    }
}
