// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

/// <summary>
/// Links user Stop with a one-shot USB-reader failure. A failed WinUSB reader
/// need not generate HCI Disconnection Complete: revoke input immediately,
/// then cancel the operation awaiting that missing event.
/// </summary>
public sealed class A2854SessionFaultGate : IDisposable
{
    readonly CancellationTokenSource _linked;
    readonly Action _revokeInputs;
    int _state; // 0=active, 1=failed, 2=disposed

    public A2854SessionFaultGate(CancellationToken userStop, Action revokeInputs)
    {
        _revokeInputs = revokeInputs ?? throw new ArgumentNullException(nameof(revokeInputs));
        _linked = CancellationTokenSource.CreateLinkedTokenSource(userStop);
    }

    public CancellationToken Token => _linked.Token;

    /// <summary>
    /// Returns true on the first failure only. Even if a UI/input callback
    /// throws, its linked token is cancelled in finally. A late WinUSB failure
    /// from a disposed adapter cannot revoke input belonging to a new session.
    /// </summary>
    public bool Fail()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0) return false;
        try { _revokeInputs(); }
        finally
        {
            try { _linked.Cancel(); }
            catch (ObjectDisposedException) { } // dispose raced USB reader callback
        }
        return true;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _state, 2);
        _linked.Dispose();
    }
}
