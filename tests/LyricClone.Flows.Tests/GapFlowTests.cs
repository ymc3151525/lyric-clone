using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LyricClone.App.Lyrics;
using LyricClone.App.Services;
using LyricClone.App.Views;
using LyricClone.Core.Library;
using LyricClone.Core.Playback;
using LyricClone.Core.Settings;
using LyricClone.Core.Views;
using LyricClone.Infrastructure.Smtc;
using LyricClone.Tests.Shared;
using Microsoft.Win32;
using static LyricClone.Flows.Tests.UiDriver;

namespace LyricClone.Flows.Tests;

/// <summary>The five gaps from parity.md: S16 hint, per-view colours, embedded lyrics, fullscreen view, start with Windows.</summary>
public sealed class GapFlowTests
{
    private static Color ColorOf(Brush b) => Assert.IsType<SolidColorBrush>(b).Color;

    // F01 / S16

    [Fact]
    public void F01_E6_no_player_hint_shows_once_and_dont_show_again_is_saved()
    {
        using var env = new FlowEnv();
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            var hints = new PlayerHintService(np, env.Settings, () => { });

            var dialog = hints.ShowIfNeeded();
            Assert.NotNull(dialog);
            Assert.Equal(PlayerHintKind.NoPlayer, dialog.Kind);
            Assert.Null(hints.ShowIfNeeded());

            var box = Descendants<CheckBox>(dialog).Single(c => c.Content as string == "不再提示");
            Assert.Equal(Visibility.Visible, box.Visibility);
            box.IsChecked = true;
            Press(ButtonWithText(dialog, "知道了"));
            await Sta.Until(() => !env.Settings.Current.ShowNoPlayerHint, "don't show again saved");

            Assert.Null(new PlayerHintService(np, env.Settings, () => { }).ShowIfNeeded());
        });
    }

    [Fact]
    public void F01_E7_all_sessions_ignored_hint_names_them_and_opens_per_app_settings()
    {
        using var env = new FlowEnv();
        Sta.Run(async () =>
        {
            env.Sessions.AddIgnoredSession("Chrome");
            var np = await env.StartAsync();
            bool opened = false;
            var dialog = new PlayerHintService(np, env.Settings, () => opened = true).ShowIfNeeded();

            Assert.NotNull(dialog);
            Assert.Equal(PlayerHintKind.AllIgnored, dialog.Kind);
            Assert.Contains(Descendants<TextBlock>(dialog), t => t.Text.Contains("Chrome"));
            Press(ButtonWithText(dialog, "打开按应用设置"));
            await Sta.Until(() => opened, "per-app settings opened");
        });
    }

    [Fact]
    public void F01_E8_smtc_unavailable_is_shown_even_when_hints_are_off_and_playing_shows_nothing()
    {
        using var env = new FlowEnv();
        Sta.Run(async () =>
        {
            await env.Settings.UpdateAsync(s => s.ShowNoPlayerHint = false);
            env.Sessions.Availability = SmtcAvailability.Unavailable;
            var np = await env.StartAsync();
            var dialog = new PlayerHintService(np, env.Settings, () => { }).ShowIfNeeded();
            Assert.NotNull(dialog);
            Assert.Equal(PlayerHintKind.SmtcUnavailable, dialog.Kind);
            Assert.Equal(Visibility.Collapsed, Descendants<CheckBox>(dialog).Single().Visibility);
            dialog.Close();
        });

        using var playing = new FlowEnv();
        Sta.Run(async () =>
        {
            var np = await playing.StartAsync();
            playing.Sessions.Play("demo.player", "示例曲目", "虚构乐队");
            await Sta.Until(() => np.Snapshot is not null, "snapshot");
            Assert.Null(new PlayerHintService(np, playing.Settings, () => { }).ShowIfNeeded());
        });
    }

    // F09 / S07 colours

    [Fact]
    public void F09_E4_colours_from_settings_apply_live_to_desktop_and_island_and_default_restores_theme()
    {
        using var env = new FlowEnv();
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            var desktop = new DesktopLyricsWindow(np, env.Settings, () => { }) { ShowActivated = false };
            var island = new IslandWindow(np, env.Settings) { ShowActivated = false };
            desktop.Show();
            island.Show();
            var current = Sta.Find<LyricLineView>(desktop, "CurrentLine");
            var next = Sta.Find<LyricLineView>(desktop, "NextLine");
            var pill = Sta.Find<LyricLineView>(island, "Line");
            var themeText = ColorOf(current.TextBrush);

            var w = new SettingsWindow(env.Settings, env.Sessions, () => { }, "test-agent") { ShowActivated = false, Left = -10_000 };
            w.Show();
            w.ShowPage("Views");
            var desktopFields = Descendants<ColorField>(Sta.Find<StackPanel>(w, "DesktopColorsPanel")).ToList();
            var islandFields = Descendants<ColorField>(Sta.Find<StackPanel>(w, "IslandColorsPanel")).ToList();
            Assert.Equal(4, desktopFields.Count);
            Assert.Equal(3, islandFields.Count);

            var text = desktopFields[0];
            Press(Descendants<Button>(text).Single(b => b.Tag as string == "#7FDBFF"));
            await Sta.Until(() => env.Settings.Current.Desktop.Colors.Text == "#7FDBFF", "swatch saved");
            await Sta.Until(() => ColorOf(current.TextBrush) == Color.FromRgb(0x7F, 0xDB, 0xFF), "desktop text colour live");
            Assert.Equal(0x7F, ColorOf(next.DimBrush).R);

            var outline = desktopFields[3];
            var hex = Descendants<TextBox>(outline).Single();
            hex.Text = "not a colour";
            hex.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(w)!, 0, System.Windows.Input.Key.Enter) { RoutedEvent = UIElement.KeyDownEvent });
            Assert.Null(env.Settings.Current.Desktop.Colors.Outline);
            Assert.Equal("", hex.Text);
            hex.Text = "#f0a";
            hex.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(w)!, 0, System.Windows.Input.Key.Enter) { RoutedEvent = UIElement.KeyDownEvent });
            await Sta.Until(() => env.Settings.Current.Desktop.Colors.Outline == "#FF00AA", "typed hex normalised and saved");
            await Sta.Until(() => ColorOf(current.StrokeBrush) == Color.FromRgb(0xFF, 0x00, 0xAA), "outline colour live");

            Press(Descendants<Button>(islandFields[1]).Single(b => b.Tag as string == "#FFD54F"));
            await Sta.Until(() => ColorOf(pill.SungBrush) == Color.FromRgb(0xFF, 0xD5, 0x4F), "island sung colour live");
            Assert.Null(env.Settings.Current.Desktop.Colors.Sung);

            Press(ButtonWithText(text, "默认"));
            await Sta.Until(() => env.Settings.Current.Desktop.Colors.Text is null, "default saved");
            await Sta.Until(() => ColorOf(current.TextBrush) == themeText, "theme colour back");

            w.Close();
            desktop.Close();
            island.Close();
        });
    }

    // F04 / embedded tags

    [Fact]
    public void F04_H2_embedded_lyrics_in_music_folder_are_used_before_lrclib()
    {
        using var env = new FlowEnv();
        File.WriteAllBytes(Path.Combine(env.MusicDir, "01.mp3"),
            new TestAudio.Id3().Text("TIT2", "示例曲目").Text("TPE1", "虚构乐队").Uslt(Samples.LrcB, encoding: 3).Build());
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            env.Sessions.Play("demo.player", "示例曲目", "虚构乐队");
            await Sta.Until(() => np.Resolved.Version is not null, "resolved");

            Assert.Equal(LyricsSources.Embedded, np.Resolved.Version!.Source);
            Assert.Equal("Made-up B line one", np.Lyrics.Lines[0].Text);
            Assert.Empty(env.Lrclib.Calls);
        });
    }

    [Fact]
    public void F04_E2_audio_file_added_later_wins_over_cached_lrclib_on_next_play()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            env.Sessions.Play("demo.player", "示例曲目", "虚构乐队");
            await Sta.Until(() => np.Resolved.Version?.Source == LyricsSources.Lrclib, "lrclib first");

            File.WriteAllBytes(Path.Combine(env.MusicDir, "虚构乐队 - 示例曲目.flac"),
                TestAudio.Flac([("TITLE", "示例曲目"), ("ARTIST", "虚构乐队"), ("LYRICS", Samples.LrcMine)]));
            env.Embedded.Invalidate();
            env.Sessions.Stop();
            await Sta.Until(() => np.Resolved.Track is null, "stopped");
            env.Sessions.Play("demo.player", "示例曲目", "虚构乐队");

            await Sta.Until(() => np.Resolved.Version?.Source == LyricsSources.Embedded, "embedded wins on replay");
            Assert.Equal("我自己写的 第一行", np.Lyrics.Lines[0].Text);
        });
    }

    // F09 / S05 fullscreen

    private static byte[] Png(Color color)
    {
        var bmp = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, 64, 64));
        }

        bmp.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static FullscreenLyricsWindow OpenFullscreen(FlowEnv env)
    {
        var w = new FullscreenLyricsWindow(env.NowPlaying!, env.Settings)
        {
            WindowState = WindowState.Normal,
            Width = 1280,
            Height = 800,
            Left = -10_000,
            ShowActivated = false,
        };
        w.Show();
        return w;
    }

    [Fact]
    public void F09_H3_fullscreen_lists_all_lines_highlights_current_and_blurs_the_cover()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        env.Sessions.Thumbnail = Png(Colors.OrangeRed);
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            env.Sessions.Play("demo.player", "示例曲目", "虚构乐队", positionMs: 5_500);
            await Sta.Until(() => np.Lyrics.Lines.Count == 3, "resolved");

            var w = OpenFullscreen(env);
            await Sta.Until(() => w.LineViews.Count == 3, "all lines listed");
            await Sta.Until(() => w.ActiveLineIndex == 1, "second line active");
            Assert.True(w.LineViews[1].IsActive);
            Assert.False(w.LineViews[0].IsActive);

            var cover = Sta.Find<Image>(w, "Cover");
            var backdrop = Sta.Find<Image>(w, "Backdrop");
            await Sta.Until(() => cover.Source is not null, "cover loaded");
            Assert.Equal(Visibility.Visible, backdrop.Visibility);
            Assert.Equal("示例曲目", Sta.Find<TextBlock>(w, "TitleText").Text);

            await env.Settings.UpdateAsync(s => s.Fullscreen.BlurredCover = false);
            await Sta.Until(() => backdrop.Visibility == Visibility.Collapsed, "blur off");

            Press(Sta.Find<Button>(w, "PlayPauseButton"));
            await Sta.Until(() => env.Sessions.PlayPauseCalls == 1, "play / pause forwarded");

            bool closed = false;
            w.Closed += (_, _) => closed = true;
            Press(Sta.Find<Button>(w, "CloseButton"));
            await Sta.Until(() => closed, "closed");
        });
    }

    [Fact]
    public void F09_E5_fullscreen_without_lyrics_or_cover_shows_message_and_placeholder()
    {
        using var env = new FlowEnv();
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            env.Sessions.Play("demo.player", "没有歌词的曲目", "虚构乐队");
            await Sta.Until(() => np.Resolved.State == Infrastructure.Lyrics.ResolveState.NotFound, "not found");

            var w = OpenFullscreen(env);
            var empty = Sta.Find<TextBlock>(w, "EmptyText");
            await Sta.Until(() => empty.Visibility == Visibility.Visible && empty.Text == "这首歌暂时没有歌词", "empty message");
            Assert.Empty(w.LineViews);
            Assert.Equal(Visibility.Visible, Sta.Find<FrameworkElement>(w, "CoverPlaceholder").Visibility);
            Assert.Equal(Visibility.Collapsed, Sta.Find<Image>(w, "Backdrop").Visibility);
            w.Close();
        });
    }

    // Shell / autostart

    [Fact]
    public void Autostart_toggle_writes_run_key_and_repairs_a_moved_exe()
    {
        var keyPath = @"Software\lyric-clone-tests\Run-" + Guid.NewGuid().ToString("N");
        try
        {
            var a = new Autostart(keyPath, @"C:\made-up\old\LyricClone.exe");
            Assert.False(a.IsEnabled);
            a.SetEnabled(true);
            Assert.True(a.IsEnabled);

            var moved = new Autostart(keyPath, @"C:\made-up\new\LyricClone.exe");
            Assert.True(moved.RepairIfMoved());
            Assert.False(moved.RepairIfMoved());
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
            {
                Assert.Equal("\"C:\\made-up\\new\\LyricClone.exe\" --autostart", key!.GetValue("lyric-clone"));
            }

            using var env = new FlowEnv();
            Sta.Run(async () =>
            {
                var w = new SettingsWindow(env.Settings, env.Sessions, () => { }, "test-agent", moved) { ShowActivated = false, Left = -10_000 };
                w.Show();
                var box = Sta.Find<CheckBox>(w, "AutostartBox");
                Assert.True(box.IsChecked);
                box.IsChecked = false;
                box.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, box));
                await Sta.Until(() => !moved.IsEnabled, "run entry removed");
                w.Close();
            });
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\lyric-clone-tests", throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void F09_H5_taskbar_lyrics_sit_on_a_bottom_bar_and_hide_when_fullscreen()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        var bar = new FakeTaskbar();
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            env.Sessions.Play("demo.player", "示例曲目", "虚构乐队", positionMs: 1_200);
            var w = new TaskbarLyricsWindow(np, env.Settings, bar) { ShowActivated = false };
            w.Show();
            await Sta.Until(() => w.LastPlacement.Visible && Math.Abs(w.Left - 72) < 0.5, "placed on the bar");
            var line = Sta.Find<LyricLineView>(w, "Line");
            await Sta.Until(() => line.Line?.Text == "示例 A 的第一行", "current line");
            Assert.True(string.IsNullOrEmpty(line.Line?.Translation));
            Assert.Equal(Visibility.Visible, Sta.Find<FrameworkElement>(w, "CoverPlaceholder").Visibility);
            Assert.Null(Sta.Find<Image>(w, "Cover").Source);

            bar.Current = bar.Current with { FullscreenApp = true };
            await Sta.Until(() => !w.IsVisible || !w.LastPlacement.Visible, "hidden over fullscreen");
            w.Close();
        });
    }

    [Fact]
    public void F09_E6_vertical_taskbar_is_marked_unsupported()
    {
        using var env = new FlowEnv();
        var bar = new FakeTaskbar { Current = new(TaskbarEdge.Left, 0, 0, 48, 1080, false, false, false) };
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            var w = new TaskbarLyricsWindow(np, env.Settings, bar) { ShowActivated = false };
            w.Show();
            await Sta.Until(() => w.IsUnsupported, "vertical unsupported");
            Assert.False(w.LastPlacement.Visible);
            w.Close();
        });
    }

    [Fact]
    public void F07_H4_language_and_auto_update_and_hotkey_are_saved()
    {
        using var env = new FlowEnv();
        Sta.Run(async () =>
        {
            var w = new SettingsWindow(env.Settings, env.Sessions, () => { }, "test-agent") { ShowActivated = false, Left = -10_000 };
            w.Show();
            var lang = Sta.Find<ComboBox>(w, "LanguageBox");
            lang.SelectedIndex = (int)UiLanguage.English;
            await Sta.Until(() => env.Settings.Current.Language == UiLanguage.English, "language saved");
            Assert.True(Loc.English);

            w.ShowPage("Lyrics");
            var auto = Sta.Find<CheckBox>(w, "AutoUpdateBox");
            auto.IsChecked = true;
            auto.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, auto));
            await Sta.Until(() => env.Settings.Current.AutoUpdateSavedOnline, "auto-update saved");

            w.ShowPage("Hotkeys");
            await env.Settings.UpdateAsync(s => s.Hotkeys.ToggleLock = "Ctrl+Alt+L");
            await Sta.Until(() => Sta.Find<TextBox>(w, "HotkeyLockBox").Text == "Ctrl+Alt+L", "hotkey shown");

            w.ShowPage("Support");
            Assert.Equal(Visibility.Visible, Sta.Find<StackPanel>(w, "SupportPage").Visibility);
            w.Close();
            Loc.Apply(UiLanguage.Chinese);
        });
    }

    [Fact]
    public void F09_H6_stage_centres_the_current_line_and_closes_on_escape()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            env.Sessions.Play("demo.player", "示例曲目", "虚构乐队", positionMs: 1_200);
            await Sta.Until(() => np.Lyrics is { IsEmpty: false }, "lyrics");

            var w = new StageWindow(np, env.Settings)
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 800,
                Left = -10_000,
                ShowActivated = false,
            };
            w.Show();
            await Sta.Until(() => w.Current.Line is not null, "current line");
            Assert.Equal("示例 A 的第一行", w.Current.Line!.Text);

            await env.Settings.UpdateAsync(s => s.Stage.FontFamily = "Consolas");
            await Sta.Until(() => w.Current.FontFamily.Source.Contains("Consolas"), "stage font");

            bool closed = false;
            w.Closed += (_, _) => closed = true;
            Press(Sta.Find<Button>(w, "CloseButton"));
            await Sta.Until(() => closed, "closed");
        });
    }

    [Fact]
    public void About_check_update_without_a_feed_explains_itself()
    {
        using var env = new FlowEnv();
        Sta.Run(async () =>
        {
            var w = new SettingsWindow(env.Settings, env.Sessions, () => { }, "test-agent") { ShowActivated = false, Left = -10_000 };
            w.Show();
            w.ShowPage("About");
            Press(Sta.Find<Button>(w, "CheckUpdateButton"));
            var status = Sta.Find<TextBlock>(w, "UpdateStatusText");
            await Sta.Until(() => status.Text.Contains("没有发布更新源"), "no-feed message");
            w.Close();
        });
    }
}
