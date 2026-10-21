using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using LyricClone.App.Lyrics;
using LyricClone.App.Services;
using LyricClone.App.Views;
using LyricClone.Core.Settings;
using LyricClone.Infrastructure.Lyrics;
using static LyricClone.Flows.Tests.UiDriver;
using ThemeMode = LyricClone.Core.Settings.ThemeMode;

namespace LyricClone.Flows.Tests;

/// <summary>F09 (views on / off, lock) and the settings screens that drive F07.</summary>
public sealed partial class ViewsFlowTests
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20;
    private const long WS_EX_TOOLWINDOW = 0x80;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    private static long ExStyle(Window w) => GetWindowLongPtr(new WindowInteropHelper(w).Handle, GWL_EXSTYLE);

    [Fact]
    public void F09_H1_desktop_lyrics_lock_makes_window_click_through_and_unlock_restores()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            var w = new DesktopLyricsWindow(np, env.Settings, () => { }) { ShowActivated = false };
            w.Show();
            Assert.Equal(0, ExStyle(w) & WS_EX_TRANSPARENT);

            await env.Settings.UpdateAsync(s => s.Desktop.Locked = true);
            await Sta.Until(() => (ExStyle(w) & WS_EX_TRANSPARENT) != 0, "click-through on lock");
            await env.Settings.UpdateAsync(s => s.Desktop.Locked = false);
            await Sta.Until(() => (ExStyle(w) & WS_EX_TRANSPARENT) == 0, "click-through off on unlock");

            env.Sessions.Play("demo.player", "示例曲目", "虚构乐队", positionMs: 1_200);
            var current = Sta.Find<LyricLineView>(w, "CurrentLine");
            await Sta.Until(() => current.Line?.Text == "示例 A 的第一行", "desktop shows the current line");
            w.Close();
        });
    }

    [Fact]
    public void F09_H2_island_is_a_tool_window_hides_when_idle_and_shows_when_playing()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            var w = new IslandWindow(np, env.Settings) { ShowActivated = false };
            w.Show();
            Assert.NotEqual(0, ExStyle(w) & WS_EX_TOOLWINDOW);
            await Sta.Until(() => w.Opacity < 0.01, "hidden while idle");

            env.Sessions.Play("demo.player", "示例曲目", "虚构乐队", positionMs: 5_500);
            await Sta.Until(() => w.Opacity > 0.99, "visible while playing");
            var line = Sta.Find<LyricLineView>(w, "Line");
            await Sta.Until(() => line.Line?.Text == "示例 A 的第二行", "island shows the current line");

            var area = SystemParameters.WorkArea;
            Assert.InRange(w.Left + w.ActualWidth / 2, area.Left + area.Width / 2 - 2, area.Left + area.Width / 2 + 2);

            await env.Settings.UpdateAsync(s => s.Island.AutoHideWhenIdle = false);
            env.Sessions.Stop();
            await Task.Delay(400);
            Assert.True(w.Opacity > 0.99, "stays visible when auto-hide is off");

            w.HideFromMouse();
            Assert.True(w.Opacity < 0.01, "hides when the pointer is over the island");
            Assert.NotEqual(0, ExStyle(w) & WS_EX_TRANSPARENT);
            w.Close();
        });
    }

    [Fact]
    public void F09_E1_island_has_no_playback_controls()
    {
        using var env = new FlowEnv();
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            var w = new IslandWindow(np, env.Settings) { ShowActivated = false };
            w.Show();
            env.Sessions.Play("demo.player", "示例曲目", "虚构乐队");
            Assert.Empty(Descendants<Button>(w));
            Assert.Null(w.FindName("Header"));
            Assert.Null(w.FindName("TitleText"));
            w.Close();
        });
    }

    [Fact]
    public void F07_H3_settings_per_app_delay_and_source_order_are_saved()
    {
        using var env = new FlowEnv();
        Sta.Run(async () =>
        {
            env.Sessions.Play("Demo.Player_abc!App", "示例曲目", "虚构乐队");
            var w = new SettingsWindow(env.Settings, env.Sessions, () => { }, "test-agent") { ShowActivated = false, Left = -10_000 };
            w.Show();
            w.ShowPage("Apps");
            var list = Sta.Find<ListBox>(w, "AppList");
            await Sta.Until(() => list.Items.Count == 1 && list.SelectedIndex == 0, "app listed and selected");

            var delay = Sta.Find<TextBox>(w, "AppDelayBox");
            delay.Text = "180";
            delay.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, delay, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            await Sta.Until(() => env.Settings.Current.AppProfiles.TryGetValue("demo.player_abc!app", out var p) && p.SessionDelayMs == 180, "delay saved (case-insensitive id)");
            Assert.Equal("Player", env.Settings.Current.AppProfiles["Demo.Player_abc!App"].DisplayName);

            var custom = Sta.Find<CheckBox>(w, "AppCustomOrderBox");
            custom.IsChecked = true;
            custom.RaiseEvent(new RoutedEventArgs(ButtonBaseClick));
            await Sta.Until(() => env.Settings.Current.AppProfiles["Demo.Player_abc!App"].SourceOrder is { Count: 6 }, "custom order saved");

            var order = Sta.Find<ListBox>(w, "AppOrderList");
            order.SelectedIndex = 5;
            Press(ButtonWithText(Sta.Find<FrameworkElement>(w, "AppOrderPanel"), "上移"));
            await Sta.Until(() => env.Settings.Current.AppProfiles["Demo.Player_abc!App"].SourceOrder![4] == "lrclib", "lrclib moved up");

            delay.Text = "abc";
            delay.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, delay, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            await Task.Delay(100);
            Assert.Equal(180, env.Settings.Current.AppProfiles["Demo.Player_abc!App"].SessionDelayMs);

            Press(ButtonWithText(w, "恢复这个应用的默认设置"));
            await Sta.Until(() => env.Settings.Current.AppProfiles.Count == 0, "profile reset");
            w.Close();
        });
    }

    private static RoutedEvent ButtonBaseClick => System.Windows.Controls.Primitives.ButtonBase.ClickEvent;

    [Fact]
    public void F09_E2_theme_switch_changes_brushes_live()
    {
        using var env = new FlowEnv();
        Sta.Run(async () =>
        {
            var w = new SettingsWindow(env.Settings, env.Sessions, () => { }, "test-agent") { ShowActivated = false, Left = -10_000 };
            w.Show();
            var light = ((System.Windows.Media.SolidColorBrush)w.Background).Color;

            ThemeService.Apply(ThemeMode.Dark);
            var dark = ((System.Windows.Media.SolidColorBrush)w.Background).Color;
            Assert.NotEqual(light, dark);
            Assert.True(dark.R < 0x40, "dark background");

            ThemeService.Apply(ThemeMode.Light);
            Assert.Equal(light, ((System.Windows.Media.SolidColorBrush)w.Background).Color);
            w.Close();
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void F09_E3_desktop_font_cleared_falls_back_to_default()
    {
        using var env = new FlowEnv();
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            await env.Settings.UpdateAsync(s => s.Desktop.FontFamily = "Consolas");
            var w = new DesktopLyricsWindow(np, env.Settings, () => { }) { ShowActivated = false };
            w.Show();
            var current = Sta.Find<LyricLineView>(w, "CurrentLine");
            Assert.Equal("Consolas", current.FontFamily.Source);
            await env.Settings.UpdateAsync(s => s.Desktop.FontFamily = null);
            var fallback = (System.Windows.Media.FontFamily)w.FindResource("Font.Sans");
            await Sta.Until(() => current.FontFamily == fallback, "default font");
            w.Close();
        });
    }

    [Fact]
    public void F09_settings_preview_follows_translation_and_disables_view_toggles()
    {
        using var env = new FlowEnv();
        Sta.Run(async () =>
        {
            var w = new SettingsWindow(env.Settings, env.Sessions, () => { }, "test-agent") { ShowActivated = false, Left = -10_000 };
            w.Show();
            w.ShowPage("Views");
            var preview = Sta.Find<LyricLineView>(w, "ViewPreviewCurrent");
            var desktopTr = Sta.Find<CheckBox>(w, "DesktopTranslationBox");
            Assert.True(preview.ShowTranslation);
            Assert.Equal("你和我", preview.Line?.Translation);
            Assert.True(desktopTr.IsEnabled);

            await env.Settings.UpdateAsync(s => s.ShowChineseTranslation = false);
            await Sta.Until(() => !preview.ShowTranslation && !desktopTr.IsEnabled, "master switch hides preview translation");
            Assert.Equal(Visibility.Visible, Sta.Find<TextBlock>(w, "ViewTranslationHint").Visibility);
            w.Close();
        });
    }

    [Fact]
    public void F09_desktop_does_not_invent_a_translation_row_for_chinese_lyrics()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            var w = new DesktopLyricsWindow(np, env.Settings, () => { }) { ShowActivated = false };
            w.Show();
            env.Sessions.Play("demo.player", "示例曲目", "虚构乐队", positionMs: 1_200);
            var current = Sta.Find<LyricLineView>(w, "CurrentLine");
            await Sta.Until(() => current.Line?.Text == "示例 A 的第一行", "current line");
            Assert.True(string.IsNullOrEmpty(current.Line?.Translation));
            w.Close();
        });
    }

    [Fact]
    public void F09_desktop_shows_real_translation_on_english_lines()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "Hello Track", "Nobody Real", Samples.LrcTranslated);
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            var w = new DesktopLyricsWindow(np, env.Settings, () => { }) { ShowActivated = false };
            w.Show();
            env.Sessions.Play("demo.player", "Hello Track", "Nobody Real", positionMs: 1_200);
            var current = Sta.Find<LyricLineView>(w, "CurrentLine");
            await Sta.Until(() => current.Line?.Text == "Hello there friends", "english line");
            Assert.Equal("你好朋友们", current.Line?.Translation);
            w.Close();
        });
    }

    [Fact]
    public void F09_island_clears_previous_lyrics_when_the_next_song_has_none()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "第一首", "虚构乐队", Samples.LrcA);
        Sta.Run(async () =>
        {
            await env.Settings.UpdateAsync(s =>
            {
                s.Island.AlwaysTwoLines = true;
                s.Island.ContinentMode = true;
                s.Island.AutoHideWhenIdle = false;
            });
            var np = await env.StartAsync();
            var w = new IslandWindow(np, env.Settings) { ShowActivated = false };
            w.Show();
            env.Sessions.Play("demo.player", "第一首", "虚构乐队", positionMs: 1_200);
            var line = Sta.Find<LyricLineView>(w, "Line");
            var next = Sta.Find<LyricLineView>(w, "NextLine");
            await Sta.Until(() => line.Line?.Text == "示例 A 的第一行", "first song");
            env.Sessions.Play("demo.player", "没有歌词的曲目", "测试艺人", positionMs: 1_200);
            await Sta.Until(() => line.Line is null && next.Line is null, "previous lyrics gone");
            w.Close();
        });
    }

    [Fact]
    public void F09_island_english_shows_only_current_line_and_translation()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "Hello Track", "Nobody Real", Samples.LrcTranslated);
        Sta.Run(async () =>
        {
            await env.Settings.UpdateAsync(s =>
            {
                s.ShowChineseTranslation = true;
                s.Island.ShowTranslation = true;
                s.Island.AlwaysTwoLines = true;
                s.Island.ContinentMode = true;
                s.Island.AutoHideWhenIdle = false;
            });
            var np = await env.StartAsync();
            var w = new IslandWindow(np, env.Settings) { ShowActivated = false };
            w.Show();
            env.Sessions.Play("demo.player", "Hello Track", "Nobody Real", positionMs: 1_200);
            var line = Sta.Find<LyricLineView>(w, "Line");
            var next = Sta.Find<LyricLineView>(w, "NextLine");
            await Sta.Until(() => line.Line?.Text == "Hello there friends", "english line");
            Assert.Equal("你好朋友们", line.Line?.Translation);
            Assert.Equal(Visibility.Collapsed, next.Visibility);
            Assert.Null(next.Line);
            w.Close();
        });
    }

    [Fact]
    public void F09_island_cover_stays_compact_when_the_header_is_open()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        Sta.Run(async () =>
        {
            await env.Settings.UpdateAsync(s =>
            {
                s.Island.ContinentMode = true;
                s.Island.AutoHideWhenIdle = false;
            });
            var np = await env.StartAsync();
            var w = new IslandWindow(np, env.Settings) { ShowActivated = false };
            w.Show();
            env.Sessions.Play("demo.player", "示例曲目", "虚构乐队", positionMs: 1_200);
            var host = Sta.Find<FrameworkElement>(w, "CoverHost");
            await Sta.Until(() => host.Visibility == Visibility.Visible, "cover");
            Assert.True(host.ActualWidth <= 40, "cover stays a small tile, not the full island height");
            Assert.Null(w.FindName("Header"));
            w.Close();
        });
    }

    [Fact]
    public void F09_island_width_grows_with_a_longer_lyric_line()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", """
            [00:01.00]短
            [00:05.00]this made-up lyric line is intentionally much longer than the short one above
            """);
        Sta.Run(async () =>
        {
            await env.Settings.UpdateAsync(s =>
            {
                s.Island.ContinentMode = false;
                s.Island.AlwaysTwoLines = false;
                s.Island.AutoHideWhenIdle = false;
            });
            var np = await env.StartAsync();
            var w = new IslandWindow(np, env.Settings) { ShowActivated = false };
            w.Show();
            env.Sessions.Play("demo.player", "示例曲目", "虚构乐队", positionMs: 1_200);
            var line = Sta.Find<LyricLineView>(w, "Line");
            var pill = Sta.Find<FrameworkElement>(w, "Pill");
            await Sta.Until(() => line.Line?.Text == "短", "short line");
            await Task.Delay(80);
            double shortWidth = pill.ActualWidth;
            env.Sessions.Play("demo.player", "示例曲目", "虚构乐队", positionMs: 5_500);
            await Sta.Until(() => line.Line?.Text?.Contains("intentionally") == true, "long line");
            await Sta.Until(() => pill.ActualWidth > shortWidth + 40, "pill grew with the lyric");
            w.Close();
        });
    }

    [Fact]
    public void F09_island_keeps_a_cover_placeholder_when_the_player_gives_none()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            var w = new IslandWindow(np, env.Settings) { ShowActivated = false };
            w.Show();
            env.Sessions.Play("demo.player", "示例曲目", "虚构乐队", positionMs: 1_200);
            var host = Sta.Find<FrameworkElement>(w, "CoverHost");
            var placeholder = Sta.Find<FrameworkElement>(w, "CoverPlaceholder");
            await Sta.Until(() => host.Visibility == Visibility.Visible && placeholder.Visibility == Visibility.Visible, "cover placeholder");
            Assert.Null(Sta.Find<System.Windows.Controls.Image>(w, "CoverImage").Source);
            w.Close();
        });
    }
}
