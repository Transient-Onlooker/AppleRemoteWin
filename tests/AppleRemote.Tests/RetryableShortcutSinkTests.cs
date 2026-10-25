// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

/// <summary>
/// Model direct shortcut/macro calls to KeyboardInjector.Key (NOT KeyBinder's
/// direct TryKey path). Every fake here is pure Core and never calls SendInput.
/// </summary>
public sealed class RetryableShortcutSinkTests
{
    sealed class FakeConfirmedSink : IConfirmedKeySink
    {
        public readonly List<(KeyCode Key, bool Down)> Accepted = new();
        public readonly Dictionary<KeyCode, int> RejectUps = new();
        public readonly Dictionary<KeyCode, int> RejectDowns = new();
        public bool ThrowOnNextUp { get; set; }

        public void Key(KeyCode key, bool down) => _ = TryKey(key, down);

        public bool TryKey(KeyCode key, bool down)
        {
            if (!down && ThrowOnNextUp)
            {
                ThrowOnNextUp = false;
                throw new InvalidOperationException("Fake OS rejection");
            }
            var failures = down ? RejectDowns : RejectUps;
            if (failures.TryGetValue(key, out int left) && left > 0)
            {
                failures[key] = left - 1;
                return false;
            }
            Accepted.Add((key, down));
            return true;
        }
    }

    static readonly KeyCode Ctrl = new(0x11);
    static readonly KeyCode C = Keys.Names["C"];
    static readonly KeyCode F5 = Keys.Names["F5"];

    [Fact]
    public void Ctrl_C_rejected_modifier_keyup_is_retried_at_Stop()
    {
        var underlying = new FakeConfirmedSink();
        underlying.RejectUps[Ctrl] = 1;
        var sink = new RetryableShortcutSink(underlying);

        sink.Key(Ctrl, true);
        sink.Key(C, true);
        sink.Key(C, false);
        sink.Key(Ctrl, false); // Windows rejects this Ctrl up
        Assert.True(sink.HasPendingReleases);
        Assert.False(underlying.Accepted.Contains((Ctrl, false)));

        Assert.True(sink.ReleaseAll()); // Stop
        Assert.False(sink.HasPendingReleases);
        Assert.Equal(new[] {
            (Ctrl, true), (C, true), (C, false), (Ctrl, false)
        }, underlying.Accepted);
    }

    [Fact]
    public void Failed_shortcut_release_survives_repeated_Stop_until_accepted()
    {
        var underlying = new FakeConfirmedSink();
        underlying.RejectUps[F5] = 3;
        var sink = new RetryableShortcutSink(underlying);
        sink.Key(F5, true);
        sink.Key(F5, false);
        Assert.True(sink.HasPendingReleases);

        Assert.False(sink.ReleaseAll());
        Assert.False(sink.ReleaseAll());
        Assert.True(sink.HasPendingReleases);
        Assert.True(sink.ReleaseAll());
        Assert.False(sink.HasPendingReleases);

        for (int i = 0; i < 5; i++) Assert.True(sink.ReleaseAll());
        Assert.Equal(new[] { (F5, true), (F5, false) }, underlying.Accepted);
    }

    [Fact]
    public void Two_failed_shortcut_keyups_are_both_retried_during_cleanup()
    {
        var underlying = new FakeConfirmedSink();
        underlying.RejectUps[Ctrl] = 1;
        underlying.RejectUps[C] = 1;
        var sink = new RetryableShortcutSink(underlying);
        sink.Key(Ctrl, true);
        sink.Key(C, true);
        sink.Key(C, false);
        sink.Key(Ctrl, false);
        Assert.True(sink.HasPendingReleases);
        Assert.True(sink.ReleaseAll());
        Assert.Equal(4, underlying.Accepted.Count);
        Assert.Equal(1, underlying.Accepted.Count(x => x == (Ctrl, false)));
        Assert.Equal(1, underlying.Accepted.Count(x => x == (C, false)));
    }

    [Fact]
    public void New_shortcut_is_suppressed_while_previous_keyup_still_fails()
    {
        var underlying = new FakeConfirmedSink();
        underlying.RejectUps[Ctrl] = 2;
        var sink = new RetryableShortcutSink(underlying);
        sink.Key(Ctrl, true);
        sink.Key(Ctrl, false); // first reject
        sink.Key(F5, true); // second reject; F5 must not be sent
        Assert.Equal(new[] { (Ctrl, true) }, underlying.Accepted);
        Assert.True(sink.HasPendingReleases);

        sink.Key(F5, true); // accepted Ctrl-up, then F5-down
        sink.Key(F5, false);
        Assert.True(sink.ReleaseAll());
        Assert.Equal(new[] {
            (Ctrl, true), (Ctrl, false), (F5, true), (F5, false)
        }, underlying.Accepted);
    }

    [Fact]
    public void Rejected_keydown_never_registers_a_key_for_later_release()
    {
        var underlying = new FakeConfirmedSink();
        underlying.RejectDowns[F5] = 1;
        var sink = new RetryableShortcutSink(underlying);
        sink.Key(F5, true);
        sink.Key(F5, false);
        Assert.True(sink.ReleaseAll());
        Assert.False(sink.HasPendingReleases);
        Assert.Empty(underlying.Accepted);
    }

    [Fact]
    public void Repeated_down_and_up_are_not_duplicated()
    {
        var underlying = new FakeConfirmedSink();
        var sink = new RetryableShortcutSink(underlying);
        sink.Key(F5, true);
        sink.Key(F5, true);
        sink.Key(F5, false);
        sink.Key(F5, false);
        Assert.True(sink.ReleaseAll());
        Assert.Equal(new[] { (F5, true), (F5, false) }, underlying.Accepted);
    }

    [Fact]
    public void Stop_releases_a_still_held_shortcut_key()
    {
        var underlying = new FakeConfirmedSink();
        var sink = new RetryableShortcutSink(underlying);
        sink.Key(Ctrl, true);
        Assert.False(sink.HasPendingReleases); // accepted down, not rejected up
        Assert.True(sink.ReleaseAll());
        Assert.Equal(new[] { (Ctrl, true), (Ctrl, false) }, underlying.Accepted);
    }

    [Fact]
    public void Exception_during_keyup_leaves_pending_state_for_retry()
    {
        var underlying = new FakeConfirmedSink { ThrowOnNextUp = true };
        var sink = new RetryableShortcutSink(underlying);
        sink.Key(F5, true);
        sink.Key(F5, false);
        Assert.True(sink.HasPendingReleases);
        Assert.True(sink.ReleaseAll());
        Assert.Equal(new[] { (F5, true), (F5, false) }, underlying.Accepted);
    }
}
