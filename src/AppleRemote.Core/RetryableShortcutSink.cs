// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

/// <summary>
/// Acknowledge shortcut/macro key transitions that are not owned by KeyBinder.
/// An accepted key-down remains tracked until an accepted key-up, including
/// when Windows rejects the immediate release. Stop or later chords can retry.
/// </summary>
public sealed class RetryableShortcutSink : IKeySink
{
    readonly IConfirmedKeySink _sink;
    readonly object _gate = new();
    readonly HashSet<KeyCode> _held = new();
    readonly HashSet<KeyCode> _pendingKeyUps = new();

    public RetryableShortcutSink(IConfirmedKeySink sink) =>
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));

    public bool HasPendingReleases
    {
        get { lock (_gate) return _pendingKeyUps.Count != 0; }
    }

    bool TrySend(KeyCode key, bool down)
    {
        try { return _sink.TryKey(key, down); }
        catch { return false; }
    }

    bool TryRelease(KeyCode key)
    {
        if (!_held.Contains(key)) return true;
        if (TrySend(key, false))
        {
            _held.Remove(key);
            _pendingKeyUps.Remove(key);
            return true;
        }
        _pendingKeyUps.Add(key);
        return false;
    }

    public void Key(KeyCode key, bool down)
    {
        lock (_gate)
        {
            if (!down)
            {
                TryRelease(key);
                return;
            }

            // A previously rejected key-up may still be down in Windows.
            // Do not send a new press until the old release can succeed.
            foreach (var pending in _pendingKeyUps.ToArray())
                TryRelease(pending);
            if (_pendingKeyUps.Count != 0) return;

            if (_held.Contains(key)) return; // no duplicate accepted down
            if (TrySend(key, true)) _held.Add(key);
        }
    }

    /// <summary>
    /// Attempt all outstanding releases, retaining rejected keys for later
    /// Stop / input-disabled cleanup. Safe to call repeatedly after success.
    /// </summary>
    public bool ReleaseAll()
    {
        lock (_gate)
        {
            foreach (var key in _held.ToArray())
                TryRelease(key);
            return _held.Count == 0;
        }
    }
}
