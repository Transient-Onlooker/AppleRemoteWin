// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class CapturedKeyBindingTests
{
    [Theory]
    [InlineData("#F5", "F5")]
    [InlineData("#Space", "Space")]
    [InlineData("#Esc", "Escape")]
    [InlineData("#Enter", "Enter")]
    [InlineData("#D0", "D0")]
    [InlineData("#Ctrl+C", "#Ctrl+C")]
    [InlineData("#Win+D", "#Win+D")]
    [InlineData("#Shift+Ctrl+F5", "#Ctrl+Shift+F5")]
    public void Recorded_keys_and_shortcuts_convert_to_existing_mapping_formats(
        string captured, string expected)
    {
        Assert.Equal(expected, CapturedKeyBinding.FromRecordedCombo(captured));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("#Ctrl")]
    [InlineData("#Ctrl+")]
    [InlineData("#NotAKey")]
    [InlineData("Ctrl+C")]
    [InlineData("#Ctrl+Ctrl+C")]
    [InlineData("#Ctrl+C+F5")]
    public void Incomplete_or_unsupported_captures_never_replace_an_assignment(string? captured)
    {
        Assert.Null(CapturedKeyBinding.FromRecordedCombo(captured));
    }

    [Fact]
    public void Single_key_is_valid_key_map_input_and_preserves_held_semantics()
    {
        string single = CapturedKeyBinding.FromRecordedCombo("#F5")!;
        var map = KeyMap.Default();
        map.Set("click", Keys.Names[single]);
        Assert.True(map.TryGet("click", out var code));
        Assert.Equal(Keys.Names["F5"], code);
    }
}
