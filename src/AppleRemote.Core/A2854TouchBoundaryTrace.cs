// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;

namespace AppleRemote;

public enum A2854TraceContact { Release, Hover, Single, Multi }
public enum A2854TraceSign { Negative = -1, Zero = 0, Positive = 1 }

public sealed class A2854TouchBoundaryTrace
{
    public const int Capacity = 512;
    public static readonly TimeSpan Horizon = TimeSpan.FromSeconds(2);

    readonly object _gate = new();
    readonly Entry[] _entries = new Entry[Capacity];
    int _start, _count;
    ushort? _lastSequence;

    readonly record struct Entry(TimeSpan Timestamp, TimeSpan RecordedAt, string SequenceClass, int Delta,
        A2854TraceContact Contact, A2854TouchObservationKind Outcome,
        A2854TraceSign XSign, A2854TraceSign YSign);

    public void Record(ushort sequence, TimeSpan timestamp, A2854TraceContact contact,
        A2854TouchObservationKind outcome, A2854TraceSign xSign, A2854TraceSign ySign,
        TimeSpan? recordedAt = null)
    {
        if ((uint)contact > (uint)A2854TraceContact.Multi) throw new ArgumentOutOfRangeException(nameof(contact));
        if ((uint)outcome > (uint)A2854TouchObservationKind.Move) throw new ArgumentOutOfRangeException(nameof(outcome));
        if ((int)xSign is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(xSign));
        if ((int)ySign is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(ySign));

        lock (_gate)
        {
            if (timestamp < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timestamp));
            TimeSpan now = recordedAt ?? timestamp;
            if (now < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(recordedAt));
            Prune(now);
            int delta = _lastSequence is ushort last ? (ushort)(sequence - last) : 0;
            string sequenceClass = _lastSequence is null ? "first" : delta switch
            {
                0 => "duplicate",
                < 32768 => "forward",
                _ => "backward_or_large_gap"
            };
            if (sequenceClass == "backward_or_large_gap") delta -= 65536;
            if (sequenceClass is "first" or "forward") _lastSequence = sequence;
            if (_count == Capacity)
            {
                _start = (_start + 1) % Capacity;
                _count--;
            }
            _entries[(_start + _count++) % Capacity] = new(timestamp, now, sequenceClass, delta,
                contact, outcome, xSign, ySign);
        }
    }

    public IReadOnlyList<string> Snapshot(TimeSpan timestamp)
    {
        lock (_gate)
        {
            if (timestamp < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timestamp));
            Prune(timestamp);
            var lines = new List<string>(_count);
            for (int i = 0; i < _count; i++)
            {
                var entry = _entries[(_start + i) % Capacity];
                if (entry.Timestamp > timestamp || timestamp - entry.Timestamp > Horizon) continue;
                string offset = (timestamp - entry.Timestamp).TotalMilliseconds.ToString("0.####", CultureInfo.InvariantCulture);
                lines.Add($"age_ms={offset} seq={entry.SequenceClass} delta={entry.Delta.ToString(CultureInfo.InvariantCulture)} " +
                    $"contact={entry.Contact.ToString().ToLowerInvariant()} outcome={entry.Outcome.ToString().ToLowerInvariant()} " +
                    $"x={Sign(entry.XSign)} y={Sign(entry.YSign)}");
            }
            return lines;
        }
    }

    void Prune(TimeSpan now)
    {
        while (_count > 0 && now - _entries[_start].RecordedAt > Horizon)
        {
            _entries[_start] = default;
            _start = (_start + 1) % Capacity;
            _count--;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            Array.Clear(_entries);
            _start = _count = 0;
            _lastSequence = null;
        }
    }

    static string Sign(A2854TraceSign sign) => sign switch
    {
        A2854TraceSign.Negative => "-",
        A2854TraceSign.Positive => "+",
        _ => "0"
    };
}
