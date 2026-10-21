using LyricClone.Core.Playback;
using LyricClone.Core.Settings;
using LyricClone.Core.Views;

namespace LyricClone.Core.Tests;

public sealed class SettingsLogicTests
{
    [Theory]
    [InlineData("#FFAA00", 0xFFFFAA00u)]
    [InlineData("ffaa00", 0xFFFFAA00u)]
    [InlineData(" #fa0 ", 0xFFFFAA00u)]
    [InlineData("#80112233", 0x80112233u)]
    public void Colour_hex_accepts_short_long_and_alpha_forms(string text, uint expected)
    {
        Assert.True(ColorHex.TryParse(text, out var v));
        Assert.Equal(expected, v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    public void Colour_hex_rejects_anything_else(string? text) => Assert.False(ColorHex.TryParse(text, out _));

    [Fact]
    public void Colour_hex_normalises_to_upper_case_and_drops_opaque_alpha()
    {
        Assert.Equal("#FFAA00", ColorHex.Normalize("#fa0"));
        Assert.Equal("#FFAA00", ColorHex.Normalize("#ffffaa00"));
        Assert.Equal("#80112233", ColorHex.Normalize("80112233"));
        Assert.Null(ColorHex.Normalize("nope"));
    }

    [Theory]
    [InlineData(true, 0, false, false, PlayerHintKind.SmtcUnavailable)]
    [InlineData(false, 0, false, true, PlayerHintKind.NoPlayer)]
    [InlineData(false, 2, false, true, PlayerHintKind.AllIgnored)]
    [InlineData(false, 1, true, true, PlayerHintKind.None)]
    [InlineData(false, 0, false, false, PlayerHintKind.None)]
    public void Player_hint_is_shown_only_when_needed(bool unavailable, int sessions, bool active, bool enabled, PlayerHintKind expected) =>
        Assert.Equal(expected, PlayerHint.Decide(unavailable, sessions, active, enabled));

    [Theory]
    [InlineData("Ctrl+Alt+L", true, true, false, false, 0x4C)]
    [InlineData("ctrl+shift+F2", true, false, true, false, 0x71)]
    public void Hotkey_parses_modifiers_and_key(string text, bool ctrl, bool alt, bool shift, bool win, int vk)
    {
        Assert.True(HotkeyChord.TryParse(text, out var c));
        Assert.Equal((ctrl, alt, shift, win, vk), (c.Ctrl, c.Alt, c.Shift, c.Win, c.VirtualKey));
        Assert.True(HotkeyChord.TryParse(c.ToString(), out var roundtrip));
        Assert.Equal(c, roundtrip);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("L")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt+L+M")]
    public void Hotkey_rejects_missing_modifier_or_junk(string? text) => Assert.False(HotkeyChord.TryParse(text, out _));

    [Fact]
    public void Taskbar_strip_sits_left_of_the_tray_on_a_bottom_bar_and_hides_otherwise()
    {
        var shown = TaskbarLayout.Place(TaskbarEdge.Bottom, 0, 1040, 1920, 40, autoHide: false, fullscreenApp: false);
        Assert.True(shown.Visible);
        Assert.Equal(72, shown.Left);
        Assert.Equal(1920 - 72 - 160, shown.Width);

        Assert.False(TaskbarLayout.Place(TaskbarEdge.Bottom, 0, 1040, 1920, 40, autoHide: true, fullscreenApp: false).Visible);
        Assert.False(TaskbarLayout.Place(TaskbarEdge.Bottom, 0, 1040, 1920, 40, autoHide: false, fullscreenApp: true).Visible);
        var vertical = TaskbarLayout.Place(TaskbarEdge.Left, 0, 0, 48, 1080, false, false);
        Assert.True(vertical.Unsupported);
        Assert.False(vertical.Visible);
    }

    [Fact]
    public void Island_side_cover_is_treated_as_the_default_transport()
    {
        var island = new IslandSettings { SideContent = IslandSideContent.Cover };
        Assert.Equal(IslandSideContent.Default, island.EffectiveSideContent);

        var settings = new AppSettings { Island = island };
        settings.Normalize();
        Assert.Equal(IslandSideContent.Default, settings.Island.SideContent);
        Assert.Equal(IslandSideContent.Hidden, new IslandSettings { SideContent = IslandSideContent.Hidden }.EffectiveSideContent);
    }
}
