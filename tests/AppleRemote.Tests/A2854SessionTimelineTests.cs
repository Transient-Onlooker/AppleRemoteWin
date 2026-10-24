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

    [Theory]
    [InlineData("A2854: early ATT request count=2. private:SECRETKEY", "Early ATT count (details withheld)")]
    [InlineData("A2854: previous USB cleanup failed; SECRET-LTK", "Previous USB cleanup failed")]
    [InlineData("A2854: transport error (Mac=11:22:33:44:55:66)", "A2854 transport error (details withheld)")]
    [InlineData("A2854: dedicated Barrot WinUSB interface unavailable (MAC:11:22:33:44:55:66)", "Dedicated Barrot USB unavailable")]
    public void Copy_result_redaction_never_returns_an_untrusted_suffix(string input, string expected)
    {
        Assert.Equal(expected, A2854SessionTimeline.Redact(input));
        var timeline = new A2854SessionTimeline();
        Assert.True(timeline.Record(input));
        Assert.Contains(expected, timeline.Export());
        Assert.DoesNotContain("SECRET", timeline.Export());
        Assert.DoesNotContain("11:22:33:44:55:66", timeline.Export());
    }

    [Fact]
    public void Exact_numeric_diagnostics_are_preserved_but_appended_data_is_discarded()
    {
        const string safe = "A2854: HCI error phase=connect opcode=0x200D status=0x3E; bond unchanged.";
        Assert.Equal("HCI error phase=connect; opcode=0x200D; status=0x3E",
            A2854SessionTimeline.Redact(safe));

        var injected = safe + " key=DEADBEEF";
        Assert.Equal("HCI error (details withheld)", A2854SessionTimeline.Redact(injected));

        var peer = "A2854: peer disconnection 0x13 (Remote User Terminated Connection); " +
            "connected=30.1s FB=78 FC=1553 last-input=0.1s ago; host Stop/CCCD disable not yet verified.";
        Assert.Contains("Peer disconnect=0x13", A2854SessionTimeline.Redact(peer));
        Assert.Equal("Peer disconnected (details withheld)",
            A2854SessionTimeline.Redact(peer + " 11:22:33:44:55:66"));
    }

    [Theory]
    [InlineData("A2854: HCI failure source=CommandStatus.", "HCI failure source=CommandStatus")]
    [InlineData("A2854: HCI failure source=EncryptionChange.", "HCI failure source=EncryptionChange")]
    [InlineData("A2854: early peer disconnect before encryption reason=0x3E; bond unchanged.",
        "Peer disconnected before encryption; reason=0x3E")]
    public void Sanitized_encryption_phase_evidence_includes_no_raw_peer_details(
        string message, string expected)
    {
        Assert.Equal(expected, A2854SessionTimeline.Redact(message));
        Assert.DoesNotContain("bond", A2854SessionTimeline.Redact(message)!);
        Assert.NotEqual(expected, A2854SessionTimeline.Redact(message + " LTK=SECRET"));
    }

    [Fact]
    public void Unknown_and_oversized_statuses_are_rejected()
    {
        Assert.Null(A2854SessionTimeline.Redact("Address=11:22:33:44:55:66 LTK=ABCD"));
        Assert.Null(A2854SessionTimeline.Redact("A2854: encrypted LTK connection established. " + new string('x', 1200)));
        Assert.Equal("A2854: encrypted LTK connection established.",
            A2854SessionTimeline.Redact("A2854: encrypted LTK connection established."));
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
