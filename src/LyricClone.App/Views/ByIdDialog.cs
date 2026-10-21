using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using LyricClone.App.Services;
using LyricClone.App.ViewModels;
using LyricClone.Core.Library;
using LyricClone.Infrastructure.Lrclib;

namespace LyricClone.App.Views;

/// <summary>S12: fetch an LRCLIB record by numeric ID or lrclib.net link.</summary>
public sealed class ByIdDialog : Window
{
    private const long DurationToleranceMs = 2000;

    private readonly ManagerContext _ctx;
    private readonly Track _track;
    private readonly TextBox _input = new() { MinWidth = 420 };
    private readonly TextBlock _status;
    private readonly Button _ok;
    private LyricsCandidate? _fetched;

    public ByIdDialog(ManagerContext ctx, Track track)
    {
        _ctx = ctx;
        _track = track;
        Title = "通过 ID 或链接指定版本";
        Style = (Style)FindResource("AppWindow");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _status = new TextBlock { Style = (Style)FindResource("Text.Muted"), Margin = new Thickness(0, 8, 0, 0), MaxWidth = 420, TextWrapping = TextWrapping.Wrap };
        _ok = new Button { Content = "获取", IsDefault = true, MinWidth = 88, Style = (Style)FindResource("Button.Primary") };
        _ok.Click += OnOk;
        _input.TextChanged += (_, _) =>
        {
            _fetched = null;
            _ok.Content = "获取";
            _status.Text = "";
        };

        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 88, Margin = new Thickness(0, 0, 8, 0) };
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock { Text = Title, Style = (Style)FindResource("Text.Subtitle"), Margin = new Thickness(0, 0, 0, 12) },
                new TextBlock { Text = "LRCLIB 记录 ID，或 lrclib.net 上的链接", Style = (Style)FindResource("Text.Body"), Margin = new Thickness(0, 0, 0, 8) },
                _input,
                _status,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 24, 0, 0),
                    Children = { cancel, _ok },
                },
            },
        };
        Loaded += (_, _) => _input.Focus();
    }

    public LyricsCandidate? Result { get; private set; }

    private async void OnOk(object sender, RoutedEventArgs e)
    {
        if (_fetched is not null)
        {
            Result = _fetched;
            DialogResult = true;
            return;
        }

        if (!LrclibClient.TryParseId(_input.Text, out var id))
        {
            _status.Text = "无法识别。请输入纯数字 ID，或形如 lrclib.net/…/123 的链接。";
            return;
        }

        _ok.IsEnabled = false;
        _status.Text = "正在获取…";
        try
        {
            var c = await _ctx.Lrclib.GetByIdAsync(id, CancellationToken.None);
            if (c is null)
            {
                _status.Text = $"LRCLIB 上没有 ID 为 {id} 的记录。";
                return;
            }

            _fetched = c;
            var kind = c.IsInstrumental ? "纯音乐" : c.HasWordSync ? "逐字" : c.HasSyncedContent ? "逐行" : "无时间轴";
            var text = $"找到：{c.Title} — {c.Artist}  {ManagerViewModel.FormatDuration(c.DurationMs)}  ·  {kind}";
            if (_track.DurationMs is { } mine && c.DurationMs is { } theirs && Math.Abs(mine - theirs) > DurationToleranceMs)
            {
                text += $"\n注意：和当前曲目的时长相差 {Math.Abs(mine - theirs) / 1000.0:0.#} 秒。";
            }

            _status.Text = text;
            _ok.Content = "保存此版本";
        }
        catch (Exception ex) when (ex is LrclibException or HttpRequestException or TaskCanceledException)
        {
            _status.Text = $"获取失败：{ex.Message}";
        }
        finally
        {
            _ok.IsEnabled = true;
        }
    }
}
