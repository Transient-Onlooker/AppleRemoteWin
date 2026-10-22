// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

/// <summary>
/// Links user Stop with a one-shot USB-reader failure. A failed WinUSB reader
/// need not generate HCI Disconnection Complete: revoke input immediately,
/// then cancel the operation awaiting that missing event.
/// </summary>
public sealed class A2854SessionFaultGate : IDisposable
{
    readonly object _sync = new();
    readonly CancellationTokenSource _linked;
    readonly Action _revokeInputs;
    bool _failed, _disposed;

    public A2854SessionFaultGate(CancellationToken userStop, Action revokeInputs)
    {
        _revokeInputs = revokeInputs ?? throw new ArgumentNullException(nameof(revokeInputs));
        _linked = CancellationTokenSource.CreateLinkedTokenSource(userStop);
    }

    public CancellationToken Token => _linked.Token;

    /// <summary>
    /// Runs revocation only once. Holding the same lock used by Dispose
    /// makes a prior session's in-progress failure callback finish BEFORE
    /// Dispose returns and a new session can start. Cancellation happens
    /// even when a report/UI callback throws.
    /// </summary>
    public bool Fail()
    {
        lock (_sync)
        {
            if (_disposed || _failed) return false;
            _failed = true;
            try { _revokeInputs(); }
            finally { _linked.Cancel(); }
            return true;
        }
    }

    /// <summary>
    /// Cannot return while an already-started Fail() is still revoking
    /// inputs. Late Fail() calls after disposal are ignored.
    /// </summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _linked.Dispose();
        }
    }
}
