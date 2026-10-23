// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854SessionTimelineTests
{
    [Fact]
    public void Captures_relative_event_order_without_wall_clock_or_device_identity()
    {
        var log = new A2854SessionTimeline();
        Assert.True(log.Record("A2854: initializing dedicated external Barrot WinUSB controller."));
        Assert.True(log.Record("A2854: encrypted LTK connection established."));
        Assert.True(log.Record("A2854: early ATT request count=1."));
        Assert.True(log.Record(
            "A2854: peer disconnection 0x13 (Remote User Terminated Connection); " +
            "connected=30.1s FB=78 FC=1553 last-input=0.1s ago; host Stop/CCCD disable not yet verified."));
        string report = log.Export();
        Assert.Contains("Peer disconnect=0x13", report);
        Assert.Contains("connected=30.1s", report);
        Assert.Contains("FB=78", report);
        Assert.Contains("FC=1553", report);
        Assert.Contains("Early ATT request count=1", report);
        Assert.DoesNotContain("Remote User Terminated Connection", report);
        Assert.StartsWith("A2854 local connection timeline (redacted)", report);
        Assert.Equal(4, log.Snapshot().Count);
        Assert.All(log.Snapshot(), item => Assert.True(item.Seconds >= 0));
    }

    [Fact]
    public void Rejects_unrecognized_or_appended_private_content_even_with_known_prefix()
    {
        var log = new A2854SessionTimeline();
        Assert.False(log.Record("Peer address 11:22:33:44:55:66"));
        Assert.False(log.Record("LTK: DEADBEEF1234"));
        Assert.True(log.Record("A2854: HCI error phase=connect opcode=0x200D status=0x3E; bond unchanged."));
        Assert.True(log.Record("A2854: early ATT request count=2. private:SECRETKEY"));
        Assert.True(log.Record("A2854: LE connection parameters LinkEstablished; association=unbound " +
            "elapsed=0.00s min=24 max=24 latency=0 timeout=200 status=0x00. " +
            "peer address=11:22:33:44:55:66"));
        string report = log.Export();
        Assert.Contains("status=0x3E", report);
        Assert.DoesNotContain("DEADBEEF1234", report);
        Assert.DoesNotContain("SECRETKEY", report);
        Assert.DoesNotContain("11:22:33:44:55:66", report);
    }

    [Fact]
    public void Bounded_record_count_and_explicit_reset_do_not_retain_previous_session()
    {
        var log = new A2854SessionTimeline();
        for (int n = 0; n < A2854SessionTimeline.Capacity + 14; n++)
            Assert.True(log.Record($"A2854: early ATT request count={n}."));
        var events = log.Snapshot();
        Assert.Equal(A2854SessionTimeline.Capacity, events.Count);
        Assert.DoesNotContain(events, e => e.Event.EndsWith("count=0", StringComparison.Ordinal));
        log.Reset();
        Assert.Empty(log.Snapshot());
        Assert.DoesNotContain("count=61", log.Export());
    }

    [Fact]
    public void Parses_initial_controller_link_timing_without_address_or_key_material()
    {
        var log = new A2854SessionTimeline();
        Assert.True(log.Record(
            "A2854: LE connection parameters LinkEstablished; association=unbound " +
            "elapsed=0.00s min=24 max=24 latency=0 timeout=200 status=0x00."));
        var report = log.Export();
        Assert.Contains("LE LinkEstablished", report);
        Assert.Contains("interval=24..24", report);
        Assert.Contains("timeout=200", report);
        Assert.DoesNotContain("peer", report);
    }
}
