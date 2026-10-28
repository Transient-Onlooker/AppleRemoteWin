// SPDX-License-Identifier: AGPL-3.0-or-later

using AppleRemote.App.Models;
using System.Text.Json;
using Xunit;

namespace AppleRemote.Tests;

public sealed class SettingsPersistenceTests
{
    [Fact]
    public void Save_replaces_settings_with_previous_version_as_backup()
    {
        string directory = Path.Combine(Path.GetTempPath(), "AppleRemote-settings-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "settings.json");
            var settings = Settings.Load(path);
            Assert.True(settings.A2854KeyboardEnabled);
            settings.Language = "ko";
            settings.Save();
            Assert.Equal(SettingsPersistenceError.None, settings.LastPersistenceError);
            Assert.False(File.Exists(path + ".bak"));
            string original = File.ReadAllText(path);

            settings.Language = "en";
            settings.Save();
            Assert.Equal(SettingsPersistenceError.None, settings.LastPersistenceError);
            Assert.Equal(original, File.ReadAllText(path + ".bak"));
            Assert.Equal("en", Settings.Load(path).Language);
            string second = File.ReadAllText(path);
            settings.Language = "fr";
            settings.Save();
            Assert.Equal(SettingsPersistenceError.None, settings.LastPersistenceError);
            Assert.Equal(second, File.ReadAllText(path + ".bak"));
            Assert.Equal("fr", Settings.Load(path).Language);
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            Assert.False(json.RootElement.TryGetProperty("LastPersistenceError", out _));
            Assert.False(json.RootElement.TryGetProperty("PersistenceException", out _));
            Assert.Single(Directory.GetFiles(directory, "*.json"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("null")]
    public void Unreadable_load_blocks_every_save_and_preserves_file(string content)
    {
        string directory = Path.Combine(Path.GetTempPath(), "AppleRemote-settings-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, content);
            var settings = Settings.Load(path);
            Assert.Equal(SettingsPersistenceError.LoadFailed, settings.LastPersistenceError);
            Assert.NotNull(settings.PersistenceException);
            Assert.False(settings.A2854KeyboardEnabled);
            settings.Language = "ko";
            settings.Save();
            settings.Save();
            Assert.Equal(SettingsPersistenceError.LoadFailed, settings.LastPersistenceError);
            Assert.Equal(content, File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Failed_replacement_keeps_original_and_notifies_then_retry_clears_error()
    {
        string directory = Path.Combine(Path.GetTempPath(), "AppleRemote-settings-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, "{\"KeyboardEnabled\":true,\"Language\":\"fr\"}");
            var settings = Settings.Load(path);
            Assert.True(settings.A2854KeyboardEnabled);
            Assert.True(settings.A2854PointerUseCalibration);
            string original = File.ReadAllText(path);
            Directory.CreateDirectory(path + ".bak");
            var changes = new List<string?>();
            settings.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
            settings.Language = "ko";
            settings.Save();
            Assert.Equal(SettingsPersistenceError.SaveFailed, settings.LastPersistenceError);
            Assert.NotNull(settings.PersistenceException);
            Assert.Equal(original, File.ReadAllText(path));
            Assert.Contains(nameof(Settings.LastPersistenceError), changes);
            Assert.Contains(nameof(Settings.PersistenceException), changes);
            Assert.Single(Directory.GetFiles(directory));

            Directory.Delete(path + ".bak");
            settings.Save();
            Assert.Equal(SettingsPersistenceError.None, settings.LastPersistenceError);
            Assert.Null(settings.PersistenceException);
            Assert.Equal(original, File.ReadAllText(path + ".bak"));
            Assert.Equal("ko", Settings.Load(path).Language);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
