// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

/// <summary>
/// Shared OS-key ownership for physical KeyBinder and shortcut/macro producers.
/// Both call the same backend, but only the first owner sends key-down and
/// only the last owner can send key-up. Rejected transitions leave ownership
/// unchanged, so each producer can retry through its existing cleanup path.
/// </summary>
public sealed class SharedKeyOwnershipSink
{
    readonly IConfirmedKeySink _backend;
    readonly object _gate = new();
    readonly HashSet<KeyCode> _physical = new();
    readonly HashSet<KeyCode> _shortcuts = new();

    public IConfirmedKeySink Physical { get; }
    public IConfirmedKeySink Shortcuts { get; }

    public SharedKeyOwnershipSink(IConfirmedKeySink backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        Physical = new Producer(this, shortcut: false);
        Shortcuts = new Producer(this, shortcut: true);
    }

    sealed class Producer(SharedKeyOwnershipSink parent, bool shortcut) : IConfirmedKeySink
    {
        public bool TryKey(KeyCode key, bool down) => parent.TryKey(key, down, shortcut);
        public void Key(KeyCode key, bool down) => _ = TryKey(key, down);
    }

    bool TryKey(KeyCode key, bool down, bool shortcut)
    {
        lock (_gate)
        {
            var own = shortcut ? _shortcuts : _physical;
            var other = shortcut ? _physical : _shortcuts;

            if (down)
            {
                if (own.Contains(key)) return true;
                // The other producer has already successfully sent key-down.
                // Joining its ownership must NOT inject a duplicate key-down.
                if (other.Contains(key) || Send(key, true))
                {
                    own.Add(key);
                    return true;
                }
                return false;
            }

            if (!own.Contains(key)) return true;
            // Never release a different producer's still-held VK.
            if (other.Contains(key) || Send(key, false))
            {
                own.Remove(key);
                return true;
            }
            // OS rejected the final key-up: keep the last owner for retries.
            return false;
        }
    }

    bool Send(KeyCode key, bool down)
    {
        try { return _backend.TryKey(key, down); }
        catch { return false; }
    }
}
