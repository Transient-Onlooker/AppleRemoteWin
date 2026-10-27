// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

public sealed class SharedPointerSink(IRelativePointerSink movement, IConfirmedKeySink click) : IRelativePointerSink
{
    public void Move(int dx, int dy) => movement.Move(dx, dy);
    public bool LeftButton(bool down) => click.TryKey(MouseActions.Left, down);
}
