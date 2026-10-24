// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 AnthoDingo

using System.Globalization;
using System.Resources;

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

    /// <summary>Does the message start like the text of <paramref name="key"/> (up to the first parameter)? Used to classify states.</summary>
    public static bool StartsWith(string message, string key)
    {
        string format = T(key);
        int brace = format.IndexOf('{');
        return message.StartsWith(brace < 0 ? format : format[..brace], StringComparison.Ordinal);
    }
}
