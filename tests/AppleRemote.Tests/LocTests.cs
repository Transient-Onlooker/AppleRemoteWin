// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 AnthoDingo

using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Xunit;

namespace AppleRemote.Tests;

public class LocTests
{
    static readonly ResourceManager Rm = new("AppleRemote.Strings", typeof(Loc).Assembly);

    static Dictionary<string, string> Load(string culture) =>
        Rm.GetResourceSet(CultureInfo.GetCultureInfo(culture), true, false)!
          .Cast<System.Collections.DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);

    [Fact]
    public void English_has_every_french_key_with_the_same_placeholders()
    {
        var fr = Load("fr");
        var en = Load("en");
        Assert.Equal(fr.Keys.Order(), en.Keys.Order());
        foreach (var (key, text) in fr)
            Assert.Equal(Placeholders(text), Placeholders(en[key]));
    }

    [Fact]
    public void Korean_has_every_key_and_the_same_format_placeholders()
    {
        var fr = Load("fr");
        var en = Load("en");
        var ko = Load("ko");
        Assert.Equal(fr.Keys.Order(), ko.Keys.Order());
        Assert.Equal(en.Keys.Order(), ko.Keys.Order());
        foreach (var (key, text) in ko)
        {
            Assert.Equal(Placeholders(en[key]), Placeholders(text));
            Assert.NotEqual(key, text); // missing translations silently render resource keys
        }
        Assert.Equal("설정", ko["Nav.Settings"]);
        Assert.Equal("다시 연결", ko["Remote.Reconnect"]);
        Assert.Equal("음소거", ko["A2854.Mapping.Mute"]);
    }

    [Theory]
    [InlineData("ko", "en", "ko")]
    [InlineData("en", "ko", "en")]
    [InlineData("fr", "ko", "fr")]
    [InlineData("auto", "ko", "ko")]
    [InlineData("auto", "fr", "fr")]
    [InlineData("auto", "de", "en")]
    [InlineData("invalid", "ko", "ko")]
    public void Language_resolution_supports_Korean_and_preserves_fallback(
        string preference, string windows, string expected)
    {
        Assert.Equal(expected, Loc.ResolveCulture(preference, windows).TwoLetterISOLanguageName);
    }

    static string Placeholders(string s) => string.Join(",", Regex.Matches(s, @"\{\d+[^}]*\}").Select(m => m.Value).Order());

    [Fact]
    public void SetLanguage_switches_the_texts_and_status_prefixes_still_match()
    {
        var previous = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            Loc.SetLanguage("en");
            Assert.Equal("Stopped.", Loc.T("Status.Stopped"));
            Assert.True(Loc.StartsWith(Loc.T("Status.ConnectedHid", "Siri Remote"), "Status.ConnectedHid"));
            Loc.SetLanguage("ko");
            Assert.Equal("중지됨", Loc.T("Status.Stopped"));
            Assert.True(Loc.StartsWith(Loc.T("Status.ConnectedHid", "Siri Remote"), "Status.ConnectedHid"));
            Loc.SetLanguage("fr");
            Assert.Equal("Arrêté.", Loc.T("Status.Stopped"));
            Assert.Equal("Inconnue", Loc.T("Inconnue"));   // unknown key returned as is
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture = previous ?? CultureInfo.InvariantCulture;
        }
    }
}
