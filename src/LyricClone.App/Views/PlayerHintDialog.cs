using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LyricClone.Core.Playback;
using LyricClone.Core.Settings;

namespace LyricClone.App.Views;

/// <summary>S16: shown when SMTC is unavailable, no player is running, or every player is ignored. Not modal; the app lives in the tray.</summary>
public sealed class PlayerHintDialog : Window
{
    private readonly CheckBox _dontShow;

    public PlayerHintDialog(PlayerHintKind kind, IReadOnlyList<string> ignoredApps)
    {
        Kind = kind;
        Title = TitleFor(kind);
        Style = (Style)FindResource("AppWindow");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        _dontShow = new CheckBox
        {
            Content = Loc.T("不再提示", "Don't show again"),
            Margin = new Thickness(0, 16, 0, 0),
            Visibility = kind == PlayerHintKind.SmtcUnavailable ? Visibility.Collapsed : Visibility.Visible,
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        if (kind == PlayerHintKind.AllIgnored)
        {
            var open = new Button { Content = Loc.T("打开按应用设置", "Open per-app settings"), MinWidth = 88, Margin = new Thickness(0, 0, 8, 0) };
            open.Click += (_, _) =>
            {
                OpenAppsRequested = true;
                Close();
            };
            buttons.Children.Add(open);
        }

        var ok = new Button { Content = Loc.T("知道了", "OK"), MinWidth = 88, IsDefault = true, IsCancel = true, Style = (Style)FindResource("Button.Primary") };
        ok.Click += (_, _) => Close();
        buttons.Children.Add(ok);

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock { Text = Title, Style = (Style)FindResource("Text.Subtitle"), Margin = new Thickness(0, 0, 0, 12) },
                new TextBlock { Text = MessageFor(kind, ignoredApps), Style = (Style)FindResource("Text.Body"), MaxWidth = 440, TextWrapping = TextWrapping.Wrap },
                _dontShow,
                buttons,
            },
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    public PlayerHintKind Kind { get; }

    public bool DontShowAgain => _dontShow.IsChecked == true;

    public bool OpenAppsRequested { get; private set; }

    public static string TitleFor(PlayerHintKind kind) => kind switch
    {
        PlayerHintKind.SmtcUnavailable => Loc.T("无法读取播放信息", "Can't read playback"),
        PlayerHintKind.AllIgnored => Loc.T("检测到的播放器都被忽略了", "Every detected player is ignored"),
        _ => Loc.T("没有检测到播放器", "No player detected"),
    };

    public static string MessageFor(PlayerHintKind kind, IReadOnlyList<string> ignoredApps) => kind switch
    {
        PlayerHintKind.SmtcUnavailable =>
            Loc.T(
                "这台电脑上的 Windows 媒体控制（SMTC）不可用，lyric-clone 读不到正在播放的歌曲。它需要 Windows 10 1809 或更新的版本；精简过的系统可能移除了媒体相关组件。",
                "Windows media controls (SMTC) aren't available on this PC, so lyric-clone can't read what's playing. It needs Windows 10 1809 or later; stripped-down systems may have removed the media components."),
        PlayerHintKind.AllIgnored =>
            Loc.T(
                $"正在运行的 {string.Join("、", ignoredApps)} 在设置里被关闭了，或者是默认忽略的浏览器。可以在「设置 → 按应用」里打开。",
                $"{string.Join(", ", ignoredApps)} {(ignoredApps.Count == 1 ? "is" : "are")} turned off in settings, or treated as a browser. You can enable them under Settings → Per app."),
        _ =>
            Loc.T(
                "打开一个支持 Windows 媒体控制的播放器并开始播放，歌词会自动出现。大多数播放器都支持，例如 Spotify、Apple Music，以及装了 SMTC 插件的 foobar2000 和 MusicBee。",
                "Start playing in an app that shows up in the Windows media flyout. Spotify, Apple Music, and foobar2000 / MusicBee with an SMTC plugin all work."),
    };
}
