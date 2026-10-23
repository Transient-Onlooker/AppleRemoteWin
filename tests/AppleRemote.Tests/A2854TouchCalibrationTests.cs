// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854TouchCalibrationTests
{
    static readonly Dictionary<string, A2854TouchReference> Samples = new()
    {
        ["center"] = new(-730, -410),
        ["left"] = new(-1750, -400),
        ["right"] = new(240, -420),
        ["top"] = new(-710, 780),
        ["bottom"] = new(-720, -1020),
    };

    [Fact]
    public void Captured_center_maps_to_actual_round_pad_center()
    {
        Assert.True(A2854TouchCalibration.TryCreate(Samples, out var profile));
        var p = profile!.Map(-730, -410);
        Assert.Equal(180, p.X, 6);
        Assert.Equal(180, p.Y, 6);
    }

    [Fact]
    public void Asymmetric_boundary_points_project_to_cardinal_ring_edges()
    {
        Assert.True(A2854TouchCalibration.TryCreate(Samples, out var profile));
        var p = profile!;
        Assert.Equal(30, p.Map(-1750, -410).X, 6);
        Assert.Equal(330, p.Map(240, -410).X, 6);
        Assert.Equal(30, p.Map(-730, 780).Y, 6);
        Assert.Equal(330, p.Map(-730, -1020).Y, 6);
    }

    [Fact]
    public void Inverted_sensor_axes_are_detected_from_physical_landmarks()
    {
        var inverted = new Dictionary<string, A2854TouchReference>
        {
            ["center"] = new(300, 250),
            ["left"] = new(1200, 250),
            ["right"] = new(-900, 250),
            ["top"] = new(300, -700),
            ["bottom"] = new(300, 1300),
        };
        Assert.True(A2854TouchCalibration.TryCreate(inverted, out var model));
        Assert.Equal(30, model!.Map(1200, 250).X, 6);
        Assert.Equal(330, model.Map(-900, 250).X, 6);
        Assert.Equal(30, model.Map(300, -700).Y, 6);
        Assert.Equal(330, model.Map(300, 1300).Y, 6);
    }

    [Fact]
    public void Off_axis_outliers_stay_within_the_round_marker_clip()
    {
        Assert.True(A2854TouchCalibration.TryCreate(Samples, out var profile));
        foreach (var pt in new[] { (-2048, -2048), (2047, 2047), (4000, -4000), (0, 0) })
        {
            var v = profile!.Map(pt.Item1, pt.Item2);
            var dx = v.X - 180;
            var dy = v.Y - 180;
            Assert.True(Math.Sqrt(dx * dx + dy * dy) <= 150.000001);
        }
    }

    [Fact]
    public void Calibration_refuses_missing_or_physically_invalid_edges()
    {
        var incomplete = Samples.Where(pair => pair.Key != "top")
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.False(A2854TouchCalibration.TryCreate(incomplete, out _));
        var inverted = Samples.ToDictionary(pair => pair.Key, pair => pair.Value);
        inverted["right"] = new(-1900, 0);
        Assert.False(A2854TouchCalibration.TryCreate(inverted, out _));
    }

    [Fact]
    public void A_calibration_window_requires_five_recent_active_samples()
    {
        var window = new A2854TouchSampleWindow();
        for (var i = 0; i < 4; ++i) window.Add(i, i, 1000 + i);
        Assert.False(window.TryRead(1004, out _));
        window.Add(20, 40, 1005);
        Assert.True(window.TryRead(1010, out var v));
        Assert.Equal(new A2854TouchReference(2, 2), v);
        Assert.False(window.TryRead(2500, out _));
    }

    [Fact]
    public void Median_is_robust_to_single_spurious_touch_frame()
    {
        var window = new A2854TouchSampleWindow();
        for (var i = 0; i < 9; i++) window.Add(-800 + i % 3, 100 + i % 3, 5000 + i);
        window.Add(2047, -2048, 5010);
        Assert.True(window.TryRead(5011, out var v));
        Assert.InRange(v.X, -800, -798);
        Assert.InRange(v.Y, 100, 102);
        window.Clear();
        Assert.False(window.TryRead(5011, out _));
    }
}
