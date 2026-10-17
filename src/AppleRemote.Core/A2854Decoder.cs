// SPDX-License-Identifier: AGPL-3.0-or-later
// A2854 protocol mapping from observed hardware traces:
// https://github.com/Transient-Onlooker/siri-remote-for-windows
// Distinct from the first-generation Decoder; no gen-1 behavior is changed.

namespace AppleRemote;

/// <summary>
/// One decoded A2854 clickpad contact. The flags and contact geometry are
/// empirical values, not an Apple-published physical pressure measurement.
/// </summary>
public readonly record struct A2854Finger(
    int X, int Y, byte Major, byte Minor, byte Pressure, byte Flags)
{
    public bool Hover => (Flags & 0x02) != 0;
    public int AngleIndex => (Flags >> 5) & 7;
}

/// <summary>Parsed 0xFC touch payload, excluding the report-ID byte.</summary>
public sealed record A2854TouchFrame(
    ushort Sequence, byte Header, A2854Finger? First, A2854Finger? Second)
{
    public int FingerCount => (First.HasValue ? 1 : 0) + (Second.HasValue ? 1 : 0);
}

/// <summary>
/// Decoder for the A2854 (third-generation USB-C Siri Remote). The inputs are
/// unpadded GATT reports prefixed with their Report Reference ID (0xFB/0xFC).
/// The existing <see cref="Decoder"/> is retained for first-generation remotes.
/// </summary>
public sealed class A2854Decoder
{
    public const byte ButtonsReportId = 0xFB;
    public const byte TouchReportId = 0xFC;
    public const int TouchClockHz = 2000; // Empirical: +30 ticks per ~15 ms frame.

    // Names deliberately align with the existing KeyMap where applicable.
    // "power" and "mute" have no keyboard binding in gen 1, but are exposed.
    static readonly (int Bit, string Name)[] Buttons =
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

    int _mask;
    ushort? _sequence;
    double _time;
    TouchEvent? _touch;

    static IReadOnlyList<string> Names(int mask) =>
        Buttons.Where(b => (mask & b.Bit) != 0).Select(b => b.Name).ToArray();

    static int Signed12(int value) => (value & 0x800) != 0 ? value - 0x1000 : value;

    static A2854Finger? ParseFinger(ReadOnlySpan<byte> slot)
    {
        if (slot.Length != 7) return null;
        if (slot[3] == 0 && slot[4] == 0 && slot[5] == 0) return null;

        int packed = slot[0] | slot[1] << 8 | slot[2] << 16;
        return new A2854Finger(
            Signed12(packed & 0xFFF),
            Signed12((packed >> 12) & 0xFFF),
            slot[3], slot[4], slot[5], slot[6]);
    }

    /// <summary>
    /// Parse exactly 11-byte (one contact) or 18-byte (two contacts) GATT
    /// payloads, only when the first payload byte is the observed 0x32 marker.
    /// A release carries no active finger. Malformed packets return null.
    /// </summary>
    public static A2854TouchFrame? ParseTouch(ReadOnlySpan<byte> payload)
    {
        if (payload.Length is not (11 or 18) || payload[0] != 0x32)
            return null;
        return new A2854TouchFrame(
            (ushort)(payload[1] | payload[2] << 8),
            payload[3],
            ParseFinger(payload.Slice(4, 7)),
            payload.Length == 18 ? ParseFinger(payload.Slice(11, 7)) : null);
    }

    ButtonsEvent SetButtons(int newMask, byte[] raw)
    {
        int previous = _mask;
        _mask = newMask;
        return new ButtonsEvent(newMask, Names(newMask & ~previous),
            Names(previous & ~newMask), raw);
    }

    /// <summary>
    /// Return decoded button/touch events. null is a clock tick. An empty
    /// array is a lost BLE connection and releases previously held controls.
    /// Unknown reports, including microphone reports, are not decoded.
    /// </summary>
    public List<RemoteEvent> Decode(byte[]? report)
    {
        List<RemoteEvent> events = [];
        if (report is null) return events;

        if (report.Length == 0)
        {
            if (_mask != 0) events.Add(SetButtons(0, report));
            if (_touch is { Down: true } previous)
                events.Add(previous with { Down = false, Fingers = [], Raw = report });
            _touch = null;
            _sequence = null;
            _time = 0;
            return events;
        }

        if (report[0] == ButtonsReportId && report.Length == 3)
        {
            int mask = report[1] | report[2] << 8;
            if (mask != _mask) events.Add(SetButtons(mask, report));
            return events;
        }

        if (report[0] != TouchReportId) return events;
        A2854TouchFrame? frame = ParseTouch(report.AsSpan(1));
        if (frame is null) return events;

        if (_sequence is ushort last)
            _time += (ushort)(frame.Sequence - last) / (double)TouchClockHz;
        _sequence = frame.Sequence;

        A2854Finger? first = frame.First;
        A2854Finger? second = frame.Second;
        List<Finger> fingers = [];
        foreach (A2854Finger? slot in new A2854Finger?[] { first, second })
        {
            if (slot is not { } f) continue;
            // State 7 = non-contact/hover, State 4 = solid contact, as far
            // as the existing first-generation gesture contract is concerned.
            fingers.Add(new Finger(f.X, f.Y, f.Hover ? 7 : 4));
        }

        A2854Finger? primary =
            (first is { Hover: false } ? first : second is { Hover: false } ? second : first ?? second);
        bool down = primary is { Hover: false };
        int x = primary?.X ?? _touch?.X ?? 0;
        int y = primary?.Y ?? _touch?.Y ?? 0;

        _touch = new TouchEvent(Math.Round(_time, 4), x, y, down, fingers, report);
        events.Add(_touch);
        return events;
    }
}
