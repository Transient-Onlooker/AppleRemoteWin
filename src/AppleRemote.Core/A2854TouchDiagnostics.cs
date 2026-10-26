// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;

namespace AppleRemote;

/// <summary>
/// Lightweight, aggregate-only A2854 touch diagnostics. Records counts and
/// monotonic timing; never stores finger coordinates, BLE addresses or packets.
/// Sampling a window is approximate across concurrently active threads.
/// </summary>
public sealed class A2854TouchDiagnostics
{
    public enum NoMoveReason
    {
        NotSingleContact,
        FirstContact,
        Discontinuity,
        ClickWobble,
        DragAnchor,
        PressureGuard,
        BelowPixel
    }

    sealed class Stage
    {
        long _count, _last, _maxGap;

        public void Hit()
        {
            long now = Stopwatch.GetTimestamp();
            Interlocked.Increment(ref _count);
            long previous = Interlocked.Exchange(ref _last, now);
            if (previous > 0 && now > previous) Max(ref _maxGap, now - previous);
        }

        public (long Count, long MaxGapTicks) Drain() =>
            (Interlocked.Exchange(ref _count, 0), Interlocked.Exchange(ref _maxGap, 0));

        public void Reset()
        {
            Interlocked.Exchange(ref _count, 0);
            Interlocked.Exchange(ref _last, 0);
            Interlocked.Exchange(ref _maxGap, 0);
        }
    }

    static void Max(ref long slot, long value)
    {
        long current = Volatile.Read(ref slot);
        while (value > current)
        {
            long seen = Interlocked.CompareExchange(ref slot, value, current);
            if (seen == current) return;
            current = seen;
        }
    }

    static string Ms(long timestampTicks) =>
        (timestampTicks * 1000.0 / Stopwatch.Frequency).ToString("F0",
            System.Globalization.CultureInfo.InvariantCulture);

    readonly Stage _raw = new(), _hid = new(), _delivered = new();
    readonly Stage _session = new(), _pointer = new(), _move = new();
    readonly Stage _uiQueued = new(), _uiApplied = new(), _uiHeartbeat = new();
    readonly long[] _noMove = new long[7];
    long _uiDropped, _moveFailures, _maxUiQueueDelay, _maxReceiverDuration, _maxPointerDuration;

    public void RawTouch() => _raw.Hit();
    public void HidAccepted() => _hid.Hit();
    public void InputGateDelivered() => _delivered.Hit();
    public void SessionDecoded() => _session.Hit();
    public void PointerProcessed(long began)
    {
        _pointer.Hit();
        Max(ref _maxPointerDuration, Stopwatch.GetTimestamp() - began);
    }
    public void MoveResult(bool accepted)
    {
        _move.Hit();
        if (!accepted) Interlocked.Increment(ref _moveFailures);
    }
    public void Suppressed(NoMoveReason reason) =>
        Interlocked.Increment(ref _noMove[(int)reason]);
    public void ReceiverFinished(long began) =>
        Max(ref _maxReceiverDuration, Stopwatch.GetTimestamp() - began);
    public long UiQueued()
    {
        _uiQueued.Hit();
        return Stopwatch.GetTimestamp();
    }
    public void UiApplied(long enqueued)
    {
        _uiApplied.Hit();
        Max(ref _maxUiQueueDelay, Stopwatch.GetTimestamp() - enqueued);
    }
    public void UiDropped() => Interlocked.Increment(ref _uiDropped);
    public void UiHeartbeat() => _uiHeartbeat.Hit();

    public void Reset()
    {
        foreach (Stage s in new[] { _raw, _hid, _delivered, _session, _pointer, _move,
                                    _uiQueued, _uiApplied, _uiHeartbeat })
            s.Reset();
        foreach (int i in Enumerable.Range(0, _noMove.Length))
            Interlocked.Exchange(ref _noMove[i], 0);
        Interlocked.Exchange(ref _uiDropped, 0);
        Interlocked.Exchange(ref _moveFailures, 0);
        Interlocked.Exchange(ref _maxUiQueueDelay, 0);
        Interlocked.Exchange(ref _maxReceiverDuration, 0);
        Interlocked.Exchange(ref _maxPointerDuration, 0);
    }

    /// <summary>
    /// Drain one diagnostic interval; gaps include intervals when the finger
    /// was lifted, so interpret gap maxima only during continuous movement.
    /// UiApplied means WPF event handler finished, not compositor paint.
    /// </summary>
    public string DrainSummary()
    {
        var raw = _raw.Drain();
        var hid = _hid.Drain();
        var gate = _delivered.Drain();
        var session = _session.Drain();
        var pointer = _pointer.Drain();
        var move = _move.Drain();
        var queue = _uiQueued.Drain();
        var applied = _uiApplied.Drain();
        var heartbeat = _uiHeartbeat.Drain();
        long[] ignored = new long[_noMove.Length];
        for (int i = 0; i < ignored.Length; i++)
            ignored[i] = Interlocked.Exchange(ref _noMove[i], 0);
        return "A2854 touch/5s " +
            $"raw={raw.Count} hid={hid.Count} gate={gate.Count} " +
            $"session={session.Count} ptr={pointer.Count} " +
            $"mouse={move.Count} mouseFail={Interlocked.Exchange(ref _moveFailures, 0)} " +
            $"uiQueued={queue.Count} uiDone={applied.Count} uiDrop={Interlocked.Exchange(ref _uiDropped, 0)} " +
            $"rawGapMax={Ms(raw.MaxGapTicks)}ms sessionGapMax={Ms(session.MaxGapTicks)}ms " +
            $"mouseGapMax={Ms(move.MaxGapTicks)}ms uiGapMax={Ms(applied.MaxGapTicks)}ms " +
            $"uiQueueMax={Ms(Interlocked.Exchange(ref _maxUiQueueDelay, 0))}ms " +
            $"receiverMax={Ms(Interlocked.Exchange(ref _maxReceiverDuration, 0))}ms " +
            $"pointerMax={Ms(Interlocked.Exchange(ref _maxPointerDuration, 0))}ms " +
            $"uiHeartbeatMax={Ms(heartbeat.MaxGapTicks)}ms " +
            $"noMove[contact,first,jump,wobble,drag,pressure,subpx]={string.Join(",", ignored)}";
    }
}
