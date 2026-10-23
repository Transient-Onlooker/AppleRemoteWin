// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

/// <summary>
/// Named points that the operator physically marks on the A2854 clickpad.
/// Coordinates are decoded 0xFC signed-12-bit values, never a BLE address or key.
/// </summary>
public readonly record struct A2854TouchReference(int X, int Y);

/// <summary>
/// Five-point, deliberately operator-calibrated display geometry. The points
/// represent reachable contact CENTERS, not mechanical button edges.
/// No calibration is inferred from the signed-12-bit protocol limits.
/// </summary>
public sealed record A2854TouchCalibration(
    A2854TouchReference Center,
    A2854TouchReference Left,
    A2854TouchReference Right,
    A2854TouchReference Top,
    A2854TouchReference Bottom)
{
    public static bool TryCreate(
        IReadOnlyDictionary<string, A2854TouchReference> references,
        out A2854TouchCalibration? calibration)
    {
        calibration = null;
        if (!references.TryGetValue("center", out var c) ||
            !references.TryGetValue("left", out var l) ||
            !references.TryGetValue("right", out var r) ||
            !references.TryGetValue("top", out var t) ||
            !references.TryGetValue("bottom", out var b))
            return false;
        // A visual projection needs a measurable, correctly oriented span.
        // Don't silently invert axes or accept 5 nearby touches as edges.
        // Determine directions from measured physical landmarks. A remote
        // firmware revision can invert either axis relative to our old guess.
        int left = l.X - c.X, right = r.X - c.X;
        int top = t.Y - c.Y, bottom = b.Y - c.Y;
        if (Math.Abs(left) < 100 || Math.Abs(right) < 100 ||
            Math.Abs(top) < 100 || Math.Abs(bottom) < 100 ||
            Math.Sign(left) == Math.Sign(right) ||
            Math.Sign(top) == Math.Sign(bottom))
            return false;
        calibration = new(c, l, r, t, b);
        return true;
    }

    /// <summary>
    /// Piecewise center-relative normalization supports asymmetric extents
    /// around the physical center. The 30px dot remains wholly inside
    /// A2854's 165px round WPF clip. This does not affect Windows SendInput.
    /// </summary>
    public (double X, double Y) Map(int x, int y)
    {
        double directionX = Math.Sign(Right.X - Center.X);
        double directionY = Math.Sign(Top.Y - Center.Y);
        double xFromCenter = (x - (double)Center.X) * directionX;
        double yFromCenter = (y - (double)Center.Y) * directionY;
        double relativeX = xFromCenter >= 0
            ? xFromCenter / Math.Abs(Right.X - Center.X)
            : xFromCenter / Math.Abs(Left.X - Center.X);
        double relativeY = yFromCenter >= 0
            ? yFromCenter / Math.Abs(Top.Y - Center.Y)
            : yFromCenter / Math.Abs(Bottom.Y - Center.Y);
        double dx = Math.Clamp(relativeX, -1, 1) * A2854TouchProjection.ContactCenterRadius;
        double dy = -Math.Clamp(relativeY, -1, 1) * A2854TouchProjection.ContactCenterRadius;
        double radius = Math.Sqrt(dx * dx + dy * dy);
        if (radius > A2854TouchProjection.ContactCenterRadius)
        {
            double scale = A2854TouchProjection.ContactCenterRadius / radius;
            dx *= scale;
            dy *= scale;
        }
        return (A2854TouchProjection.PadSize / 2 + dx,
                A2854TouchProjection.PadSize / 2 + dy);
    }
}

/// <summary>
/// Bounded median from recently observed real, single-finger contacts.
/// The UI labels each sample with the physical point the operator selected.
/// Hover, releases and older frames must never be reused as measurements.
/// </summary>
public sealed class A2854TouchSampleWindow
{
    readonly Queue<(int X, int Y, long Milliseconds)> _points = new();
    const int MaxSamples = 40;

    public void Add(int x, int y, long milliseconds)
    {
        _points.Enqueue((x, y, milliseconds));
        while (_points.Count > MaxSamples) _points.Dequeue();
    }

    public void Clear() => _points.Clear();

    public bool TryRead(long nowMilliseconds, out A2854TouchReference point)
    {
        point = default;
        var recent = _points.Where(p =>
            nowMilliseconds >= p.Milliseconds &&
            nowMilliseconds - p.Milliseconds <= 1200).ToArray();
        if (recent.Length < 5) return false;
        int[] xs = recent.Select(p => p.X).Order().ToArray();
        int[] ys = recent.Select(p => p.Y).Order().ToArray();
        point = new(xs[xs.Length / 2], ys[ys.Length / 2]);
        return true;
    }
}
