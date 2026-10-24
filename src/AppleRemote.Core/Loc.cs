// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 AnthoDingo

using System.Globalization;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;

namespace AppleRemote;

/// <summary>
/// Translations: Strings.resx (French), Strings.en.resx (English),
/// Strings.ko.resx (Korean). Follows <see cref="CultureInfo.CurrentUICulture"/>.
/// Unknown keys are returned as-is for diagnosis.
/// </summary>
public static class Loc
{
    static readonly ResourceManager Resources = new("AppleRemote.Strings", typeof(Loc).Assembly);

    /// <summary>
    /// Select fr/en/ko or "auto" from Windows display language. An unsupported
    /// locale intentionally falls back to English, matching legacy behavior.
    /// Kept pure so language resolution can be unit tested without mutating
    /// process-wide UI culture.
    /// </summary>
    public static CultureInfo ResolveCulture(string? language, string? windowsLanguage = null)
    {
        string requested = language is "fr" or "en" or "ko"
            ? language
            : windowsLanguage ??
              CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
        return CultureInfo.GetCultureInfo(requested is "fr" or "en" or "ko"
            ? requested : "en");
    }

    /// <summary>Notifies loaded embedded editors to refresh their display locale.</summary>
    public static event Action? LanguageChanged;

    public static void SetLanguage(string? language)
    {
        var culture = ResolveCulture(language);
        bool changed = !string.Equals(CultureInfo.CurrentUICulture.Name,
            culture.Name, StringComparison.OrdinalIgnoreCase);
        CultureInfo.DefaultThreadCurrentUICulture =
            CultureInfo.CurrentUICulture = culture;
        if (changed) LanguageChanged?.Invoke();
    }

    public static string T(string key) => Resources.GetString(key) ?? key;

    public static string T(string key, params object?[] args) => string.Format(T(key), args);

    /// <summary>
    /// Recovers format arguments from a status string while its originating
    /// UI culture is still active. Callers can then reformat the *same* status
    /// after a language change without losing a device label or an HCI reason.
    /// Never guesses unknown messages and never logs or persists the values.
    /// </summary>
    public static bool TryCaptureStatus(string message, string key, out object?[] arguments)
    {
        arguments = [];
        if (string.IsNullOrEmpty(message) || message.Length > 1024) return false;

        string template = T(key);
        if (template == key || template.Length > 1024) return false;

        var placeholders = Regex.Matches(template, @"\{(?<index>[0-9]+)(?::(?<spec>[^}]+))?\}");
        if (placeholders.Count == 0 || placeholders.Count > 4) return false;

        var pattern = new StringBuilder(@"\A");
        var formats = new Dictionary<int, string>();
        int cursor = 0, maxIndex = -1;
        foreach (Match ph in placeholders)
        {
            if (!int.TryParse(ph.Groups["index"].Value, out int idx) ||
                idx is < 0 or > 3 || formats.ContainsKey(idx))
                return false; // no ambiguous repeated argument substitution
            formats.Add(idx, ph.Groups["spec"].Value);
            maxIndex = Math.Max(maxIndex, idx);
            pattern.Append(Regex.Escape(template[cursor..ph.Index]));
            pattern.Append($"(?<value{idx}>.*?)");
            cursor = ph.Index + ph.Length;
        }
        pattern.Append(Regex.Escape(template[cursor..]));
        pattern.Append(@"\z");

        Match matched;
        try
        {
            matched = Regex.Match(message, pattern.ToString(),
                RegexOptions.Singleline | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
        }
        catch (RegexMatchTimeoutException) { return false; }
        if (!matched.Success || formats.Count != maxIndex + 1) return false;

        var captured = new object?[maxIndex + 1];
        foreach (var (idx, spec) in formats)
        {
            string value = matched.Groups[$"value{idx}"].Value;
            if (spec.StartsWith("X", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out int code))
                    return false;
                captured[idx] = code;
            }
            else if (spec.Length == 0)
                captured[idx] = value;
            else
                return false; // avoid fabricating an unsupported formatted value
        }
        arguments = captured;
        return true;
    }

    /// <summary>Does the message start like the text of <paramref name="key"/> (up to the first parameter)? Used to classify states.</summary>
    public static bool StartsWith(string message, string key)
    {
        string format = T(key);
        int brace = format.IndexOf('{');
        return message.StartsWith(brace < 0 ? format : format[..brace], StringComparison.Ordinal);
    }
}
