// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

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
    const int MaxStatusLength = 1024;
    static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    readonly List<(double Seconds, string Event)> _events = [];
    long _start = Stopwatch.GetTimestamp();

    public void Reset()
    {
        _events.Clear();
        _start = Stopwatch.GetTimestamp();
    }

    public bool Record(string message)
    {
        var safe = Redact(message);
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

    /// <summary>
    /// Turn an app status into a fresh, fixed-format description. Never return
    /// any substring wholesale, including after a known prefix. The same
    /// strict redaction is used by both Copy timeline and Copy result.
    /// </summary>
    public static string? Redact(string message)
    {
        if (string.IsNullOrEmpty(message) || message.Length > MaxStatusLength)
            return null;

        static Match MatchNumeric(string input, string pattern) =>
            Regex.Match(input, pattern, RegexOptions.CultureInvariant, RegexTimeout);

        if (message.StartsWith("A2854: peer disconnection ", StringComparison.Ordinal))
        {
            var m = MatchNumeric(message,
                @"\AA2854: peer disconnection (0x[0-9A-Fa-f]{2}) \([^)\r\n]{0,100}\); connected=([0-9]{1,5}\.[0-9]{1,3})s FB=([0-9]{1,9}) FC=([0-9]{1,9}) last-input=(none|[0-9]{1,5}\.[0-9]{1,3}s ago); host Stop/CCCD disable not yet verified\.\z");
            return m.Success
                ? $"Peer disconnect={m.Groups[1].Value}; connected={m.Groups[2].Value}s; FB={m.Groups[3].Value}; FC={m.Groups[4].Value}; last-input={m.Groups[5].Value}"
                : "Peer disconnected (details withheld)";
        }
        if (message.StartsWith("A2854: LE connection parameters ", StringComparison.Ordinal))
        {
            var m = MatchNumeric(message,
                @"\AA2854: LE connection parameters (LinkEstablished|RequestReceived|CommandAccepted|CommandRejected|CommandFailed|Completed); association=(bound|unbound) elapsed=(unknown|[0-9]{1,5}\.[0-9]{1,3}s) min=([0-9]{1,5}) max=([0-9]{1,5}) latency=([0-9]{1,5}) timeout=([0-9]{1,5}) status=(n/a|0x[0-9A-Fa-f]{2})\.\z");
            return m.Success
                ? $"LE {m.Groups[1].Value}; {m.Groups[2].Value}; elapsed={m.Groups[3].Value}; interval={m.Groups[4].Value}..{m.Groups[5].Value}; latency={m.Groups[6].Value}; timeout={m.Groups[7].Value}; status={m.Groups[8].Value}"
                : "LE parameter event (details withheld)";
        }
        if (message.StartsWith("A2854: HCI error phase=", StringComparison.Ordinal))
        {
            var m = MatchNumeric(message,
                @"\AA2854: HCI error phase=(load-bond|initialize-controller|connect|encrypt|discover-hid|enable-hid|observe) opcode=(0x[0-9A-Fa-f]{4}) status=(0x[0-9A-Fa-f]{2}); bond unchanged\.\z");
            return m.Success
                ? $"HCI error phase={m.Groups[1].Value}; opcode={m.Groups[2].Value}; status={m.Groups[3].Value}"
                : "HCI error (details withheld)";
        }
        if (message.StartsWith("A2854: session error phase=", StringComparison.Ordinal))
        {
            var m = MatchNumeric(message,
                @"\AA2854: session error phase=(load-bond|initialize-controller|connect|encrypt|discover-hid|enable-hid|observe) type=([a-zA-Z][a-zA-Z0-9]{0,63}); bond unchanged\.\z");
            return m.Success ? $"Session error phase={m.Groups[1].Value}; type={m.Groups[2].Value}"
                : "Session error (details withheld)";
        }
        if (message.StartsWith("A2854: received early ATT request opcode=", StringComparison.Ordinal))
        {
            var m = MatchNumeric(message,
                @"\AA2854: received early ATT request opcode=(0x[0-9A-Fa-f]{2}) before encryption completed \(handled by GATT\)\.\z");
            return m.Success ? $"Early ATT request opcode={m.Groups[1].Value}"
                : "Early ATT request (details withheld)";
        }
        if (message.StartsWith("A2854: early ATT request count=", StringComparison.Ordinal))
        {
            var m = MatchNumeric(message,
                @"\AA2854: early ATT request count=([0-9]{1,9})\.\z");
            return m.Success ? $"Early ATT request count={m.Groups[1].Value}"
                : "Early ATT count (details withheld)";
        }
        if (message.StartsWith("A2854: disconnect ended with HCI reason ", StringComparison.Ordinal))
        {
            var m = MatchNumeric(message,
                @"\AA2854: disconnect ended with HCI reason (0x[0-9A-Fa-f]{2}) \(host cleanup unverified\)\.\z");
            return m.Success ? $"Host disconnect unverified; HCI reason={m.Groups[1].Value}"
                : "Host disconnect unverified";
        }

        // Exact, independently authored constants: do not return arbitrary
        // tail text from a StartsWith match.
        string[] constants =
        [
            "A2854: initializing dedicated external Barrot WinUSB controller.",
            "A2854: encrypted LTK connection established.",
            "A2854: connection or operation timed out. Existing bond was preserved.",
            "A2854: both input notifications disabled.",
            "A2854: notification cleanup partial; disconnect follows.",
            "A2854: notification cleanup interrupted; closing link.",
            "A2854: host disconnect completed with normal local reason 0x16.",
            "A2854: link cleanup incomplete; check adapter before restarting."
        ];
        if (constants.Any(value => string.Equals(message, value, StringComparison.Ordinal)))
            return message; // exact comparison to code-authored constant, no dynamic suffix

        // Canonicalize known categories; values after a prefix are discarded,
        // even if a future controller/transport includes arbitrary text.
        if (message.StartsWith("A2854: connecting to bonded remote;", StringComparison.Ordinal))
            return "Connecting using existing protected bond";
        if (message.StartsWith("A2854: previous USB session is still closing;", StringComparison.Ordinal))
            return "Previous USB cleanup in progress";
        if (message.StartsWith("A2854: previous USB cleanup failed;", StringComparison.Ordinal))
            return "Previous USB cleanup failed";
        if (message.StartsWith("A2854: dedicated Barrot WinUSB interface unavailable", StringComparison.Ordinal))
            return "Dedicated Barrot USB unavailable";
        if (message.StartsWith("A2854: transport error (", StringComparison.Ordinal))
            return "A2854 transport error (details withheld)";
        return null;
    }
}
