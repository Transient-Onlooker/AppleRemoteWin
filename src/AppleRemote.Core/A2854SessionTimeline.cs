// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Text;

namespace AppleRemote;

/// <summary>
/// In-memory, bounded, privacy-safe A2854 connection event timeline.
/// Only known status categories are allowed in exports; incoming arbitrary
/// strings are NEVER copied because they can contain BLE addresses or LTKs.
/// The timeline does not open a device, retry, write GATT or touch the bond.
/// </summary>
public sealed class A2854SessionTimeline
{
    public const int Capacity = 48;

    readonly List<(double Seconds, string Event)> _events = [];
    long _start = Stopwatch.GetTimestamp();

    public void Reset()
    {
        _events.Clear();
        _start = Stopwatch.GetTimestamp();
    }

    public bool Record(string message)
    {
        var safe = ExtractSafeEvent(message);
        if (safe is null) return false;

        if (_events.Count == Capacity) _events.RemoveAt(0);
        _events.Add((Stopwatch.GetElapsedTime(_start).TotalSeconds, safe));
        return true;
    }

    public IReadOnlyList<(double Seconds, string Event)> Snapshot() =>
        _events.Select(e => e).ToArray();

    public string Export()
    {
        var text = new StringBuilder("A2854 local connection timeline (redacted)");
        text.AppendLine();
        text.AppendLine("Times are elapsed since explicit GUI Start, not wall-clock times.");
        if (_events.Count == 0) text.AppendLine("No supported connection events collected.");
        foreach (var (seconds, ev) in _events)
            text.AppendLine($"+{seconds:F2}s  {ev}");
        return text.ToString();
    }

    static string? ExtractSafeEvent(string message)
    {
        // Never export raw text from callbacks. Even if a message happens to
        // begin with a known prefix, any appended addresses/keys are ignored.
        // Preserve only strictly parsed numeric controller diagnostics.
        if (message.StartsWith("A2854: peer disconnection ", StringComparison.Ordinal))
        {
            var m = System.Text.RegularExpressions.Regex.Match(message,
                @"^A2854: peer disconnection (0x[0-9A-Fa-f]{2}) \([^\r\n]*\); connected=([0-9]+\.[0-9]+)s FB=([0-9]+) FC=([0-9]+) last-input=(none|[0-9]+\.[0-9]+s ago);");
            return m.Success ? $"Peer disconnect={m.Groups[1].Value}; connected={m.Groups[2].Value}s; FB={m.Groups[3].Value}; FC={m.Groups[4].Value}; last-input={m.Groups[5].Value}"
                : "Peer disconnect (details not exported)";
        }
        if (message.StartsWith("A2854: LE connection parameters ", StringComparison.Ordinal))
        {
            var m = System.Text.RegularExpressions.Regex.Match(message,
                @"^A2854: LE connection parameters (LinkEstablished|RequestReceived|CommandAccepted|CommandRejected|CommandFailed|Completed); association=(bound|unbound) elapsed=(unknown|[0-9]+\.[0-9]+s) min=([0-9]+) max=([0-9]+) latency=([0-9]+) timeout=([0-9]+) status=(n/a|0x[0-9A-Fa-f]{2})\.$");
            return m.Success ? $"LE {m.Groups[1].Value}; {m.Groups[2].Value}; elapsed={m.Groups[3].Value}; interval={m.Groups[4].Value}..{m.Groups[5].Value}; latency={m.Groups[6].Value}; timeout={m.Groups[7].Value}; status={m.Groups[8].Value}"
                : "LE parameter event (details not exported)";
        }
        if (message.StartsWith("A2854: HCI error phase=", StringComparison.Ordinal))
        {
            var m = System.Text.RegularExpressions.Regex.Match(message,
                @"^A2854: HCI error phase=(load-bond|initialize-controller|connect|encrypt|discover-hid|enable-hid|observe) opcode=(0x[0-9A-Fa-f]{4}) status=(0x[0-9A-Fa-f]{2}); bond unchanged\.$");
            return m.Success ? $"HCI error phase={m.Groups[1].Value}; opcode={m.Groups[2].Value}; status={m.Groups[3].Value}"
                : "HCI command error (details not exported)";
        }
        if (message.StartsWith("A2854: received early ATT request opcode=", StringComparison.Ordinal))
        {
            var m = System.Text.RegularExpressions.Regex.Match(message,
                @"^A2854: received early ATT request opcode=(0x[0-9A-Fa-f]{2}) before encryption completed");
            return m.Success ? $"Early ATT request {m.Groups[1].Value}" : "Early ATT request";
        }
        if (message.StartsWith("A2854: early ATT request count=", StringComparison.Ordinal))
        {
            var m = System.Text.RegularExpressions.Regex.Match(message,
                @"^A2854: early ATT request count=([0-9]+)\.$");
            return m.Success ? $"Early ATT request count={m.Groups[1].Value}" : "Early ATT count";
        }
        string[] exactStart =
        [
            "A2854: initializing dedicated external Barrot WinUSB controller.",
            "A2854: connecting to bonded remote;",
            "A2854: encrypted LTK connection established.",
            "A2854: connection or operation timed out.",
            "A2854: both input notifications disabled.",
            "A2854: notification cleanup partial;",
            "A2854: notification cleanup interrupted;",
            "A2854: host disconnect completed with normal local reason",
            "A2854: disconnect ended with HCI reason",
            "A2854: link cleanup incomplete;",
            "A2854: previous USB session is still closing;",
            "A2854: previous USB cleanup failed;"
        ];
        return exactStart.FirstOrDefault(prefix =>
            message.StartsWith(prefix, StringComparison.Ordinal));
    }
}
