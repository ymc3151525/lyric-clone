using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LyricClone.App.Lyrics;
using LyricClone.App.Services;
using LyricClone.Core.Lyrics;
using LyricClone.Core.Settings;
using LyricClone.Infrastructure.Settings;

namespace LyricClone.App.Views;

/// <summary>S06: one centred column of oversized lyrics over a slow, cover-sampled gradient.</summary>
public partial class StageWindow : Window
{
    private static readonly TimeSpan CoverRetry = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan HideChromeAfter = TimeSpan.FromSeconds(3);

    private readonly NowPlayingService _nowPlaying;
    private readonly ISettingsStore _settings;
    private readonly DispatcherTimer _idle;
    private ParsedLyrics? _lyrics;
    private string? _trackKey;
    private int _active = int.MinValue;

    public StageWindow(NowPlayingService nowPlaying, ISettingsStore settings)
    {
        InitializeComponent();
        _nowPlaying = nowPlaying;
        _settings = settings;
        Title = Loc.T("歌词舞台 — lyric-clone", "Lyrics stage — lyric-clone");
        CloseButton.ToolTip = Loc.T("退出舞台（Esc 或双击）", "Leave the stage (Esc or double-click)");
        AutomationProperties.SetName(this, Loc.T("歌词舞台", "Lyrics stage"));

        CurrentLine.FontSize = (double)FindResource("Lyric.Stage.Size");
        _idle = new DispatcherTimer { Interval = HideChromeAfter };
        _idle.Tick += (_, _) => SetChrome(false);

        _nowPlaying.PropertyChanged += OnNowPlayingChanged;
        _settings.Changed += OnSettingsChanged;
        CompositionTarget.Rendering += OnFrame;
        PreviewKeyDown += OnKey;
        MouseMove += (_, _) => SetChrome(true);
        MouseDoubleClick += (_, _) => Close();
        Closed += (_, _) =>
        {
            StopWash();
            _idle.Stop();
            CompositionTarget.Rendering -= OnFrame;
            _nowPlaying.PropertyChanged -= OnNowPlayingChanged;
            _settings.Changed -= OnSettingsChanged;
        };

        ApplySettings();
        Rebuild();
        _ = RefreshTrackAsync();
        SetChrome(true);
        StartWash();
    }

    public int ActiveLineIndex => Math.Max(-1, _active);

    public LyricLineView Current => CurrentLine;

    private StageSettings Options => _settings.Current.Stage;

    private void OnNowPlayingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NowPlayingService.Resolved) or nameof(NowPlayingService.StatusText) or nameof(NowPlayingService.SurfaceStatus))
        {
            Rebuild();
        }

        if (e.PropertyName is nameof(NowPlayingService.Snapshot))
        {
            _ = RefreshTrackAsync();
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings s) => Dispatcher.InvokeAsync(() =>
    {
        ApplySettings();
        Rebuild();
    });

    private void ApplySettings()
    {
        var o = Options;
        var family = string.IsNullOrWhiteSpace(o.FontFamily) ? (FontFamily)FindResource("Font.Sans") : new FontFamily(o.FontFamily);
        CurrentLine.FontFamily = family;
        PrevLine.FontFamily = family;
        NextLine.FontFamily = family;
        bool showTr = _settings.Current.ShowsTranslation(o.ShowTranslation);
        CurrentLine.ShowTranslation = showTr;
        PrevLine.ShowTranslation = showTr;
        NextLine.ShowTranslation = showTr;
        LyricColors.Apply(CurrentLine, o.Colors);
        LyricColors.Apply(PrevLine, o.Colors);
        LyricColors.Apply(NextLine, o.Colors);
        PrevLine.DimBrush = PrevLine.TextBrush;
        NextLine.DimBrush = NextLine.TextBrush;
        PrevLine.IsActive = false;
        NextLine.IsActive = false;
    }

    private void Rebuild()
    {
        _lyrics = _nowPlaying.Lyrics;
        _active = int.MinValue;
        var lyrics = _lyrics;
        if (lyrics is null || lyrics.IsEmpty)
        {
            EmptyText.Text = _nowPlaying.SurfaceStatus;
            EmptyText.Visibility = Visibility.Visible;
            PrevLine.Visibility = Visibility.Collapsed;
            CurrentLine.Visibility = Visibility.Collapsed;
            NextLine.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;
        PrevLine.Visibility = Visibility.Visible;
        CurrentLine.Visibility = Visibility.Visible;
        NextLine.Visibility = Visibility.Visible;
        ShowLine(-1);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var lyrics = _lyrics;
        if (!IsVisible || lyrics is null || lyrics.IsEmpty)
        {
            return;
        }

        int index = lyrics.FindLineIndex(_nowPlaying.Clock.PositionMs, _nowPlaying.Snapshot?.DurationMs);
        if (index != _active)
        {
            ShowLine(index);
        }

        if (_active >= 0)
        {
            CurrentLine.PositionMs = _nowPlaying.Clock.PositionMs;
        }
    }

    private void ShowLine(int index)
    {
        var lyrics = _lyrics;
        if (lyrics is null || lyrics.IsEmpty)
        {
            return;
        }

        _active = index;
        int current = Math.Clamp(index < 0 ? 0 : index, 0, lyrics.Lines.Count - 1);
        CurrentLine.Line = lyrics.Lines[current];
        CurrentLine.IsActive = lyrics.IsSynced && index >= 0;
        PrevLine.Line = current > 0 ? lyrics.Lines[current - 1] : null;
        PrevLine.Placeholder = "";
        NextLine.Line = current + 1 < lyrics.Lines.Count ? lyrics.Lines[current + 1] : null;
        NextLine.Placeholder = "";
        PrevLine.Visibility = PrevLine.Line is null ? Visibility.Hidden : Visibility.Visible;
        NextLine.Visibility = NextLine.Line is null ? Visibility.Hidden : Visibility.Visible;
    }

    private async Task RefreshTrackAsync()
    {
        var snap = _nowPlaying.Snapshot;
        var key = snap is { HasTrack: true } ? $"{snap.AppId}|{snap.Title}|{snap.Artist}|{snap.Album}" : null;
        if (key == _trackKey)
        {
            return;
        }

        _trackKey = key;
        ApplyBackdrop(null);
        if (key is null)
        {
            return;
        }

        var image = await LoadCoverAsync();
        if (image is null)
        {
            await Task.Delay(CoverRetry);
            if (_trackKey != key)
            {
                return;
            }

            image = await LoadCoverAsync();
        }

        if (_trackKey == key)
        {
            ApplyBackdrop(image);
        }
    }

    private async Task<BitmapSource?> LoadCoverAsync()
    {
        var bytes = await _nowPlaying.GetThumbnailAsync();
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 256;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or IOException)
        {
            return null;
        }
    }

    private void ApplyBackdrop(BitmapSource? image)
    {
        var (a, b) = CoverSampler.Sample(image);
        StopWash();
        StopA.Color = a;
        StopB.Color = b;
        StartWash();
    }

    private void StartWash()
    {
        var duration = TimeSpan.FromSeconds(14);
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        var animA = new ColorAnimation(StopA.Color, StopB.Color, duration) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
        var animB = new ColorAnimation(StopB.Color, StopA.Color, duration) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
        StopA.BeginAnimation(System.Windows.Media.GradientStop.ColorProperty, animA);
        StopB.BeginAnimation(System.Windows.Media.GradientStop.ColorProperty, animB);
    }

    private void StopWash()
    {
        StopA.BeginAnimation(System.Windows.Media.GradientStop.ColorProperty, null);
        StopB.BeginAnimation(System.Windows.Media.GradientStop.ColorProperty, null);
    }

    private void SetChrome(bool visible)
    {
        CloseButton.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        Cursor = visible ? null : Cursors.None;
        _idle.Stop();
        if (visible)
        {
            _idle.Start();
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
            case Key.F11:
                Close();
                e.Handled = true;
                break;
            case Key.Space:
                _ = _nowPlaying.PlayPauseAsync();
                e.Handled = true;
                break;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
