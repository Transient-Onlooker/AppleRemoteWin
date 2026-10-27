using System.Text.Json;
using AppleRemote.App.Models;
using Xunit;

namespace AppleRemote.Tests;

public class A2854KeyboardDefaultsTests
{
    [Fact]
    public void Fresh_settings_enable_only_a2854_keyboard()
    {
        var settings = new Settings();
        settings.ApplyKeyboardDefaults(existingSettings: false);

        Assert.True(settings.A2854KeyboardEnabled);
        Assert.False(settings.KeyboardEnabled);
        Assert.False(settings.A2854Mode);
        Assert.False(settings.A2854PointerEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_shared_choice_is_preserved_for_both_models(bool enabled)
    {
        var settings = JsonSerializer.Deserialize<Settings>(
            enabled ? """{"KeyboardEnabled":true,"A2854Mode":true}"""
                    : """{"KeyboardEnabled":false,"A2854Mode":true}""")!;
        settings.ApplyKeyboardDefaults(existingSettings: true);

        Assert.Equal(enabled, settings.KeyboardEnabled);
        Assert.Equal(enabled, settings.A2854KeyboardEnabled);
    }

    [Fact]
    public void Explicit_a2854_opt_out_survives_reload_and_does_not_change_gen1()
    {
        var settings = new Settings { KeyboardEnabled = true };
        settings.ApplyKeyboardDefaults(existingSettings: false);
        settings.A2854KeyboardEnabled = false;

        var reloaded = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;
        reloaded.ApplyKeyboardDefaults(existingSettings: true);
        reloaded.ApplyKeyboardDefaults(existingSettings: true);

        Assert.False(reloaded.A2854KeyboardEnabled);
        Assert.True(reloaded.KeyboardEnabled);
    }

    [Fact]
    public void Existing_settings_without_keyboard_field_stay_off()
    {
        var settings = JsonSerializer.Deserialize<Settings>("{}")!;
        settings.ApplyKeyboardDefaults(existingSettings: true);
        Assert.False(settings.A2854KeyboardEnabled);
    }
}
