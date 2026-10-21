using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LyricClone.App.Interop;
using LyricClone.App.Lyrics;
using LyricClone.App.Services;
using LyricClone.Core.Lyrics;
using LyricClone.Core.Settings;
using LyricClone.Infrastructure.Settings;

namespace LyricClone.App.Views;

public partial class DesktopLyricsWindow : Window
{
    private const double MinFontSize = 16;
    private const double MaxFontSize = 96;

    private readonly NowPlayingService _nowPlaying;
    private readonly ISettingsStore _settings;
    private readonly Action _openSettings;
    private int _lineIndex = int.MinValue;
    private ParsedLyrics? _lyrics;

    public DesktopLyricsWindow(NowPlayingService nowPlaying, ISettingsStore settings, Action openSettings)
    {
        InitializeComponent();
        _nowPlaying = nowPlaying;
        _settings = settings;
        _openSettings = openSettings;

        ApplySettings(settings.Current.Desktop);
        _settings.Changed += OnSettingsChanged;
        _nowPlaying.PropertyChanged += OnNowPlayingChanged;

        SourceInitialized += (_, _) =>
        {
            NativeWindow.MakeToolWindow(this);
            NativeWindow.SetClickThrough(this, _settings.Current.Desktop.Locked);
        };
        Loaded += (_, _) => PlaceInitially();
        MouseEnter += (_, _) => SetHover(true);
        MouseLeave += (_, _) => SetHover(false);
        MouseLeftButtonDown += OnDragStart;
        CompositionTarget.Rendering += OnFrame;
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnFrame;
            _nowPlaying.PropertyChanged -= OnNowPlayingChanged;
            _settings.Changed -= OnSettingsChanged;
        };

        RefreshLyrics();
    }

    public bool IsLocked => _settings.Current.Desktop.Locked;

    public Task SetLockedAsync(bool locked) => _settings.UpdateAsync(s => s.Desktop.Locked = locked);

    private void OnSettingsChanged(object? sender, AppSettings s) =>
        Dispatcher.InvokeAsync(() =>
        {
            ApplySettings(s.Desktop);
            RefreshLyrics();
            if (s.Desktop.Left is null || s.Desktop.Top is null)
            {
                PlaceInitially();
            }

            NativeWindow.SetClickThrough(this, s.Desktop.Locked);
            if (s.Desktop.Locked)
            {
                SetHover(false);
            }
        });

    private void ApplySettings(DesktopLyricsSettings d)
    {
        CurrentLine.FontSize = d.FontSize;
        NextLine.FontSize = d.FontSize * 0.75;
        CurrentLine.ShowTranslation = _settings.Current.ShowsTranslation(d.ShowTranslation);
        NextLine.ShowTranslation = false;
        CurrentLine.OutlineThickness = d.Outline ? (double)FindResource("Lyric.OutlineThickness") : 0;
        NextLine.OutlineThickness = CurrentLine.OutlineThickness;
        var align = d.Alignment switch
        {
            LyricAlignment.Left => TextAlignment.Left,
            LyricAlignment.Right => TextAlignment.Right,
            _ => TextAlignment.Center,
        };
        CurrentLine.TextAlignment = align;
        NextLine.TextAlignment = align;
        var family = string.IsNullOrWhiteSpace(d.FontFamily) ? (FontFamily)FindResource("Font.Sans") : new FontFamily(d.FontFamily);
        CurrentLine.FontFamily = family;
        NextLine.FontFamily = family;
        LyricColors.Apply(CurrentLine, d.Colors);
        LyricColors.Apply(NextLine, d.Colors);

        Width = d.Width;
    }

    private void PlaceInitially()
    {
        var d = _settings.Current.Desktop;
        var area = SystemParameters.WorkArea;
        if (d.Left is double left && d.Top is double top && IsOnScreen(left, top))
        {
            Left = left;
            Top = top;
            return;
        }

        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - area.Height * 0.12;
    }

    private static bool IsOnScreen(double left, double top) =>
        left >= SystemParameters.VirtualScreenLeft - 50
        && top >= SystemParameters.VirtualScreenTop - 50
        && left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50
        && top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50;

    private void OnNowPlayingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NowPlayingService.Resolved) or nameof(NowPlayingService.StatusText) or nameof(NowPlayingService.SurfaceStatus) or nameof(NowPlayingService.Snapshot))
        {
            RefreshLyrics();
        }
    }

    private void RefreshLyrics()
    {
        _lyrics = _nowPlaying.Lyrics;
        _lineIndex = int.MinValue;
        UpdateLines(force: true);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (!IsVisible)
        {
            return;
        }

        UpdateLines(force: false);
    }

    private void UpdateLines(bool force)
    {
        var lyrics = _lyrics;
        long pos = _nowPlaying.Clock.PositionMs;

        if (lyrics is null || lyrics.IsEmpty)
        {
            if (force || _lineIndex != -2)
            {
                _lineIndex = -2;
                CurrentLine.Line = null;
                CurrentLine.Placeholder = _nowPlaying.SurfaceStatus;
                NextLine.Line = null;
                NextLine.Placeholder = "";
                NextLine.Visibility = Visibility.Collapsed;
            }

            return;
        }

        int index = lyrics.FindLineIndex(pos, _nowPlaying.Snapshot?.DurationMs);
        if (force || index != _lineIndex)
        {
            _lineIndex = index;
            LyricLine? shown = null;
            if (index < 0)
            {
                CurrentLine.Line = null;
                CurrentLine.Placeholder = _nowPlaying.SurfaceStatus;
                shown = null;
            }
            else
            {
                shown = lyrics.Lines[index];
                CurrentLine.Line = shown;
                CurrentLine.Placeholder = "";
            }

            bool translationPair = CurrentLine.ShowTranslation && shown is { Translation.Length: > 0 };
            bool showNext = _settings.Current.Desktop.TwoLines && !translationPair;
            NextLine.Visibility = showNext ? Visibility.Visible : Visibility.Collapsed;
            if (!showNext)
            {
                NextLine.Line = null;
                NextLine.Placeholder = "";
            }
            else if (index < 0)
            {
                NextLine.Line = lyrics.Lines[0];
            }
            else
            {
                NextLine.Line = index + 1 < lyrics.Lines.Count ? lyrics.Lines[index + 1] : null;
                NextLine.Placeholder = "";
            }
        }

        CurrentLine.PositionMs = pos;
    }

    private void SetHover(bool hover)
    {
        bool show = hover && !IsLocked;
        Toolbar.Visibility = show ? Visibility.Visible : Visibility.Hidden;
        LyricsPanel.Background = show ? new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)) : Brushes.Transparent;
    }

    private async void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (IsLocked || e.OriginalSource is DependencyObject d && FindParent<System.Windows.Controls.Button>(d) is not null)
        {
            return;
        }

        DragMove();
        await _settings.UpdateAsync(s =>
        {
            s.Desktop.Left = Left;
            s.Desktop.Top = Top;
        });
    }

    private async void OnLock(object sender, RoutedEventArgs e) => await SetLockedAsync(true);

    private async void OnSmaller(object sender, RoutedEventArgs e) =>
        await _settings.UpdateAsync(s => s.Desktop.FontSize = Math.Max(MinFontSize, s.Desktop.FontSize - 2));

    private async void OnLarger(object sender, RoutedEventArgs e) =>
        await _settings.UpdateAsync(s => s.Desktop.FontSize = Math.Min(MaxFontSize, s.Desktop.FontSize + 2));

    private void OnSettings(object sender, RoutedEventArgs e) => _openSettings();

    private async void OnClose(object sender, RoutedEventArgs e) => await _settings.UpdateAsync(s => s.Desktop.Enabled = false);

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
