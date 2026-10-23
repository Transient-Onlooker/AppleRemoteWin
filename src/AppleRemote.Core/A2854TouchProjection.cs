// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

/// <summary>
/// Stable A2854 touch-display projection. The decoder confirms signed 12-bit
/// X/Y values but the physical clickpad extrema have not yet been measured.
/// Use the complete signed domain rather than reusing the gen-1 calibration,
/// invert the upward-growing Y axis, and clip dots inside a round clickpad.
/// This is a display-only mapping; it never changes pointer injection.
/// </summary>
public static class A2854TouchProjection
{
    public const double PadSize = 360;
    public const double DotSize = 30;
    // The WPF ellipse clips at radius 165 px (center 180). Its 30 px
    // contact marker must fit fully inside that clip, not just its center.
    // The display space is 360 x 360; usable center radius is 165 - 15.
    public const double VisualClipRadius = 165;
    public const double ContactCenterRadius = VisualClipRadius - DotSize / 2;
    const int Min = -2048, Max = 2047;

    public static (double X, double Y) Map(int x, int y)
    {
        double px = Math.Clamp((x - (double)Min) / (Max - (double)Min), 0, 1) * PadSize;
        double py = (1 - Math.Clamp((y - (double)Min) / (Max - (double)Min), 0, 1)) * PadSize;
        double center = PadSize / 2;
        double dx = px - center, dy = py - center;
        double radius = ContactCenterRadius;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance > radius)
        {
            double scale = radius / distance;
            dx *= scale;
            dy *= scale;
        }
        return (center + dx, center + dy);
    }
}

