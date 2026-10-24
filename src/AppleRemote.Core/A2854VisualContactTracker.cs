// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

/// <summary>
/// A visual-only contact with coordinates already decoded from A2854 report
/// 0xFC. No BLE access, gestures, pointer movement or OS input.
/// </summary>
public readonly record struct A2854VisualContact(int X, int Y);

/// <summary>
/// Maintains two persistent visual slots while the order of the incoming
/// contact list changes. The protocol's public TouchEvent/Finger contract
/// carries no persistent per-finger ID; nearest-position matching is best
/// effort, so a perfect identity at a true crossing cannot be guaranteed.
/// </summary>
public sealed class A2854VisualContactTracker
{
    readonly A2854VisualContact?[] _slots = new A2854VisualContact?[2];

    public IReadOnlyList<A2854VisualContact?> Slots => [.. _slots];

    public void Reset() => Array.Clear(_slots);

    public IReadOnlyList<A2854VisualContact?> Update(IReadOnlyList<A2854VisualContact> contacts)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        if (contacts.Count > 2)
            throw new ArgumentOutOfRangeException(nameof(contacts), "A2854 supports at most two contact markers.");

        if (contacts.Count == 0)
        {
            Reset();
            return Slots;
        }

        if (contacts.Count == 1)
        {
            int selected = ChoosePreviousSlot(contacts[0]);
            _slots[selected] = contacts[0];
            _slots[1 - selected] = null;
            return Slots;
        }

        A2854VisualContact first = contacts[0], second = contacts[1];
        int oldCount = (_slots[0].HasValue ? 1 : 0) + (_slots[1].HasValue ? 1 : 0);
        if (oldCount == 1)
        {
            int existingSlot = _slots[0].HasValue ? 0 : 1;
            var previous = _slots[existingSlot]!.Value;
            bool firstIsOld = DistanceSquared(previous, first) <= DistanceSquared(previous, second);
            _slots[existingSlot] = firstIsOld ? first : second;
            _slots[1 - existingSlot] = firstIsOld ? second : first;
        }
        else if (oldCount == 2)
        {
            long identityCost = DistanceSquared(_slots[0]!.Value, first) +
                                DistanceSquared(_slots[1]!.Value, second);
            long swappedCost = DistanceSquared(_slots[0]!.Value, second) +
                               DistanceSquared(_slots[1]!.Value, first);
            if (swappedCost < identityCost)
            {
                _slots[0] = second;
                _slots[1] = first;
            }
            else
            {
                _slots[0] = first;
                _slots[1] = second;
            }
        }
        else
        {
            _slots[0] = first;
            _slots[1] = second;
        }

        return Slots;
    }

    int ChoosePreviousSlot(A2854VisualContact current)
    {
        if (_slots[0] is not { } first)
            return _slots[1].HasValue ? 1 : 0;
        if (_slots[1] is not { } second)
            return 0;
        return DistanceSquared(first, current) <= DistanceSquared(second, current) ? 0 : 1;
    }

    static long DistanceSquared(A2854VisualContact a, A2854VisualContact b)
    {
        long dx = (long)a.X - b.X, dy = (long)a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}

/// <summary>
/// Display-only opacity envelope for independent A2854 finger trails.
/// A marker hides immediately on release, but its trail fades naturally
/// without joining an unrelated new finger. Pure math: no WPF dependencies.
/// </summary>
public static class A2854VisualTrailFade
{
    public static double Opacity(long nowMilliseconds, long lastContactMilliseconds, long lifeMilliseconds = 1000)
    {
        if (lifeMilliseconds <= 0) return 0;
        long elapsed = Math.Max(0, nowMilliseconds - lastContactMilliseconds);
        return 0.85 * Math.Clamp(1 - elapsed / (double)lifeMilliseconds, 0, 1);
    }
}
