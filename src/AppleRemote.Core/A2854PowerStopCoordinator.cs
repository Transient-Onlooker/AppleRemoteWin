// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

/// <summary>Coalesces power/session stop requests and dispatches cleanup asynchronously.</summary>
public sealed class A2854PowerStopCoordinator(Func<bool> shouldStop, Action<Action> schedule, Action stop,
    Func<long>? generation = null) : IDisposable
{
    readonly Func<bool> _shouldStop = shouldStop ?? throw new ArgumentNullException(nameof(shouldStop));
    readonly Action<Action> _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
    readonly Action _stop = stop ?? throw new ArgumentNullException(nameof(stop));
    readonly Func<long>? _generation = generation;
    int _pending, _disposed;

    public void RequestStop()
    {
        long generation = _generation?.Invoke() ?? 0;
        if (Volatile.Read(ref _disposed) != 0 || !_shouldStop() ||
            Interlocked.CompareExchange(ref _pending, 1, 0) != 0) return;
        try { _schedule(() => StopIfStillRequired(generation)); }
        catch { Volatile.Write(ref _pending, 0); }
    }

    void StopIfStillRequired(long generation)
    {
        try
        {
            if (Volatile.Read(ref _disposed) == 0 && (_generation?.Invoke() ?? 0) == generation && _shouldStop()) _stop();
        }
        finally { Volatile.Write(ref _pending, 0); }
    }

    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}
