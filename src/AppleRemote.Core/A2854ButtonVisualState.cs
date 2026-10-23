// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

/// <summary>
/// Exact UI highlight bits from an A2854 0xFB report. Not the first-generation
/// 0xFC bitmap; no OS keyboard or pointer input is performed here.
/// </summary>
public readonly record struct A2854ButtonVisualState(
    bool Back, bool Tv, bool Siri, bool PlayPause, bool VolumeUp,
    bool VolumeDown, bool CenterClick, bool Mute, bool Power,
    bool Up, bool Right, bool Down, bool Left)
{
    public static A2854ButtonVisualState FromMask(int mask) => new(
        Back: (mask & 0x0040) != 0,
        Tv: (mask & 0x0001) != 0,
        Siri: (mask & 0x0020) != 0,
        PlayPause: (mask & 0x0100) != 0,
        VolumeUp: (mask & 0x0002) != 0,
        VolumeDown: (mask & 0x0004) != 0,
        CenterClick: (mask & 0x0008) != 0,
        Mute: (mask & 0x0080) != 0,
        Power: (mask & 0x0010) != 0,
        Up: (mask & 0x0200) != 0,
        Right: (mask & 0x0400) != 0,
        Down: (mask & 0x0800) != 0,
        Left: (mask & 0x1000) != 0);
}

