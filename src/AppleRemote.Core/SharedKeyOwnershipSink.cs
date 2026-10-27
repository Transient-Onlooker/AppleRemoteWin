// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

/// <summary>
/// Shares one acknowledged OS VK state among independent producers: physical
/// buttons, bound shortcuts, and separate macro-test runs. Each producer owns
/// its own accepted down state; no source can lift another source's held VK.
/// </summary>
public sealed class SharedKeyOwnershipSink
{
    readonly IConfirmedKeySink _backend;
    readonly object _gate = new();
    readonly List<HashSet<KeyCode>> _owners = [];

    public IConfirmedKeySink Physical { get; }
    public IConfirmedKeySink Shortcuts { get; }

    public SharedKeyOwnershipSink(IConfirmedKeySink backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        Physical = CreateProducer();
        Shortcuts = CreateProducer();
    }

    /// <summary>
    /// Creates an independent producer within this same OS-key registry.
    /// The owning application keeps the producer for its lifetime so failed
    /// releases remain attributable and retryable. Used by the macro tester.
    /// </summary>
    public IConfirmedKeySink CreateProducer()
    {
        lock (_gate)
        {
            var held = new HashSet<KeyCode>();
            _owners.Add(held);
            return new Producer(this, held);
        }
    }

    sealed class Producer(SharedKeyOwnershipSink parent, HashSet<KeyCode> held) : IConfirmedKeySink
    {
        public bool TryKey(KeyCode key, bool down) => parent.TryKey(key, down, held);
        public void Key(KeyCode key, bool down) => _ = TryKey(key, down);
    }

    bool TryKey(KeyCode key, bool down, HashSet<KeyCode> own)
    {
        lock (_gate)
        {
            if (WheelActions.TryDelta(key, out _))
                return !down || Send(key, true);

            bool heldElsewhere = _owners.Any(o => !ReferenceEquals(o, own) && o.Contains(key));

            if (down)
            {
                if (own.Contains(key)) return true;
                if (heldElsewhere || Send(key, true))
                {
                    own.Add(key);
                    return true;
                }
                return false;
            }

            if (!own.Contains(key)) return true;
            if (heldElsewhere || Send(key, false))
            {
                own.Remove(key);
                return true;
            }
            // Last owner keeps rejected VK until a subsequent cleanup.
            return false;
        }
    }

    bool Send(KeyCode key, bool down)
    {
        try { return _backend.TryKey(key, down); }
        catch { return false; }
    }
}
