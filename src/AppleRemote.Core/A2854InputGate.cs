// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

/// <summary>
/// Serializes A2854 HID report delivery with terminal input release.
/// A peer-end, user Stop or USB failure closes the gate before releasing held
/// mouse/keyboard state; callbacks resumed afterwards cannot re-press it.
/// Independent of WinUSB so the races are unit-testable on any platform.
/// </summary>
public sealed class A2854InputGate(Action<byte[]?> receiver)
{
    readonly Action<byte[]?> _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
    readonly object _gate = new();
    bool _closed;

    /// <summary>Forward only if the session is still accepting input.</summary>
    public bool TryForward(byte[] report)
    {
        ArgumentNullException.ThrowIfNull(report);
        lock (_gate)
        {
            if (_closed) return false;
            _receiver(report);
            return true;
        }
    }

    /// <summary>
    /// Atomically stop further input and publish exactly one empty release
    /// marker. Repeated Close() calls from separate HCI/USB/Stop paths are safe.
    /// </summary>
    public void Close()
    {
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
            _receiver([]); // canonical decoder/link-loss release sentinel
        }
    }

    public bool IsClosed
    {
        get { lock (_gate) return _closed; }
    }
}
