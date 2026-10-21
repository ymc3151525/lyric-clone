using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LyricClone.App.Interop;
using LyricClone.App.Lyrics;
using LyricClone.App.Services;
using LyricClone.Core.Lyrics;
using LyricClone.Core.Settings;
using LyricClone.Core.Views;
using LyricClone.Infrastructure.Settings;

namespace LyricClone.App.Views;

/// <summary>S04: a click-through strip sitting on the taskbar, left of the tray.</summary>
public partial class TaskbarLyricsWindow : Window
{
    private readonly NowPlayingService _nowPlaying;
    private readonly ISettingsStore _settings;
    private readonly ITaskbarQuery _taskbar;
    private readonly DispatcherTimer _place;
    private ParsedLyrics? _lyrics;
    private int _lineIndex = int.MinValue;
    private string? _coverKey;
    private bool _unsupported;

    public TaskbarLyricsWindow(NowPlayingService nowPlaying, ISettingsStore settings, ITaskbarQuery taskbar)
    {
        InitializeComponent();
        _nowPlaying = nowPlaying;
        _settings = settings;
        _taskbar = taskbar;

        SourceInitialized += (_, _) =>
        {
            NativeWindow.MakeToolWindow(this);
            NativeWindow.SetClickThrough(this, true);
        };
        _nowPlaying.PropertyChanged += OnNowPlayingChanged;
        _settings.Changed += OnSettingsChanged;
        CompositionTarget.Rendering += OnFrame;
        _place = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _place.Tick += (_, _) => Place();
        _place.Start();
        Closed += (_, _) =>
        {
            _place.Stop();
            CompositionTarget.Rendering -= OnFrame;
            _nowPlaying.PropertyChanged -= OnNowPlayingChanged;
            _settings.Changed -= OnSettingsChanged;
        };

        ApplySettings(settings.Current.Taskbar);
        Refresh();
        _ = RefreshCoverAsync();
        Place();
    }

    public bool IsUnsupported => _unsupported;

    public TaskbarPlacement LastPlacement { get; private set; }

    private void OnSettingsChanged(object? sender, AppSettings s) => Dispatcher.InvokeAsync(() =>
    {
        ApplySettings(s.Taskbar);
        Place();
    });

    private void ApplySettings(TaskbarLyricsSettings t)
    {
        Line.FontSize = t.FontSize;
        Line.ShowTranslation = _settings.Current.ShowsTranslation(t.ShowTranslation);
        Line.FontFamily = string.IsNullOrWhiteSpace(t.FontFamily) ? (FontFamily)FindResource("Font.Sans") : new FontFamily(t.FontFamily);
        LyricColors.Apply(Line, t.Colors);
        CoverFrame.Visibility = t.ShowCover ? Visibility.Visible : Visibility.Collapsed;
        Root.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x10, 0x11, 0x14));
    }

    private void OnNowPlayingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NowPlayingService.Resolved) or nameof(NowPlayingService.StatusText) or nameof(NowPlayingService.SurfaceStatus))
        {
            Refresh();
        }

        if (e.PropertyName is nameof(NowPlayingService.Snapshot))
        {
            _ = RefreshCoverAsync();
        }
    }

    private void Refresh()
    {
        _lyrics = _nowPlaying.Lyrics;
        _lineIndex = int.MinValue;
        UpdateLine(force: true);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (IsVisible)
        {
            UpdateLine(force: false);
        }
    }

    private void UpdateLine(bool force)
    {
        var lyrics = _lyrics;
        long pos = _nowPlaying.Clock.PositionMs;
        if (lyrics is null || lyrics.IsEmpty)
        {
            if (force || _lineIndex != -2)
            {
                _lineIndex = -2;
                Line.Line = null;
                Line.Placeholder = _nowPlaying.SurfaceStatus;
            }

            return;
        }

        int index = lyrics.FindLineIndex(pos, _nowPlaying.Snapshot?.DurationMs);
        if (force || index != _lineIndex)
        {
            _lineIndex = index;
            Line.Line = index >= 0 ? lyrics.Lines[index] : null;
            Line.Placeholder = index < 0 ? _nowPlaying.SurfaceStatus : "";
        }

        Line.PositionMs = pos;
    }

    private void Place()
    {
        var snap = _taskbar.Read();
        if (snap.QueryFailed)
        {
            Hide();
            return;
        }

        var placed = TaskbarLayout.Place(snap.Edge, snap.Left, snap.Top, snap.Width, snap.Height, snap.AutoHide, snap.FullscreenApp);
        LastPlacement = placed;
        _unsupported = placed.Unsupported;
        if (!placed.Visible)
        {
            Hide();
            return;
        }

        Left = placed.Left;
        Top = placed.Top;
        Width = placed.Width;
        Height = placed.Height;
        if (!IsVisible)
        {
            Show();
        }
    }

    private async Task RefreshCoverAsync()
    {
        var snap = _nowPlaying.Snapshot;
        var key = snap is { HasTrack: true } ? $"{snap.AppId}|{snap.Title}|{snap.Artist}" : null;
        if (key == _coverKey)
        {
            return;
        }

        _coverKey = key;
        Cover.Source = null;
        CoverPlaceholder.Visibility = Visibility.Visible;
        if (key is null)
        {
            return;
        }

        var bytes = await _nowPlaying.GetThumbnailAsync();
        if (bytes is not { Length: > 0 } || _coverKey != key)
        {
            return;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 48;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            Cover.Source = bmp;
            CoverPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or IOException)
        {
            CoverPlaceholder.Visibility = Visibility.Visible;
        }
    }
}
