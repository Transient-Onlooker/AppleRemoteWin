// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 AppleRemoteWin A2854 contributors
//
// Independently implemented from documented, experimentally observed A2854
// report layouts. No Linux implementation code is copied into this file.

namespace AppleRemote;

/// <summary>Which Siri Remote report layout should the application decode?</summary>
public enum RemoteModel { Gen1, A2854 }

/// <summary>
/// Experimental 3rd-generation (USB-C, A2854) Siri Remote input decoder.
/// HID-over-GATT notifications have NO report ID byte. The transport must
/// prepend the ID from descriptor 0x2908 before invoking <see cref="Decode"/>.
///
/// 0xFB: two-byte little-endian button mask.
/// 0xFC: 0x32 marker, 16-bit 2 kHz sequence counter, header, 1-2
///        seven-byte touch slots. 0xFA voice payloads are ignored.
/// </summary>
public sealed class A2854Decoder
{
    const int TicksPerSecond = 2000;

    // Names deliberately agree with KeyMap.Inputs where possible.
    static readonly (ushort Bit, string Name)[] ButtonNames =
    [
        (0x0001, "tv"),
        (0x0002, "volup"),
        (0x0004, "voldown"),
        (0x0008, "click"),
        (0x0010, "power"),
        (0x0020, "siri"),
        (0x0040, "menu"),
        (0x0080, "mute"),
        (0x0100, "playpause"),
        (0x0200, "up"),
        (0x0400, "right"),
        (0x0800, "down"),
        (0x1000, "left"),
    ];

    ushort _buttons;
    ushort? _counter;
    double _touchTime;
    TouchEvent? _touch;

    /// <summary>Unknown bits remain in Mask but are intentionally not mapped to keys.</summary>
    public static IReadOnlyList<string> Names(ushort mask) =>
        ButtonNames.Where(b => (mask & b.Bit) != 0).Select(b => b.Name).ToArray();

    static int Signed12(int value) => (value & 0x800) != 0 ? value - 0x1000 : value;

    static Finger? Slot(byte[] report, int start)
    {
        // Only a complete seven-byte slot can reach here.
        int major = report[start + 3], minor = report[start + 4], pressure = report[start + 5];
        if (major == 0 && minor == 0 && pressure == 0) return null;
        int x = Signed12(report[start] | (report[start + 1] & 0x0F) << 8);
        int y = Signed12((report[start + 1] >> 4) | report[start + 2] << 4);
        bool hover = (report[start + 6] & 0x02) != 0;
        // Existing gesture engine expects 3 for contact and 7 for release.
        return new Finger(x, y, hover ? 2 : 3);
    }

    ButtonsEvent Transition(ushort next, byte[] raw)
    {
        ushort old = _buttons;
        _buttons = next;
        return new ButtonsEvent(next, Names((ushort)(next & ~old)),
            Names((ushort)(old & ~next)), raw);
    }

    /// <summary>
    /// Decode one ID-prefixed report. null = clock tick; empty = link lost.
    /// Malformed packets do not change button or finger state.
    /// </summary>
    public List<RemoteEvent> Decode(byte[]? report)
    {
        var output = new List<RemoteEvent>();
        if (report is null) return output;
        if (report.Length == 0)
        {
            if (_buttons != 0) output.Add(Transition(0, report));
            if (_touch is { Down: true } last)
                output.Add(last with { Down = false, Fingers = Array.Empty<Finger>(), Raw = report });
            _touch = null;
            _counter = null;
            _touchTime = 0;
            return output;
        }

        if (report[0] == 0xFB && report.Length == 3)
        {
            ushort mask = (ushort)(report[1] | report[2] << 8);
            if (mask != _buttons) output.Add(Transition(mask, report));
            return output;
        }

        if (report[0] != 0xFC || (report.Length != 12 && report.Length != 19)
            || report[1] != 0x32)
            return output; // Never confuse microphone report 0xFA with input.

        ushort seq = (ushort)(report[2] | report[3] << 8);
        if (_counter is ushort prev)
            _touchTime += (ushort)(seq - prev) / (double)TicksPerSecond;
        _counter = seq;

        var fingers = new List<Finger>(2);
        if (Slot(report, 5) is Finger first) fingers.Add(first);
        if (report.Length == 19 && Slot(report, 12) is Finger second) fingers.Add(second);

        var contact = fingers.FirstOrDefault(f => f.State == 3);
        bool down = fingers.Any(f => f.State == 3);
        var position = down ? contact : (fingers.Count > 0 ? fingers[0] : default);
        _touch = new TouchEvent(Math.Round(_touchTime, 4), position.X, position.Y,
            down, fingers.ToArray(), report);
        output.Add(_touch);
        return output;
    }
}
