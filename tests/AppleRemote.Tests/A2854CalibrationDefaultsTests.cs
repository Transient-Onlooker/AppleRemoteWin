using System.Text.Json;
using AppleRemote.App.Models;
using Xunit;

namespace AppleRemote.Tests;

public class A2854CalibrationDefaultsTests
{
    [Fact]
    public void Existing_default_off_migrates_without_enabling_input()
    {
        var settings = JsonSerializer.Deserialize<Settings>(
            """{"A2854Mode":true,"A2854PointerUseCalibration":false,"A2854PointerSensitivity":0.42}""")!;

        settings.ApplyCalibrationDefaults();

        Assert.True(settings.A2854PointerUseCalibration);
        Assert.Equal(1, settings.A2854CalibrationDefaultsVersion);
        Assert.False(settings.A2854PointerEnabled);
        Assert.False(settings.KeyboardEnabled);
        Assert.Equal(0.42, settings.A2854PointerSensitivity);
        Assert.True(settings.A2854Mode);
    }

    [Fact]
    public void Explicit_opt_out_survives_serialization_and_next_start()
    {
        var settings = new Settings();
        settings.ApplyCalibrationDefaults();
        settings.A2854PointerUseCalibration = false;

        var reloaded = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;
        reloaded.ApplyCalibrationDefaults();

        Assert.False(reloaded.A2854PointerUseCalibration);
    }

    [Fact]
    public void Fresh_settings_prefer_saved_calibration_but_leave_input_off()
    {
        var settings = new Settings();
        settings.ApplyCalibrationDefaults();

        Assert.True(settings.A2854PointerUseCalibration);
        Assert.False(settings.A2854PointerEnabled);
        Assert.False(settings.KeyboardEnabled);
        Assert.False(settings.A2854Mode);
    }
}
