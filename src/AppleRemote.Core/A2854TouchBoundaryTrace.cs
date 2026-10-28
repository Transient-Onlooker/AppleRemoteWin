// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;

namespace AppleRemote;

public enum A2854TraceContact { Release, Hover, Single, Multi }
public enum A2854TraceSign { Negative = -1, Zero = 0, Positive = 1 }
public enum A2854TraceIncidentTrigger { BackwardOrLargeGap, RejectedPointerMove }
public enum A2854TraceIncidentState { None, Capturing, Ready }

public sealed record A2854TraceIncidentSnapshot(
    bool Capturing, A2854TraceIncidentTrigger Trigger, bool Truncated, TimeSpan TriggerElapsed, IReadOnlyList<string> Lines);

public sealed class A2854TouchBoundaryTrace
{
    public const int Capacity = 512;
    public const int IncidentCapacity = 768;
    public static readonly TimeSpan Horizon = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan IncidentPostWindow = TimeSpan.FromSeconds(1);

    readonly object _gate = new();
    readonly Entry[] _entries = new Entry[Capacity];
    readonly Entry[] _incident = new Entry[IncidentCapacity];
    int _start, _count;
    ushort? _lastSequence;
    long _reportId;
    bool _incidentEnabled, _incidentPresent, _incidentCapturing, _incidentTruncated;
    int _incidentCount;
    TimeSpan _incidentTime;
    A2854TraceIncidentTrigger _incidentTrigger;

    readonly record struct Entry(long ReportId, TimeSpan Timestamp, TimeSpan RecordedAt, string SequenceClass, int Delta,
        A2854TraceContact Contact, A2854TouchObservationKind Outcome,
        A2854TraceSign XSign, A2854TraceSign YSign, A2854MoveAcceptance Acceptance);

    public void Record(ushort sequence, TimeSpan timestamp, A2854TraceContact contact,
        A2854TouchObservationKind outcome, A2854TraceSign xSign, A2854TraceSign ySign,
        TimeSpan? recordedAt = null, A2854MoveAcceptance acceptance = A2854MoveAcceptance.NotAttempted)
    {
        if ((uint)contact > (uint)A2854TraceContact.Multi) throw new ArgumentOutOfRangeException(nameof(contact));
        if ((uint)outcome > (uint)A2854TouchObservationKind.Move) throw new ArgumentOutOfRangeException(nameof(outcome));
        if ((int)xSign is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(xSign));
        if ((int)ySign is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(ySign));
        if (!Enum.IsDefined(acceptance)) throw new ArgumentOutOfRangeException(nameof(acceptance));

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
            var entry = new Entry(++_reportId, timestamp, now, sequenceClass, delta,
                contact, outcome, xSign, ySign, acceptance);
            if (_incidentEnabled && !_incidentPresent &&
                (sequenceClass == "backward_or_large_gap" ||
                 (outcome == A2854TouchObservationKind.Move && acceptance == A2854MoveAcceptance.Rejected)))
            {
                _incidentPresent = _incidentCapturing = true;
                _incidentTime = timestamp;
                _incidentTrigger = sequenceClass == "backward_or_large_gap"
                    ? A2854TraceIncidentTrigger.BackwardOrLargeGap
                    : A2854TraceIncidentTrigger.RejectedPointerMove;
                for (int i = 0; i < _count; i++)
                {
                    var previous = _entries[(_start + i) % Capacity];
                    if (previous.Timestamp <= timestamp
                        ? timestamp - previous.Timestamp <= Horizon
                        : previous.Timestamp - timestamp <= IncidentPostWindow)
                        _incident[_incidentCount++] = previous;
                }
                _incident[_incidentCount++] = entry;
                if (now >= _incidentTime && now - _incidentTime > IncidentPostWindow)
                    _incidentCapturing = false;
            }
            else if (_incidentCapturing)
            {
                bool inWindow = timestamp <= _incidentTime
                    ? _incidentTime - timestamp <= Horizon
                    : timestamp - _incidentTime <= IncidentPostWindow;
                if (inWindow)
                {
                    if (_incidentCount < IncidentCapacity) _incident[_incidentCount++] = entry;
                    else _incidentTruncated = true;
                }
                if (now >= _incidentTime && now - _incidentTime > IncidentPostWindow)
                    _incidentCapturing = false;
            }
            if (_count == Capacity)
            {
                _start = (_start + 1) % Capacity;
                _count--;
            }
            _entries[(_start + _count++) % Capacity] = entry;
        }
    }

    public void SetIncidentCaptureEnabled(bool enabled)
    {
        lock (_gate)
        {
            _incidentEnabled = enabled;
            if (!enabled) _incidentCapturing = false;
        }
    }

    public A2854TraceIncidentState IncidentState(TimeSpan now)
    {
        if (now < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(now));
        lock (_gate)
        {
            if (!_incidentPresent) return A2854TraceIncidentState.None;
            if (now >= _incidentTime && now - _incidentTime >= IncidentPostWindow)
                _incidentCapturing = false;
            return _incidentCapturing ? A2854TraceIncidentState.Capturing : A2854TraceIncidentState.Ready;
        }
    }

    public A2854TraceIncidentSnapshot? SnapshotIncident(TimeSpan now)
    {
        if (now < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(now));
        Entry[] entries;
        bool capturing, truncated;
        TimeSpan incidentTime;
        A2854TraceIncidentTrigger trigger;
        lock (_gate)
        {
            if (!_incidentPresent) return null;
            if (now >= _incidentTime && now - _incidentTime >= IncidentPostWindow)
                _incidentCapturing = false;
            entries = _incident[.._incidentCount];
            capturing = _incidentCapturing;
            truncated = _incidentTruncated;
            incidentTime = _incidentTime;
            trigger = _incidentTrigger;
        }
        var lines = new string[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            string offset = (entry.Timestamp - incidentTime).TotalMilliseconds.ToString("0.####", CultureInfo.InvariantCulture);
            lines[i] = $"report={entry.ReportId.ToString(CultureInfo.InvariantCulture)} offset_ms={offset} " +
                $"seq={entry.SequenceClass} delta={entry.Delta.ToString(CultureInfo.InvariantCulture)} " +
                $"contact={entry.Contact.ToString().ToLowerInvariant()} outcome={entry.Outcome.ToString().ToLowerInvariant()} " +
                $"x={Sign(entry.XSign)} y={Sign(entry.YSign)} acceptance={entry.Acceptance.ToString().ToLowerInvariant()}";
        }
        return new(capturing, trigger, truncated, incidentTime, lines);
    }

    public void ClearIncident()
    {
        lock (_gate) ClearIncidentCore();
    }

    void ClearIncidentCore()
    {
        Array.Clear(_incident);
        _incidentCount = 0;
        _incidentPresent = _incidentCapturing = _incidentTruncated = false;
    }

    public IReadOnlyList<string> Snapshot(TimeSpan timestamp)
    {
        Entry[] entries;
        lock (_gate)
        {
            if (timestamp < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timestamp));
            Prune(timestamp);
            entries = new Entry[_count];
            for (int i = 0; i < _count; i++)
                entries[i] = _entries[(_start + i) % Capacity];
        }
        var lines = new List<string>(entries.Length);
        foreach (var entry in entries)
        {
            if (entry.Timestamp > timestamp || timestamp - entry.Timestamp > Horizon) continue;
            string offset = (timestamp - entry.Timestamp).TotalMilliseconds.ToString("0.####", CultureInfo.InvariantCulture);
            lines.Add($"age_ms={offset} seq={entry.SequenceClass} delta={entry.Delta.ToString(CultureInfo.InvariantCulture)} " +
                $"contact={entry.Contact.ToString().ToLowerInvariant()} outcome={entry.Outcome.ToString().ToLowerInvariant()} " +
                $"x={Sign(entry.XSign)} y={Sign(entry.YSign)}");
        }
        return lines;
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
            _reportId = 0;
            ClearIncidentCore();
        }
    }

    static string Sign(A2854TraceSign sign) => sign switch
    {
        A2854TraceSign.Negative => "-",
        A2854TraceSign.Positive => "+",
        _ => "0"
    };
}
