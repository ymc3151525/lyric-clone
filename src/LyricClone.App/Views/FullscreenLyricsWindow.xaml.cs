using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LyricClone.App.Lyrics;
using LyricClone.App.Services;
using LyricClone.Core.Lyrics;
using LyricClone.Core.Playback;
using LyricClone.Core.Settings;
using LyricClone.Infrastructure.Settings;

namespace LyricClone.App.Views;

/// <summary>S05: every line of the song, the current one highlighted and kept at a fixed height, over the blurred cover.</summary>
public partial class FullscreenLyricsWindow : Window
{
    private const double AnchorRatio = 0.38;
    private const double LineSize = 34;
    private static readonly TimeSpan CoverRetry = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan HideChromeAfter = TimeSpan.FromSeconds(3);

    private readonly NowPlayingService _nowPlaying;
    private readonly ISettingsStore _settings;
    private readonly DispatcherTimer _idle;
    private readonly List<LyricLineView> _views = [];
    private ParsedLyrics? _lyrics;
    private string? _trackKey;
    private int _active = int.MinValue;
    private double _manualOffset;

    public FullscreenLyricsWindow(NowPlayingService nowPlaying, ISettingsStore settings)
    {
        InitializeComponent();
        _nowPlaying = nowPlaying;
        _settings = settings;

        _idle = new DispatcherTimer { Interval = HideChromeAfter };
        _idle.Tick += (_, _) => SetChrome(false);

        _nowPlaying.PropertyChanged += OnNowPlayingChanged;
        _settings.Changed += OnSettingsChanged;
        CompositionTarget.Rendering += OnFrame;
        Viewport.SizeChanged += (_, _) => ScrollToActive(animate: false);
        PreviewKeyDown += OnKey;
        MouseMove += (_, _) => SetChrome(true);
        Closed += (_, _) =>
        {
            _idle.Stop();
            CompositionTarget.Rendering -= OnFrame;
            _nowPlaying.PropertyChanged -= OnNowPlayingChanged;
            _settings.Changed -= OnSettingsChanged;
        };

        Rebuild();
        _ = RefreshTrackAsync();
        SetChrome(true);
    }

    /// <summary>Index of the highlighted line, or -1 before the first line / for unsynced lyrics.</summary>
    public int ActiveLineIndex => Math.Max(-1, _active);

    public IReadOnlyList<LyricLineView> LineViews => _views;

    private FullscreenSettings Options => _settings.Current.Fullscreen;

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
        Rebuild();
        ApplyBackdrop();
    });

    private void Rebuild()
    {
        _lyrics = _nowPlaying.Lyrics;
        _views.Clear();
        LinesPanel.Children.Clear();
        _active = int.MinValue;
        _manualOffset = 0;

        var o = Options;
        var family = string.IsNullOrWhiteSpace(o.FontFamily) ? (FontFamily)FindResource("Font.Sans") : new FontFamily(o.FontFamily);
        var lyrics = _lyrics;
        if (lyrics is null || lyrics.IsEmpty)
        {
            EmptyText.Text = _nowPlaying.SurfaceStatus;
            EmptyText.Visibility = Visibility.Visible;
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;
        foreach (var line in lyrics.Lines)
        {
            var view = new LyricLineView
            {
                Line = line,
                FontSize = LineSize,
                FontFamily = family,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Left,
                ShowTranslation = _settings.Current.ShowsTranslation(o.ShowTranslation),
                TranslationRatio = 0.6,
                IsActive = false,
                Margin = new Thickness(0, 0, 0, 26),
            };
            LyricColors.Apply(view, o.Colors);
            if (!lyrics.IsSynced)
            {
                view.DimBrush = view.TextBrush;
            }

            _views.Add(view);
            LinesPanel.Children.Add(view);
        }

        Dispatcher.InvokeAsync(() => ScrollToActive(animate: false), DispatcherPriority.Loaded);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var lyrics = _lyrics;
        if (!IsVisible || lyrics is null || lyrics.IsEmpty || _views.Count == 0)
        {
            return;
        }

        long pos = _nowPlaying.Clock.PositionMs;
        int index = lyrics.FindLineIndex(pos, _nowPlaying.Snapshot?.DurationMs);
        if (index != _active)
        {
            if (_active >= 0 && _active < _views.Count)
            {
                _views[_active].IsActive = false;
                _views[_active].Line = lyrics.Lines[_active];
            }

            _active = index;
            if (index >= 0)
            {
                _views[index].IsActive = true;
                _views[index].Line = lyrics.Lines[index];
            }

            _manualOffset = 0;
            ScrollToActive(animate: true);
        }

        if (index >= 0)
        {
            _views[index].PositionMs = pos;
        }
    }

    private void ScrollToActive(bool animate)
    {
        if (_views.Count == 0)
        {
            Scroll.BeginAnimation(TranslateTransform.YProperty, null);
            Scroll.Y = 0;
            return;
        }

        bool synced = _lyrics?.IsSynced == true;
        var target = _views[Math.Clamp(_active < 0 ? 0 : _active, 0, _views.Count - 1)];
        double lineTop = target.TranslatePoint(new Point(0, 0), LinesPanel).Y;
        double anchor = synced ? Viewport.ActualHeight * AnchorRatio : Viewport.ActualHeight * 0.14;
        double y = anchor - lineTop + _manualOffset;

        if (!animate)
        {
            Scroll.BeginAnimation(TranslateTransform.YProperty, null);
            Scroll.Y = y;
            return;
        }

        Scroll.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        _manualOffset += e.Delta * 0.6;
        ScrollToActive(animate: true);
    }

    private async Task RefreshTrackAsync()
    {
        var snap = _nowPlaying.Snapshot;
        var key = snap is { HasTrack: true } ? $"{snap.AppId}|{snap.Title}|{snap.Artist}|{snap.Album}" : null;
        TitleText.Text = snap?.Title ?? "";
        ArtistText.Text = snap?.Artist ?? "";
        if (key == _trackKey)
        {
            return;
        }

        _trackKey = key;
        SetCover(null);
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
            SetCover(image);
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
            bmp.DecodePixelWidth = 512;
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

    private void SetCover(BitmapSource? image)
    {
        Cover.Source = image;
        CoverPlaceholder.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
        ApplyBackdrop();
    }

    private void ApplyBackdrop()
    {
        bool show = Cover.Source is not null && Options.BlurredCover;
        Backdrop.Source = show ? Cover.Source : null;
        Backdrop.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
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

    private async void OnPrevious(object sender, RoutedEventArgs e) => await _nowPlaying.PreviousAsync();

    private async void OnPlayPause(object sender, RoutedEventArgs e) => await _nowPlaying.PlayPauseAsync();

    private async void OnNext(object sender, RoutedEventArgs e) => await _nowPlaying.NextAsync();

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
