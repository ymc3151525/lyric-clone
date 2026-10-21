using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LyricClone.Core.Lyrics;
using LyricClone.Core.Playback;
using LyricClone.Core.Settings;
using LyricClone.Infrastructure.Lyrics;
using LyricClone.Infrastructure.Settings;
using LyricClone.Infrastructure.Smtc;
using Microsoft.Extensions.Logging;

namespace LyricClone.App.Services;

/// <summary>
/// UI-thread view of what is playing: the snapshot, the resolved lyrics and a shared clock.
/// Lyric windows bind to this and read <see cref="Clock"/> once per frame.
/// </summary>
public sealed partial class NowPlayingService : ObservableObject, IAsyncDisposable
{
    private readonly ISessionWatcher _watcher;
    private readonly ILyricsResolver _resolver;
    private readonly ISettingsStore _settings;
    private readonly ILogger<NowPlayingService> _logger;
    private readonly Dispatcher _dispatcher;
    private CancellationTokenSource? _resolveCts;
    private MediaSnapshot? _lastTrackSnapshot;
    private long? _maxDurationMs;

    [ObservableProperty]
    private MediaSnapshot? _snapshot;

    [ObservableProperty]
    private ResolvedLyrics _resolved = ResolvedLyrics.None;

    [ObservableProperty]
    private bool _isResolving;

    [ObservableProperty]
    private SmtcAvailability _availability;

    public NowPlayingService(ISessionWatcher watcher, ILyricsResolver resolver, ISettingsStore settings, ILogger<NowPlayingService> logger)
    {
        _watcher = watcher;
        _resolver = resolver;
        _settings = settings;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;
        Clock = new PlaybackClock();
        ApplySettings(settings.Current);
        _settings.Changed += (_, s) => _dispatcher.InvokeAsync(() =>
        {
            ApplySettings(s);
            OnPropertyChanged(nameof(Lyrics));
            OnPropertyChanged(nameof(SurfaceStatus));
        });
    }

    public PlaybackClock Clock { get; }

    public ParsedLyrics Lyrics => LyricPresenter.Apply(
        Resolved.Lyrics,
        _settings.Current.PreferSimplifiedChinese,
        _settings.Current.Romaji,
        _settings.Current.SplitLongLines,
        _settings.Current.ShowExplicitLyrics,
        showTranslation: _settings.Current.ShowChineseTranslation);

    public string StatusText => Snapshot switch
    {
        null when Availability == SmtcAvailability.Unavailable => "这台电脑上的 Windows 媒体控制不可用，无法读取播放信息。",
        null => "没有正在播放的内容",
        { HasTrack: false } => "没有正在播放的内容",
        var s when IsResolving => $"{s.Title} — {s.Artist}",
        var s when Resolved.State == ResolveState.Instrumental => "纯音乐",
        var s when Resolved.State is ResolveState.NotFound or ResolveState.Error => $"{s.Title} — {s.Artist}",
        var s => $"{s.Title} — {s.Artist}",
    };

    /// <summary>Short phrase lyric windows show when there is no current line. Never includes raw errors.</summary>
    public string SurfaceStatus
    {
        get
        {
            if (Snapshot is null && Availability == SmtcAvailability.Unavailable)
            {
                return LyricSurfaceStatus.Text(LyricEmptyKind.Unavailable);
            }

            if (Snapshot is null || !Snapshot.HasTrack)
            {
                return LyricSurfaceStatus.Text(LyricEmptyKind.NoTrack);
            }

            var title = $"{Snapshot.Title} — {Snapshot.Artist}";
            if (IsResolving)
            {
                return LyricSurfaceStatus.Text(LyricEmptyKind.Resolving, title);
            }

            return Resolved.State switch
            {
                ResolveState.Instrumental => LyricSurfaceStatus.Text(LyricEmptyKind.Instrumental),
                ResolveState.NotFound => LyricSurfaceStatus.Text(LyricEmptyKind.NotFound),
                ResolveState.Error => LyricSurfaceStatus.Text(LyricEmptyKind.Error),
                _ => LyricSurfaceStatus.Text(LyricEmptyKind.Title, title),
            };
        }
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _watcher.IsAppAllowed = IsAppAllowed;
        _watcher.SnapshotChanged += (_, s) => _dispatcher.InvokeAsync(() => OnSnapshot(s));
        await _watcher.StartAsync(ct);
        Availability = _watcher.Availability;
        OnSnapshot(_watcher.Current);
    }

    /// <summary>Re-reads saved versions for the current track, e.g. after the user pinned another version.</summary>
    public async Task ReloadCurrentAsync()
    {
        if (Resolved.Track is not { } track || Snapshot is not { } snap)
        {
            return;
        }

        var order = _settings.Current.EffectiveSourceOrder(snap.AppId);
        Resolved = await _resolver.ReloadAsync(track.Id, order, CancellationToken.None);
    }

    public Task<bool> PlayPauseAsync() => _watcher.TryPlayPauseAsync();

    public Task<bool> NextAsync() => _watcher.TryNextAsync();

    public Task<bool> PreviousAsync() => _watcher.TryPreviousAsync();

    public Task<byte[]?> GetThumbnailAsync() => _watcher.TryGetThumbnailAsync();

    /// <summary>Every app with a media session, including ones filtered out by settings.</summary>
    public IReadOnlyList<string> SessionAppIds => _watcher.SessionAppIds;

    public async ValueTask DisposeAsync()
    {
        _resolveCts?.Cancel();
        await _watcher.DisposeAsync();
    }

    partial void OnSnapshotChanged(MediaSnapshot? value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SurfaceStatus));
    }

    partial void OnResolvedChanged(ResolvedLyrics value)
    {
        OnPropertyChanged(nameof(Lyrics));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SurfaceStatus));
    }

    partial void OnIsResolvingChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SurfaceStatus));
    }

    partial void OnAvailabilityChanged(SmtcAvailability value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SurfaceStatus));
    }

    private bool IsAppAllowed(string appId)
    {
        var s = _settings.Current;
        if (s.AppProfiles.TryGetValue(appId, out var p))
        {
            return p.Enabled;
        }

        return !AppSettings.LooksLikeBrowser(appId) || s.IncludeBrowserSessions;
    }

    private void ApplySettings(AppSettings s)
    {
        Clock.DeviceDelayMs = s.DeviceDelayMs;
        var appId = Snapshot?.AppId;
        var profile = appId is null ? null : s.AppProfiles.GetValueOrDefault(appId);
        Clock.SessionDelayMs = profile?.SessionDelayMs ?? 0;
        Clock.SmoothingEnabled = profile?.TimelineSmoothing ?? s.TimelineSmoothing;
    }

    private void OnSnapshot(MediaSnapshot? snapshot)
    {
        bool appChanged = !string.Equals(snapshot?.AppId, Snapshot?.AppId, StringComparison.OrdinalIgnoreCase);
        bool trackChanged = snapshot is null || !snapshot.HasTrack || !snapshot.IsSameTrack(_lastTrackSnapshot);

        if (trackChanged)
        {
            _resolveCts?.Cancel();
            IsResolving = snapshot is { HasTrack: true };
            Resolved = ResolvedLyrics.None;
        }

        if (snapshot is null || !snapshot.HasTrack)
        {
            Snapshot = snapshot;
            if (appChanged)
            {
                ApplySettings(_settings.Current);
            }

            Clock.Reset();
            _lastTrackSnapshot = null;
            _maxDurationMs = null;
            return;
        }

        snapshot = MediaTimeline.StabilizeDuration(snapshot, _lastTrackSnapshot, ref _maxDurationMs);
        Snapshot = snapshot;
        if (appChanged)
        {
            ApplySettings(_settings.Current);
        }

        Clock.Update(snapshot);

        bool same = snapshot.IsSameTrack(_lastTrackSnapshot);
        if (same && !MediaTimeline.DurationArrived(snapshot, _lastTrackSnapshot))
        {
            return;
        }

        _lastTrackSnapshot = snapshot;
        _ = ResolveAsync(snapshot);
    }

    private async Task ResolveAsync(MediaSnapshot snapshot)
    {
        _resolveCts?.Cancel();
        var cts = new CancellationTokenSource();
        _resolveCts = cts;
        IsResolving = true;
        Resolved = ResolvedLyrics.None;

        try
        {
            var s = _settings.Current;
            var result = await Task.Run(
                () => _resolver.ResolveAsync(snapshot, s.EffectiveSourceOrder(snapshot.AppId), s.LocalLyricsFolder, cts.Token, s.AutoUpdateSavedOnline, s.AutoManageLibrary, s.VersionPick),
                cts.Token);
            if (!cts.IsCancellationRequested)
            {
                Resolved = result;
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resolving lyrics failed");
            if (!cts.IsCancellationRequested)
            {
                Resolved = new ResolvedLyrics(null, null, ParsedLyrics.Empty, ResolveState.Error, Core.Library.SelectionReason.None, ex.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(_resolveCts, cts))
            {
                IsResolving = false;
            }
        }
    }
}
