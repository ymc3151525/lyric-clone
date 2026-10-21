using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LyricClone.App.Services;
using LyricClone.App.ViewModels;
using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;

namespace LyricClone.App.Views;

/// <summary>S14: adjust one version's offset while watching it against live or simulated playback.</summary>
public partial class OffsetDialog : Window
{
    private const long MaxOffsetMs = 600_000;

    private readonly ManagerContext _ctx;
    private readonly ParsedLyrics _raw;
    private readonly long _originalOffset;
    private readonly string _trackId;
    private bool _isPlayingTrack;
    private readonly Stopwatch _sim = new();
    private ParsedLyrics _shifted;
    private double _simBaseMs;
    private bool _syncingText;

    public OffsetDialog(ManagerContext ctx, Track track, LyricsVersion version)
    {
        InitializeComponent();
        _ctx = ctx;
        _raw = version.IsInstrumental || string.IsNullOrWhiteSpace(version.Content)
            ? ParsedLyrics.Empty
            : ctx.Parser.Parse(version.Content, version.Format, version.Translation);
        _originalOffset = version.OffsetMs;
        OffsetMs = version.OffsetMs;
        _shifted = _raw.WithOffset(OffsetMs);
        var versionLabel = version.Source == LyricsSources.Lrclib ? $"LRCLIB · ID {version.SourceSongId}" : version.Name;
        VersionText.Text = $"{track.Title} — {track.Artist}  ·  {versionLabel}";

        _trackId = track.Id;
        UpdatePlayingState();
        SimSlider.Maximum = Math.Max(track.DurationMs ?? 0, _raw.Lines.Count > 0 ? _raw.Lines[^1].EndMs : 0);
        SimSlider.ValueChanged += (_, e) =>
        {
            if (SimSlider.IsMouseCaptureWithin || SimSlider.IsKeyboardFocusWithin)
            {
                _simBaseMs = e.NewValue;
                if (_sim.IsRunning)
                {
                    _sim.Restart();
                }
                else
                {
                    _sim.Reset();
                }
            }
        };

        SetOffsetText();
        if (_isPlayingTrack)
        {
            FollowRadio.IsChecked = true;
        }
        else
        {
            SimRadio.IsChecked = true;
        }

        CompositionTarget.Rendering += OnFrame;
        ctx.NowPlaying.PropertyChanged += OnNowPlayingChanged;
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnFrame;
            ctx.NowPlaying.PropertyChanged -= OnNowPlayingChanged;
        };
    }

    private void OnNowPlayingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NowPlayingService.Resolved))
        {
            UpdatePlayingState();
        }
    }

    /// <summary>"Follow playback" only makes sense while this track is the one playing; fall back to simulation otherwise.</summary>
    private void UpdatePlayingState()
    {
        _isPlayingTrack = _ctx.NowPlaying.Resolved.Track?.Id == _trackId;
        FollowRadio.IsEnabled = _isPlayingTrack;
        FollowRadio.ToolTip = _isPlayingTrack ? null : "这首歌没有在播放";
        if (!_isPlayingTrack && FollowRadio.IsChecked == true)
        {
            SimRadio.IsChecked = true;
        }
    }

    public long OffsetMs { get; private set; }

    private bool Following => FollowRadio.IsChecked == true;

    private double CurrentPositionMs => Following
        ? _ctx.NowPlaying.Clock.PositionMs
        : _simBaseMs + _sim.Elapsed.TotalMilliseconds;

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        bool sim = !Following;
        SimSlider.IsEnabled = sim;
        SimPlayButton.IsEnabled = sim;
        if (sim)
        {
            _simBaseMs = _isPlayingTrack ? _ctx.NowPlaying.Clock.PositionMs : FirstLineStart();
            _sim.Restart();
            SimPlayButton.Content = "暂停";
        }
    }

    private double FirstLineStart() => _shifted.Lines.Count > 0 ? Math.Max(0, _shifted.Lines[0].StartMs - 1000) : 0;

    private void OnSimPlay(object sender, RoutedEventArgs e)
    {
        if (_sim.IsRunning)
        {
            _simBaseMs += _sim.Elapsed.TotalMilliseconds;
            _sim.Reset();
            SimPlayButton.Content = "播放";
        }
        else
        {
            _sim.Restart();
            SimPlayButton.Content = "暂停";
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var pos = CurrentPositionMs;
        if (!Following && pos > SimSlider.Maximum && SimSlider.Maximum > 0)
        {
            _simBaseMs = 0;
            _sim.Restart();
            pos = 0;
        }

        if (!SimSlider.IsMouseCaptureWithin)
        {
            SimSlider.Value = Math.Min(pos, SimSlider.Maximum);
        }

        PositionText.Text = ManagerViewModel.FormatTime((long)pos);

        if (_shifted.IsEmpty)
        {
            CurrentLine.Line = null;
            CurrentLine.Placeholder = "这个版本没有可预览的歌词";
            NextLine.Line = null;
            return;
        }

        int i = _shifted.FindLineIndex((long)pos);
        var current = i >= 0 ? _shifted.Lines[i] : null;
        var next = i + 1 < _shifted.Lines.Count ? _shifted.Lines[i + 1] : null;
        CurrentLine.Placeholder = current is null || current.Text.Length == 0 ? "• • •" : "";
        CurrentLine.Line = current is { Text.Length: > 0 } ? current : null;
        CurrentLine.PositionMs = _shifted.IsSynced ? pos : double.MaxValue;
        NextLine.Line = next;
    }

    private void OnStep(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && long.TryParse(tag, out var step))
        {
            SetOffset(OffsetMs + step);
        }
    }

    private void OnReset(object sender, RoutedEventArgs e) => SetOffset(0);

    private void OnOffsetText(object sender, TextChangedEventArgs e)
    {
        if (_syncingText)
        {
            return;
        }

        var text = OffsetBox.Text.Trim().Replace('−', '-');
        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v))
        {
            ApplyOffset(Math.Clamp(v, -MaxOffsetMs, MaxOffsetMs));
        }
    }

    private void SetOffset(long value)
    {
        ApplyOffset(Math.Clamp(value, -MaxOffsetMs, MaxOffsetMs));
        SetOffsetText();
    }

    private void ApplyOffset(long value)
    {
        OffsetMs = value;
        _shifted = _raw.WithOffset(value);
    }

    private void SetOffsetText()
    {
        _syncingText = true;
        OffsetBox.Text = OffsetMs.ToString(CultureInfo.InvariantCulture);
        _syncingText = false;
    }

    private void OnSave(object sender, RoutedEventArgs e) => DialogResult = OffsetMs != _originalOffset;
}
