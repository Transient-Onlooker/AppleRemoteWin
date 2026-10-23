// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;

namespace AppleRemote;

/// <summary>
/// Stores the operator's actual physical five-point clickpad measurements.
/// Data contain only signed touch coordinates. This file is entirely separate
/// from the DPAPI LTK bond and cannot change BLE/controller configuration.
/// </summary>
public static class A2854TouchCalibrationStore
{
    static readonly HashSet<string> Names =
        new(StringComparer.Ordinal) { "center", "left", "right", "top", "bottom" };

    public static Dictionary<string, A2854TouchReference> Load(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, A2854TouchReference>(StringComparer.Ordinal);
        var loaded = JsonSerializer.Deserialize<Dictionary<string, A2854TouchReference>>(
            File.ReadAllText(path)) ?? throw new InvalidDataException("Empty touch calibration document.");
        var safe = new Dictionary<string, A2854TouchReference>(StringComparer.Ordinal);
        foreach (var (name, value) in loaded)
        {
            if (!Names.Contains(name))
                throw new InvalidDataException("Unknown touch calibration landmark.");
            if (value.X is < -2048 or > 2047 || value.Y is < -2048 or > 2047)
                throw new InvalidDataException("Touch calibration coordinates must be signed 12-bit.");
            safe.Add(name, value);
        }
        return safe;
    }

    public static void Save(string path, IReadOnlyDictionary<string, A2854TouchReference> points)
    {
        foreach (var (name, value) in points)
        {
            if (!Names.Contains(name) ||
                value.X is < -2048 or > 2047 || value.Y is < -2048 or > 2047)
                throw new ArgumentException("Invalid touch calibration landmark.", nameof(points));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        // Fail closed if the old file is corrupt: never replace irreplaceable
        // physical measurements without first being able to read them.
        if (File.Exists(path))
        {
            var old = Load(path);
            if (A2854TouchCalibration.TryCreate(old, out _))
                File.Copy(path, path + ".bak", overwrite: true);
        }

        string temp = Path.Combine(directory,
            "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(points,
                new JsonSerializerOptions { WriteIndented = true }));
            // Same-directory rename prevents torn JSON if the app is closed
            // during Save; existing full calibration stays in the .bak file.
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
