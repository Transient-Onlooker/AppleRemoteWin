// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

/// <summary>
/// Convert an explicitly confirmed keyboard capture from the existing
/// #Modifier+Key editor into the normal key-map or one-shot chord format.
/// This is pure parsing: it never listens for or injects keyboard input.
/// </summary>
public static class CapturedKeyBinding
{
    static readonly string[] ModifierNames = ["Ctrl", "Alt", "Shift", "Win"];

    public static string? FromRecordedCombo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith('#'))
            return null;

        var parts = value[1..].Split('+');
        if (parts.Length == 0 || parts.Any(string.IsNullOrWhiteSpace))
            return null;

        if (!Keys.Names.TryGetValue(parts[^1], out KeyCode key))
            return null;
        // A single key must be stored as an actual held key, not a momentary
        // shortcut. This also canonicalizes aliases like Esc -> Escape.
        string canonicalKey = Keys.NameOf(key)!;
        if (parts.Length == 1)
            return canonicalKey;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var modifier in parts[..^1])
            if (!ModifierNames.Contains(modifier, StringComparer.OrdinalIgnoreCase) ||
                !seen.Add(modifier))
                return null;

        return "#" + string.Join("+", ModifierNames.Where(seen.Contains)) + "+" + canonicalKey;
    }
}
