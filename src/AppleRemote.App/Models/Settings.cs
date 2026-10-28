// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 AnthoDingo

using System.IO;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppleRemote.App.Models;

public enum SettingsPersistenceError { None, LoadFailed, SaveFailed }

/// <summary>Preferences in %AppData%\AppleRemote\settings.json.</summary>
public sealed class Settings : INotifyPropertyChanged
{
    readonly object _saveLock = new();
    string? _filePath;
    bool _saveBlocked;

    [JsonIgnore]
    public SettingsPersistenceError LastPersistenceError { get; private set; }
    [JsonIgnore]
    public Exception? PersistenceException { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool KeyboardEnabled { get; set; }
    public bool? A2854KeyboardEnabled { get; set; }
    /// <summary>Explicit opt-in for third-generation Siri Remote via dedicated Barrot 33FA:0001 WinUSB.</summary>
    public bool A2854Mode { get; set; }
    /// <summary>Opt-in Windows mouse movement/click synthesis, off by default.</summary>
    public bool A2854PointerEnabled { get; set; }
    /// <summary>
    /// When the A2854 pointer is enabled, use the configured center-button
    /// keyboard binding instead of a mouse left-click. Off by default.
    /// </summary>
    public bool A2854ClickAsKeyboard { get; set; }
    /// <summary>Relative cursor sensitivity in pixels per A2854 coordinate unit.</summary>
    public double A2854PointerSensitivity { get; set; } = 0.18;
    /// <summary>
    /// Use saved measured physical X/Y axes when available. This does not
    /// enable pointer input; invalid or missing profiles use the raw axes.
    /// </summary>
    public bool A2854PointerUseCalibration { get; set; } = true;
    public int A2854CalibrationDefaultsVersion { get; set; }
    public string? KeyMapPath { get; set; }
    /// <summary>Closing the window minimises it to the notification area (otherwise the app exits).</summary>
    public bool CloseToTray { get; set; } = true;
    /// <summary>Remote input -> exe to launch (replaces the key).</summary>
    public Dictionary<string, string> Software { get; set; } = new();
    /// <summary>Remote input -> "#Win+D" combo to send (replaces the key).</summary>
    public Dictionary<string, string> Combos { get; set; } = new();
    /// <summary>Remote input -> name of the macro to run (file in the macros folder).</summary>
    public Dictionary<string, string> Macros { get; set; } = new();
    /// <summary>Software added through "Browse", kept in the menu.</summary>
    public List<string> Apps { get; set; } = new();
    /// <summary>"auto" (Windows language), "fr" or "en"; applied at startup.</summary>
    public string Language { get; set; } = "auto";
    /// <summary>PnP instance ID of the USB Bluetooth adapter chosen for WinUSB / direct Bluetooth (null: the first one).</summary>
    public string? Adapter { get; set; }
    /// <summary>Last release tag a toast was shown for (one notification per version).</summary>
    public string? LastNotifiedVersion { get; set; }
    public bool NotifyUpdates { get; set; } = true;
    /// <summary>Download and run the setup of a new release without asking (off by default: it raises a UAC prompt).</summary>
    public bool AutoUpdate { get; set; }
    public DateTime? LastUpdateCheck { get; set; }
    /// <summary>DriverVer of the filter package this app installed (the installed driver cannot be read without elevation).</summary>
    public string? FilterVersion { get; set; }

    public static string FilePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppleRemote", "settings.json");

    public static Settings Load() => Load(FilePath);

    internal static Settings Load(string path)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path))
                ?? throw new JsonException("Settings must be a JSON object.");
            settings._filePath = path;
            settings.ApplyCalibrationDefaults();
            settings.ApplyKeyboardDefaults(existingSettings: true);
            return settings;
        }
        catch (FileNotFoundException)
        {
            var defaults = new Settings { _filePath = path };
            defaults.ApplyCalibrationDefaults();
            defaults.ApplyKeyboardDefaults(existingSettings: false);
            return defaults;
        }
        catch (DirectoryNotFoundException)
        {
            var defaults = new Settings { _filePath = path };
            defaults.ApplyCalibrationDefaults();
            defaults.ApplyKeyboardDefaults(existingSettings: false);
            return defaults;
        }
        catch (Exception e)
        {
            var defaults = new Settings { _filePath = path, _saveBlocked = true,
                LastPersistenceError = SettingsPersistenceError.LoadFailed, PersistenceException = e };
            defaults.ApplyCalibrationDefaults();
            defaults.ApplyKeyboardDefaults(existingSettings: true);
            return defaults;
        }
    }

    internal void ApplyKeyboardDefaults(bool existingSettings)
    {
        // Legacy files cannot distinguish a default OFF from an explicit OFF.
        A2854KeyboardEnabled ??= existingSettings ? KeyboardEnabled : true;
    }

    internal void ApplyCalibrationDefaults()
    {
        if (A2854CalibrationDefaultsVersion >= 1) return;
        // Earlier releases defaulted this option off, including saved files.
        // Migrate once, then preserve an explicit opt-out on later launches.
        A2854PointerUseCalibration = true;
        A2854CalibrationDefaultsVersion = 1;
    }

    public void Save()
    {
        SettingsPersistenceError error;
        Exception? failure;
        bool errorChanged, exceptionChanged;
        lock (_saveLock)
        {
            if (_saveBlocked) return;
            string? temporary = null;
            try
            {
                string path = _filePath ?? FilePath;
                string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
                Directory.CreateDirectory(directory);
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true))
                    {
                        writer.Write(json);
                        writer.Flush();
                    }
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
                error = SettingsPersistenceError.None;
                failure = null;
            }
            catch (Exception e)
            {
                error = SettingsPersistenceError.SaveFailed;
                failure = e;
            }
            finally
            {
                if (temporary is not null)
                {
                    try { File.Delete(temporary); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            errorChanged = LastPersistenceError != error;
            exceptionChanged = PersistenceException != failure;
            LastPersistenceError = error;
            PersistenceException = failure;
        }
        if (errorChanged) PropertyChanged?.Invoke(this, new(nameof(LastPersistenceError)));
        if (exceptionChanged) PropertyChanged?.Invoke(this, new(nameof(PersistenceException)));
    }
}
