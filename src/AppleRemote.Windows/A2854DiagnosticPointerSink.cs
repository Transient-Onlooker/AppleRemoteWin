// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote.Windows;

/// <summary>
/// Diagnostic-only wrapper around the existing Windows pointer backend.
/// Input behavior is unchanged; reports only aggregate SendInput outcomes.
/// </summary>
public sealed class A2854DiagnosticPointerSink(A2854TouchDiagnostics diagnostics) : IRelativePointerSink
{
    readonly WindowsPointerSink _inner = new();

    public void Move(int dx, int dy)
    {
        _inner.Move(dx, dy);
        diagnostics.MoveResult(_inner.LastError is null);
    }

    public bool LeftButton(bool down) => _inner.LeftButton(down);
}
