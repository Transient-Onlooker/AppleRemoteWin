// SPDX-License-Identifier: AGPL-3.0-or-later

using System.ComponentModel;
using System.Runtime.InteropServices;
using AppleRemote.Windows.Native;

namespace AppleRemote.Windows;

/// <summary>
/// Mouse-only SendInput backend. Constructed by RemoteSession but never called
/// unless the user explicitly enables A2854 pointer injection in the GUI.
/// </summary>
public sealed class WindowsPointerSink : IRelativePointerSink
{
    const uint InputMouse = 0;
    const uint MouseMove = 0x0001, LeftDown = 0x0002, LeftUp = 0x0004;

    public string? LastError { get; private set; }

    public void Move(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        _ = Send(dx, dy, MouseMove);
    }

    public bool LeftButton(bool down) => Send(0, 0, down ? LeftDown : LeftUp);

    bool Send(int dx, int dy, uint flags)
    {
        var input = new NativeMethods.INPUT
        {
            type = InputMouse,
            U = new NativeMethods.INPUT_UNION
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = dx, dy = dy, mouseData = 0, dwFlags = flags,
                    time = 0, dwExtraInfo = IntPtr.Zero,
                },
            },
        };
        uint count = NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>());
        LastError = count == 1 ? null : new Win32Exception(Marshal.GetLastWin32Error()).Message;
        return count == 1;
    }
}
