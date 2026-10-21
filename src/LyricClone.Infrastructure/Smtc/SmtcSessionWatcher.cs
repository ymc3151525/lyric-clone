using LyricClone.Core.Playback;
using Microsoft.Extensions.Logging;
using Windows.Media.Control;
using WinStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace LyricClone.Infrastructure.Smtc;

public enum SmtcAvailability
{
    Unknown,
    Available,
    Unavailable,
}

public interface ISessionWatcher : IAsyncDisposable
{
    SmtcAvailability Availability { get; }

    MediaSnapshot? Current { get; }

    IReadOnlyList<string> SessionAppIds { get; }

    /// <summary>Raised on any change of the active session: track, status or timeline. Not on the UI thread.</summary>
    event EventHandler<MediaSnapshot?>? SnapshotChanged;

    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Filter applied when picking the active session (e.g. disabled apps, browsers).</summary>
    Func<string, bool> IsAppAllowed { get; set; }

    Task<bool> TryPlayPauseAsync();

    Task<bool> TryNextAsync();

    Task<bool> TryPreviousAsync();

    /// <summary>Cover art of the active session's track as encoded image bytes, or null when the player gives none.</summary>
    Task<byte[]?> TryGetThumbnailAsync();
}

/// <summary>
/// Reads Windows' System Media Transport Controls. Uses events plus a 1 s poll, because some players
/// never raise timeline events and some stop raising them after a while.
/// </summary>
public sealed class SmtcSessionWatcher : ISessionWatcher
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly ILogger<SmtcSessionWatcher> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private MediaSnapshot? _current;
    private IReadOnlyList<string> _sessionAppIds = [];

    public SmtcSessionWatcher(ILogger<SmtcSessionWatcher> logger)
    {
        _logger = logger;
    }

    public SmtcAvailability Availability { get; private set; }

    public MediaSnapshot? Current => Volatile.Read(ref _current);

    public IReadOnlyList<string> SessionAppIds => Volatile.Read(ref _sessionAppIds);

    public Func<string, bool> IsAppAllowed { get; set; } = _ => true;

    public event EventHandler<MediaSnapshot?>? SnapshotChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTC is not available on this system");
            Availability = SmtcAvailability.Unavailable;
            Publish(null);
            return;
        }

        Availability = SmtcAvailability.Available;
        _manager.SessionsChanged += (_, _) => _ = RefreshAsync();
        _manager.CurrentSessionChanged += (_, _) => _ = RefreshAsync();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await RefreshAsync();
        _pollTask = PollAsync(_cts.Token);
    }

    public Task<bool> TryPlayPauseAsync() => Control(s => s.TryTogglePlayPauseAsync().AsTask());

    public Task<bool> TryNextAsync() => Control(s => s.TrySkipNextAsync().AsTask());

    public Task<bool> TryPreviousAsync() => Control(s => s.TrySkipPreviousAsync().AsTask());

    public async Task<byte[]?> TryGetThumbnailAsync()
    {
        const ulong MaxBytes = 16 * 1024 * 1024;
        var s = _session;
        if (s is null)
        {
            return null;
        }

        try
        {
            var props = await s.TryGetMediaPropertiesAsync();
            if (props?.Thumbnail is not { } reference)
            {
                return null;
            }

            using var stream = await reference.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > MaxBytes)
            {
                return null;
            }

            var buffer = new byte[stream.Size];
            using var input = stream.AsStreamForRead();
            int read = 0;
            while (read < buffer.Length)
            {
                int n = await input.ReadAsync(buffer.AsMemory(read));
                if (n == 0)
                {
                    break;
                }

                read += n;
            }

            return read == buffer.Length ? buffer : buffer[..read];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Reading the cover image failed");
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        if (_pollTask is not null)
        {
            try
            {
                await _pollTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        Detach();
        _cts?.Dispose();
        _refreshGate.Dispose();
    }

    private async Task<bool> Control(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> action)
    {
        var s = _session;
        if (s is null)
        {
            return false;
        }

        try
        {
            return await action(s);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Media control command failed");
            return false;
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            await RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        if (_manager is null || !await _refreshGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var sessions = _manager.GetSessions();
            _sessionAppIds = sessions.Select(s => s.SourceAppUserModelId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            var chosen = PickSession(sessions);
            if (!ReferenceEquals(chosen, _session))
            {
                Detach();
                _session = chosen;
                Attach();
            }

            Publish(chosen is null ? null : await ReadAsync(chosen));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh media sessions");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>Prefer the system's current session, else any playing one, else any paused one.</summary>
    private GlobalSystemMediaTransportControlsSession? PickSession(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions)
    {
        var allowed = sessions.Where(s => IsAppAllowed(s.SourceAppUserModelId)).ToList();
        if (allowed.Count == 0)
        {
            return null;
        }

        var current = _manager?.GetCurrentSession();
        if (current is not null && allowed.Any(s => s.SourceAppUserModelId == current.SourceAppUserModelId)
            && SafeStatus(current) == WinStatus.Playing)
        {
            return current;
        }

        return allowed.FirstOrDefault(s => SafeStatus(s) == WinStatus.Playing)
            ?? (current is not null && allowed.Any(s => s.SourceAppUserModelId == current.SourceAppUserModelId) ? current : null)
            ?? allowed.FirstOrDefault(s => SafeStatus(s) == WinStatus.Paused)
            ?? allowed[0];
    }

    private static WinStatus? SafeStatus(GlobalSystemMediaTransportControlsSession s)
    {
        try
        {
            return s.GetPlaybackInfo()?.PlaybackStatus;
        }
        catch
        {
            return null;
        }
    }

    private async Task<MediaSnapshot?> ReadAsync(GlobalSystemMediaTransportControlsSession s)
    {
        var props = await s.TryGetMediaPropertiesAsync();
        var info = s.GetPlaybackInfo();
        var timeline = s.GetTimelineProperties();

        long end = (long)(timeline.EndTime - timeline.StartTime).TotalMilliseconds;
        bool hasTimeline = end > 0 || timeline.Position > TimeSpan.Zero;

        var status = info?.PlaybackStatus switch
        {
            WinStatus.Playing => PlaybackStatus.Playing,
            WinStatus.Paused => PlaybackStatus.Paused,
            WinStatus.Stopped => PlaybackStatus.Stopped,
            WinStatus.Closed => PlaybackStatus.Closed,
            _ => PlaybackStatus.Paused,
        };

        return new MediaSnapshot(
            s.SourceAppUserModelId,
            props?.Title ?? "",
            props?.Artist is { Length: > 0 } artist ? artist : props?.AlbumArtist ?? "",
            props?.AlbumTitle ?? "",
            end > 0 ? end : null,
            (long)(timeline.Position - timeline.StartTime).TotalMilliseconds,
            timeline.LastUpdatedTime,
            status,
            info?.PlaybackRate ?? 1.0,
            hasTimeline);
    }

    private void Attach()
    {
        if (_session is null)
        {
            return;
        }

        _session.MediaPropertiesChanged += OnSessionChanged;
        _session.PlaybackInfoChanged += OnSessionChanged;
        _session.TimelinePropertiesChanged += OnSessionChanged;
    }

    private void Detach()
    {
        if (_session is null)
        {
            return;
        }

        _session.MediaPropertiesChanged -= OnSessionChanged;
        _session.PlaybackInfoChanged -= OnSessionChanged;
        _session.TimelinePropertiesChanged -= OnSessionChanged;
        _session = null;
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args) => _ = RefreshAsync();

    private void Publish(MediaSnapshot? snapshot)
    {
        var previous = Volatile.Read(ref _current);
        if (previous == snapshot)
        {
            return;
        }

        Volatile.Write(ref _current, snapshot);
        SnapshotChanged?.Invoke(this, snapshot);
    }
}
