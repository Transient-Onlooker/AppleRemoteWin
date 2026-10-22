// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

/// <summary>
/// Buffers an early USB reader failure until the owner's failure callback is
/// registered. UsbHci reader threads start during adapter construction, before
/// RemoteSession can subscribe to events. Delivery is at-most-once, whether
/// failure or subscriber registration happens first.
/// </summary>
public sealed class A2854UsbFailureRelay
{
    readonly object _sync = new();
    Action<string>? _listener;
    string? _failure;

    public void Subscribe(Action<string> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        string? replay;
        lock (_sync)
        {
            if (_listener is not null)
                throw new InvalidOperationException("A2854 USB failure relay already has a subscriber.");
            _listener = listener;
            replay = _failure;
        }

        if (replay is not null)
            listener(replay);
    }

    /// <summary>
    /// Stores the first terminal USB fault; late subscribers receive it
    /// synchronously. Duplicated event+ACL failures never release twice.
    /// </summary>
    public void Fail(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Action<string>? listener;
        lock (_sync)
        {
            if (_failure is not null) return;
            _failure = description;
            listener = _listener;
        }
        listener?.Invoke(description);
    }

    public bool HasFailed
    {
        get { lock (_sync) return _failure is not null; }
    }
}
