using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LyricClone.App.Interop;
using LyricClone.App.Lyrics;
using LyricClone.App.Services;
using LyricClone.Core.Lyrics;
using LyricClone.Core.Playback;
using LyricClone.Core.Settings;
using LyricClone.Core.Views;
using LyricClone.Infrastructure.Settings;

namespace LyricClone.App.Views;

/// <summary>
/// S02: a pill at the top centre of the primary screen. Island-style lyric views were first published by
/// Lyricify (WXRIW) under CC BY-SA 4.0; this view is credited on the About page.
/// </summary>
public partial class IslandWindow : Window
{
    private readonly NowPlayingService _nowPlaying;
    private readonly ISettingsStore _settings;
    private readonly TaskbarQuery _taskbar = new();
    private ParsedLyrics? _lyrics;
    private int _lineIndex = int.MinValue;
    private bool _expanded;
    private bool _hideFromMouse;
    private bool _mouseHideArmed;
    private Rect _mouseAvoid;
    private string? _coverKey;
    private bool _dragging;
    private bool _placing;
    private Point _dragOffset;
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private bool _hasFitted;
    private bool _fitQueued;
    private bool _fitAnimate;
    private DateTimeOffset _marqueeAt;

    public IslandWindow(NowPlayingService nowPlaying, ISettingsStore settings)
    {
        InitializeComponent();
        _nowPlaying = nowPlaying;
        _settings = settings;

        SourceInitialized += (_, _) => NativeWindow.MakeToolWindow(this);
        SizeChanged += (_, _) => PlaceIsland();
        MouseEnter += OnPointerEnter;
        MouseLeave += OnPointerLeave;
        PreviewMouseLeftButtonDown += OnDragStart;
        PreviewMouseMove += OnDragMove;
        PreviewMouseLeftButtonUp += OnDragEnd;
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (!_dragging)
            {
                HideFromMouse();
            }
        };
        _nowPlaying.PropertyChanged += OnNowPlayingChanged;
        _settings.Changed += OnSettingsChanged;
        CompositionTarget.Rendering += OnFrame;
        Closed += (_, _) =>
        {
            _hideTimer.Stop();
            CompositionTarget.Rendering -= OnFrame;
            _nowPlaying.PropertyChanged -= OnNowPlayingChanged;
            _settings.Changed -= OnSettingsChanged;
        };

        Loaded += (_, _) => FitToLyrics(animate: false);
        ApplySettings(settings.Current.Island);
        Refresh();
    }

    private void OnSettingsChanged(object? sender, AppSettings s) => Dispatcher.InvokeAsync(() =>
    {
        ApplySettings(s.Island);
        UpdateVisibility();
    });

    private void ApplySettings(IslandSettings s)
    {
        var family = string.IsNullOrWhiteSpace(s.FontFamily) ? (FontFamily)FindResource("Font.Sans") : new FontFamily(s.FontFamily);
        var weight = s.Bold ? FontWeights.Bold : FontWeights.SemiBold;
        bool showTr = _settings.Current.ShowsTranslation(s.ShowTranslation);
        Line.ShowTranslation = showTr;
        NextLine.ShowTranslation = false;
        _lyrics = _nowPlaying.Lyrics;
        Line.FontFamily = family;
        NextLine.FontFamily = family;
        Line.FontWeight = weight;
        NextLine.FontWeight = weight;
        Line.Karaoke = s.Karaoke;
        Line.TranslationRatio = (double)FindResource("Lyric.Island.TranslationRatio");
        Line.TranslationGap = (double)FindResource("Lyric.Island.TranslationGap");
        LyricColors.Apply(Line, s.Colors);
        LyricColors.Apply(NextLine, s.Colors);
        Scale.ScaleX = Scale.ScaleY = Math.Clamp(s.Scale, 0.6, 1.8);
        Pill.MinWidth = (double)FindResource("Layout.Island.MinWidth");
        Pill.MaxWidth = (double)FindResource("Layout.Island.MaxWidth");
        var bg = ((Brush)FindResource("Brush.Lyric.OverlayBg")).Clone();
        bg.Opacity = Math.Clamp(s.BackgroundOpacity, 0.15, 1);
        Pill.Background = bg;
        Line.FontSize = (double)FindResource(s.ContinentMode ? "Lyric.IslandExpanded.Size" : "Lyric.Island.Size");
        UpdateLine(force: true);
        UpdateVisibility();
        ApplyDockChrome(s);
        PlaceIsland();
    }

    private void OnNowPlayingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NowPlayingService.Resolved) or nameof(NowPlayingService.StatusText) or nameof(NowPlayingService.SurfaceStatus) or nameof(NowPlayingService.Snapshot))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        _lyrics = _nowPlaying.Lyrics;
        _lineIndex = int.MinValue;
        UpdateLine(force: true);
        UpdateVisibility();
        _ = LoadCoverAsync();
    }

    private void UpdateVisibility()
    {
        var s = _settings.Current.Island;
        bool idle = _nowPlaying.Snapshot is not { HasTrack: true } || _nowPlaying.Snapshot.Status != PlaybackStatus.Playing;
        bool hide = _hideFromMouse || (idle && s.AutoHideWhenIdle && !_expanded);
        if (!hide && s.HideOnFullscreen && _taskbar.Read().FullscreenApp)
        {
            hide = true;
        }

        if (!hide && s.HideOnLyricsFullscreen)
        {
            hide = Application.Current.Windows.OfType<Window>().Any(w =>
                w.IsVisible && w is FullscreenLyricsWindow or StageWindow);
        }

        NativeWindow.SetClickThrough(this, _hideFromMouse);
        IsHitTestVisible = !hide && !_hideFromMouse;

        if (_hideFromMouse)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            return;
        }

        var target = hide ? 0.0 : Math.Clamp(s.Opacity, 0.15, 1);
        if (Math.Abs(Opacity - target) > 0.01)
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(target, (Duration)FindResource("Motion.Base")));
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (_hideFromMouse)
        {
            var cursor = NativeWindow.CursorDip(this);
            if (cursor is Point p)
            {
                if (_mouseAvoid.Contains(p))
                {
                    _mouseHideArmed = true;
                }
                else if (_mouseHideArmed)
                {
                    _hideFromMouse = false;
                    _mouseHideArmed = false;
                    UpdateVisibility();
                }
            }
        }

        if (IsVisible)
        {
            UpdateLine(force: false);
            TickMarquee();
        }
    }

    /// <summary>Duck out of the way so the pointer can reach whatever is under the island.</summary>
    internal void HideFromMouse()
    {
        if (_hideFromMouse)
        {
            return;
        }

        _hideFromMouse = true;
        _mouseHideArmed = false;
        _expanded = false;
        _mouseAvoid = new Rect(Left, Top, Math.Max(ActualWidth, 1), Math.Max(ActualHeight, 1));
        _mouseAvoid.Inflate(16, 16);
        UpdateVisibility();
    }

    private void UpdateLine(bool force)
    {
        var lyrics = _lyrics;
        long pos = _nowPlaying.Clock.PositionMs;
        var island = _settings.Current.Island;
        if (lyrics is null || lyrics.IsEmpty)
        {
            if (force || _lineIndex != -2)
            {
                _lineIndex = -2;
                Line.Line = null;
                Line.Placeholder = _nowPlaying.SurfaceStatus;
                HideNextLine();
                ResetMarquee();
                RequestFit(animate: !force);
            }

            return;
        }

        int index = lyrics.FindLineIndex(pos, _nowPlaying.Snapshot?.DurationMs);
        if (force || index != _lineIndex)
        {
            _lineIndex = index;
            var next = index >= 0 ? lyrics.Lines[index] : null;
            var shown = next is { Text.Length: 0, HasSyllables: false } ? null : next;
            Line.Line = shown;
            Line.Placeholder = shown is null ? _nowPlaying.SurfaceStatus : "\u2022 \u2022 \u2022";
            bool translationPair = Line.ShowTranslation && shown is { Translation.Length: > 0 };
            bool showNext = !translationPair && (island.AlwaysTwoLines || island.ContinentMode);
            NextLine.Visibility = showNext ? Visibility.Visible : Visibility.Collapsed;
            NextLine.Line = showNext && index >= 0 && index + 1 < lyrics.Lines.Count
                ? lyrics.Lines[index + 1]
                : null;
            ResetMarquee();
            RequestFit(animate: !force);
        }

        Line.PositionMs = pos;
    }

    private void HideNextLine()
    {
        NextLine.Line = null;
        NextLine.Visibility = Visibility.Collapsed;
    }

    private void ResetMarquee()
    {
        _marqueeAt = DateTimeOffset.UtcNow;
        Line.MarqueeOffset = 0;
        NextLine.MarqueeOffset = 0;
    }

    private void TickMarquee()
    {
        double elapsed = (DateTimeOffset.UtcNow - _marqueeAt).TotalMilliseconds;
        Line.MarqueeOffset = IslandFit.MarqueeOffset(Line.ContentWidth - Line.ActualWidth, elapsed);
        if (NextLine.Visibility == Visibility.Visible)
        {
            NextLine.MarqueeOffset = IslandFit.MarqueeOffset(NextLine.ContentWidth - NextLine.ActualWidth, elapsed);
        }
    }

    private void RequestFit(bool animate)
    {
        if (!IsLoaded)
        {
            return;
        }

        _fitAnimate = animate && _hasFitted;
        if (_fitQueued)
        {
            return;
        }

        _fitQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _fitQueued = false;
            if (IsLoaded)
            {
                FitToLyrics(_fitAnimate);
            }
        }, DispatcherPriority.Loaded);
    }

    private void FitToLyrics(bool animate)
    {
        if (!IsLoaded)
        {
            return;
        }

        double min = (double)FindResource("Layout.Island.MinWidth");
        double max = (double)FindResource("Layout.Island.MaxWidth");

        double lyrics = Line.ContentWidth;
        if (NextLine.Visibility == Visibility.Visible)
        {
            lyrics = Math.Max(lyrics, NextLine.ContentWidth);
        }

        double cover = CoverHost.Visibility == Visibility.Visible ? CoverHost.Width + CoverHost.Margin.Right : 0;
        double pad = Pill.Padding.Left + Pill.Padding.Right;
        double target = IslandFit.PillWidth(lyrics, cover, pad, min, max);
        double current = Pill.ActualWidth > 1 ? Pill.ActualWidth : min;
        Pill.BeginAnimation(WidthProperty, null);

        if (!animate || Math.Abs(current - target) < 0.5)
        {
            Pill.Width = target;
            _hasFitted = true;
            PlaceIsland();
            return;
        }

        Pill.Width = current;
        var anim = new DoubleAnimation(current, target, (Duration)FindResource("Motion.IslandResize"))
        {
            EasingFunction = (IEasingFunction)FindResource("Motion.Ease"),
        };
        anim.Completed += (_, _) => PlaceIsland();
        Pill.BeginAnimation(WidthProperty, anim);
        _hasFitted = true;
    }

    private async Task LoadCoverAsync()
    {
        var snap = _nowPlaying.Snapshot;
        var key = snap is { HasTrack: true } ? $"{snap.AppId}|{snap.Title}|{snap.Artist}" : null;
        if (key is null)
        {
            CoverImage.Source = null;
            CoverPlaceholder.Visibility = Visibility.Collapsed;
            _coverKey = null;
            CoverHost.Visibility = Visibility.Collapsed;
            return;
        }

        if (key == _coverKey)
        {
            CoverHost.Visibility = Visibility.Visible;
            SizeCover();
            return;
        }

        _coverKey = key;
        CoverHost.Visibility = Visibility.Visible;
        CoverImage.Source = null;
        CoverPlaceholder.Visibility = Visibility.Visible;
        SizeCover();

        var bytes = await _nowPlaying.GetThumbnailAsync();
        foreach (var wait in new[] { 800, 1600, 2400 })
        {
            if (bytes is { Length: > 0 })
            {
                break;
            }

            await Task.Delay(wait);
            if (key != _coverKey)
            {
                return;
            }

            bytes = await _nowPlaying.GetThumbnailAsync();
        }

        if (key != _coverKey)
        {
            return;
        }

        if (bytes is not { Length: > 0 } || !TryLoadImage(bytes, out var img))
        {
            CoverImage.Source = null;
            CoverPlaceholder.Visibility = Visibility.Visible;
            SizeCover();
            return;
        }

        CoverImage.Source = img;
        CoverPlaceholder.Visibility = Visibility.Collapsed;
        CoverHost.Visibility = Visibility.Visible;
        SizeCover();
    }

    private static bool TryLoadImage(byte[] bytes, out BitmapImage? image)
    {
        try
        {
            var img = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.DecodePixelWidth = 128;
            img.StreamSource = stream;
            img.EndInit();
            img.Freeze();
            image = img;
            return true;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or IOException)
        {
            image = null;
            return false;
        }
    }

    private void OnTextColSizeChanged(object sender, SizeChangedEventArgs e) => SizeCover();

    private void OnCoverSizeChanged(object sender, SizeChangedEventArgs e) => SizeCover();

    private void SizeCover()
    {
        if (CoverHost.Visibility != Visibility.Visible)
        {
            return;
        }

        double h = (double)FindResource("Layout.Island.Cover");
        bool coverGrew = Math.Abs(CoverHost.Width - h) > 0.5;
        CoverHost.Width = CoverHost.Height = h;
        double radius = Math.Clamp(h * 0.28, 8, 12);
        CoverHost.CornerRadius = new CornerRadius(radius);
        CoverClip.Rect = new Rect(0, 0, h, h);
        CoverClip.RadiusX = CoverClip.RadiusY = radius;
        if (coverGrew)
        {
            RequestFit(_hasFitted);
        }
    }

    private void OnPointerEnter(object sender, MouseEventArgs e)
    {
        if (!_dragging && !_hideFromMouse)
        {
            _hideTimer.Stop();
            _hideTimer.Start();
        }
    }

    private void OnPointerLeave(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            _hideTimer.Stop();
        }
    }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (_hideFromMouse || e.OriginalSource is DependencyObject d && FindParent<Button>(d) is not null)
        {
            return;
        }

        _hideTimer.Stop();
        _dragging = true;
        _dragOffset = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var cursor = NativeWindow.CursorDip(this);
        if (cursor is not Point p)
        {
            return;
        }

        Left = p.X - _dragOffset.X;
        Top = p.Y - _dragOffset.Y;
    }

    private async void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        var mon = NativeWindow.MonitorDip(this);
        var (left, top, dock) = IslandPlacement.Snap(mon.Left, mon.Top, mon.Width, mon.Height, Left, Top, ActualWidth, ActualHeight);
        await _settings.UpdateAsync(s =>
        {
            s.Island.Left = left;
            s.Island.Top = top;
            s.Island.Dock = dock;
        });
    }

    private void ApplyDockChrome(IslandSettings s)
    {
        double r = s.ContinentMode ? 24 : 22;
        var chrome = IslandChrome.For(s.Dock, r);

        Pill.CornerRadius = new CornerRadius(chrome.Corners.TopLeft, chrome.Corners.TopRight, chrome.Corners.BottomRight, chrome.Corners.BottomLeft);
        Pill.Margin = new Thickness(chrome.Margin.Left, chrome.Margin.Top, chrome.Margin.Right, chrome.Margin.Bottom);
        Pill.Effect = s.Dock == IslandDock.None ? (Effect)FindResource("Shadow.Pop") : null;

        var fill = Pill.Background;
        PlaceEar(EarNW, chrome.NW, chrome.EarSize, fill);
        PlaceEar(EarNE, chrome.NE, chrome.EarSize, fill);
        PlaceEar(EarSW, chrome.SW, chrome.EarSize, fill);
        PlaceEar(EarSE, chrome.SE, chrome.EarSize, fill);
    }

    private static void PlaceEar(System.Windows.Shapes.Path ear, IslandEarSpec spec, double size, Brush fill)
    {
        ear.Width = ear.Height = size;
        if (spec.Show)
        {
            var square = new RectangleGeometry(new Rect(0, 0, size, size));
            var hole = new EllipseGeometry(new Point(spec.HoleX * size, spec.HoleY * size), size, size);
            var wrap = new CombinedGeometry(GeometryCombineMode.Exclude, square, hole);
            wrap.Freeze();
            ear.Data = wrap;
        }

        ear.Fill = fill;
        ear.Stretch = Stretch.None;
        ear.Visibility = spec.Show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PlaceIsland()
    {
        if (!IsLoaded || _dragging || _placing || ActualWidth < 1 || ActualHeight < 1)
        {
            return;
        }

        _placing = true;
        try
        {
            var s = _settings.Current.Island;
            var mon = NativeWindow.MonitorDip(this);
            double width = ActualWidth;
            double height = ActualHeight;
            switch (s.Dock)
            {
                case IslandDock.Top:
                    Left = s.Left is double x
                        ? Math.Clamp(x, mon.Left, mon.Right - width)
                        : IslandPlacement.CenteredLeft(mon.Left, mon.Width, width);
                    Top = mon.Top;
                    break;
                case IslandDock.Bottom:
                    Left = s.Left is double bx
                        ? Math.Clamp(bx, mon.Left, mon.Right - width)
                        : IslandPlacement.CenteredLeft(mon.Left, mon.Width, width);
                    Top = mon.Bottom - height;
                    break;
                case IslandDock.Left:
                    Left = mon.Left;
                    Top = s.Top is double ly
                        ? Math.Clamp(ly, mon.Top, mon.Bottom - height)
                        : mon.Top + (mon.Height - height) / 2;
                    break;
                case IslandDock.Right:
                    Left = mon.Right - width;
                    Top = s.Top is double ry
                        ? Math.Clamp(ry, mon.Top, mon.Bottom - height)
                        : mon.Top + (mon.Height - height) / 2;
                    break;
                case IslandDock.TopLeft:
                    Left = mon.Left;
                    Top = mon.Top;
                    break;
                case IslandDock.TopRight:
                    Left = mon.Right - width;
                    Top = mon.Top;
                    break;
                case IslandDock.BottomLeft:
                    Left = mon.Left;
                    Top = mon.Bottom - height;
                    break;
                case IslandDock.BottomRight:
                    Left = mon.Right - width;
                    Top = mon.Bottom - height;
                    break;
                default:
                    if (s.Left is double fl && s.Top is double ft)
                    {
                        var snapped = IslandPlacement.Snap(mon.Left, mon.Top, mon.Width, mon.Height, fl, ft, width, height);
                        Left = snapped.Left;
                        Top = snapped.Top;
                    }
                    else
                    {
                        Left = IslandPlacement.CenteredLeft(mon.Left, mon.Width, width);
                        Top = mon.Top;
                    }

                    break;
            }
        }
        finally
        {
            _placing = false;
        }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        for (var d = child; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is T t)
            {
                return t;
            }
        }

        return null;
    }
}
