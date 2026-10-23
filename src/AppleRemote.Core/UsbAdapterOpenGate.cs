// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

/// <summary>
/// Process-local, per-instance USB open exclusion. Acquire before native open;
/// release only after all native handles have been safely closed. A failed or
/// indefinitely pending cleanup intentionally retains its reservation.
/// </summary>
public sealed class UsbAdapterOpenGate
{
    readonly object _gate = new();
    readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase);

    public Lease? TryAcquire(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        lock (_gate)
        {
            if (!_reserved.Add(instanceId)) return null;
            return new Lease(this, instanceId);
        }
    }

    public sealed class Lease
    {
        readonly UsbAdapterOpenGate _owner;
        readonly string _instanceId;
        int _released;

        internal Lease(UsbAdapterOpenGate owner, string instanceId)
        {
            _owner = owner;
            _instanceId = instanceId;
        }

        /// <summary>Call only after native resource release succeeded.</summary>
        public void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            lock (_owner._gate) _owner._reserved.Remove(_instanceId);
        }
    }
}

