// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

public sealed class SharedPointerSink(IRelativePointerSink movement, IConfirmedKeySink click) : IConfirmedRelativePointerSink
{
    public void Move(int dx, int dy) => movement.Move(dx, dy);
    public bool CanConfirmMovement => movement is IConfirmedRelativePointerSink confirmed && confirmed.CanConfirmMovement;
    public bool TryMove(int dx, int dy) => movement is IConfirmedRelativePointerSink confirmed && confirmed.CanConfirmMovement
        ? confirmed.TryMove(dx, dy) : throw new InvalidOperationException("Movement confirmation is unavailable.");
    public bool LeftButton(bool down) => click.TryKey(MouseActions.Left, down);
}
