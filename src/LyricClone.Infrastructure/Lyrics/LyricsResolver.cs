using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;
using LyricClone.Core.Playback;
using LyricClone.Infrastructure.Data;
using LyricClone.Infrastructure.Lrclib;
using Microsoft.Extensions.Logging;

namespace LyricClone.Infrastructure.Lyrics;

public enum ResolveState
{
    NoTrack,
    Found,
    Instrumental,
    NotFound,
    Error,
}

public sealed record ResolvedLyrics(
    Track? Track,
    LyricsVersion? Version,
    ParsedLyrics Lyrics,
    ResolveState State,
    SelectionReason Reason,
    string? Error = null)
{
    public static readonly ResolvedLyrics None = new(null, null, ParsedLyrics.Empty, ResolveState.NoTrack, SelectionReason.None);
}

public interface ILyricsResolver
{
    Task<ResolvedLyrics> ResolveAsync(MediaSnapshot snapshot, IReadOnlyList<string> sourceOrder, string? localFolder, CancellationToken ct, bool refreshStaleOnline = false, bool autoFetch = true, VersionPickMode pick = VersionPickMode.SourceFirst);

    /// <summary>Re-reads the saved versions for a track (after the user pins, edits or offsets one) without fetching.</summary>
    Task<ResolvedLyrics> ReloadAsync(string trackId, IReadOnlyList<string> sourceOrder, CancellationToken ct);

    ParsedLyrics Parse(LyricsVersion version);

    /// <summary>Looks up one source for a saved track and saves the result. Null when the source has nothing.</summary>
    Task<LyricsVersion?> FetchAndSaveAsync(string trackId, string source, string? localFolder, CancellationToken ct);

    /// <summary>Saves a candidate the user picked (search result or ID), replacing that source's version.</summary>
    Task<LyricsVersion> SaveCandidateAsync(string trackId, LyricsCandidate candidate, CancellationToken ct);
}

/// <summary>Snapshot → track row → saved version (pinned / confirmed / source order) → fetch missing sources in order.</summary>
public sealed class LyricsResolver : ILyricsResolver
{
    private static readonly TimeSpan MissMemory = TimeSpan.FromHours(12);
    private static readonly TimeSpan[] DefaultCacheRetryDelays = [TimeSpan.FromMilliseconds(700), TimeSpan.FromMilliseconds(1600)];
    private const double MinSearchScore = 0.72;

    private readonly ILibraryRepository _repo;
    private readonly ILrclibClient _lrclib;
    private readonly LocalFileProvider _local;
    private readonly ILyricsParser _parser;
    private readonly ILogger<LyricsResolver> _logger;
    private readonly EmbeddedLyricsProvider? _embedded;
    private readonly PlayerCacheProvider _cache;
    private readonly TimeSpan[] _cacheRetryDelays;

    public LyricsResolver(ILibraryRepository repo, ILrclibClient lrclib, LocalFileProvider local, ILyricsParser parser, ILogger<LyricsResolver> logger, EmbeddedLyricsProvider? embedded = null, PlayerCacheProvider? playerCache = null, TimeSpan[]? cacheRetryDelays = null)
    {
        _repo = repo;
        _lrclib = lrclib;
        _local = local;
        _parser = parser;
        _logger = logger;
        _embedded = embedded;
        _cache = playerCache ?? new PlayerCacheProvider();
        _cacheRetryDelays = cacheRetryDelays ?? DefaultCacheRetryDelays;
    }

    public async Task<ResolvedLyrics> ResolveAsync(MediaSnapshot snapshot, IReadOnlyList<string> sourceOrder, string? localFolder, CancellationToken ct, bool refreshStaleOnline = false, bool autoFetch = true, VersionPickMode pick = VersionPickMode.SourceFirst)
    {
        if (!snapshot.HasTrack)
        {
            return ResolvedLyrics.None;
        }

        var (title, artist) = TrackNormalizer.SplitCombined(snapshot.Title, snapshot.Artist);
        var track = await _repo.GetOrCreateTrackAsync(title, artist, snapshot.Album, snapshot.DurationMs, snapshot.AppId, ct);
        await _repo.MarkPlayedAsync(track.Id, snapshot.AppId, ct);

        var versions = await _repo.GetVersionsAsync(track.Id, ct);
        var selection = VersionSelector.Select(versions, sourceOrder, pick);
        if (selection.Version is not null)
        {
            if (!autoFetch)
            {
                return Build(track, selection.Version, selection.Reason);
            }

            foreach (var source in VersionSelector.OfflineSourcesAhead(versions, sourceOrder, selection))
            {
                var local = await FetchAsync(source, track, localFolder, ct);
                if (local is not null && (local.IsInstrumental || !string.IsNullOrWhiteSpace(local.Content)))
                {
                    return Build(track, await SaveCandidateAsync(track.Id, local, ct), SelectionReason.SourceOrder);
                }
            }

            if (refreshStaleOnline && VersionSelector.IsStaleOnlineCache(selection.Version, DateTimeOffset.UtcNow))
            {
                try
                {
                    var fresh = await FetchAsync(selection.Version.Source, track, localFolder, ct);
                    if (fresh is not null && (fresh.IsInstrumental || !string.IsNullOrWhiteSpace(fresh.Content)))
                    {
                        return Build(track, await SaveCandidateAsync(track.Id, fresh, ct), selection.Reason);
                    }
                }
                catch (Exception ex) when (ex is LrclibException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Auto-update of saved lyrics failed");
                }
            }

            return Build(track, selection.Version, selection.Reason);
        }

        if (!autoFetch)
        {
            return new ResolvedLyrics(track, null, ParsedLyrics.Empty, ResolveState.NotFound, SelectionReason.None);
        }

        var misses = await _repo.RecentMissesAsync(track.Id, MissMemory, ct);
        string? lastError = null;
        foreach (var source in VersionSelector.SourcesToFetch(versions, sourceOrder, misses))
        {
            ct.ThrowIfCancellationRequested();
            LyricsCandidate? candidate;
            try
            {
                candidate = await FetchAsync(source, track, localFolder, ct);
            }
            catch (Exception ex) when (ex is LrclibException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Fetching lyrics from {Source} failed", source);
                lastError = ex.Message;
                continue;
            }

            if (candidate is null || (!candidate.IsInstrumental && string.IsNullOrWhiteSpace(candidate.Content)))
            {
                if (!LyricsSources.IsPlayerCache(source))
                {
                    await _repo.RecordMissAsync(track.Id, source, ct);
                }

                continue;
            }

            var saved = await SaveCandidateAsync(track.Id, candidate, ct);
            return Build(track, saved, SelectionReason.SourceOrder);
        }

        var delayed = await RetryPlayerCacheAsync(track, sourceOrder, localFolder, ct);
        if (delayed is not null)
        {
            return Build(track, delayed, SelectionReason.SourceOrder);
        }

        return lastError is null
            ? new ResolvedLyrics(track, null, ParsedLyrics.Empty, ResolveState.NotFound, SelectionReason.None)
            : new ResolvedLyrics(track, null, ParsedLyrics.Empty, ResolveState.Error, SelectionReason.None, lastError);
    }

    public async Task<ResolvedLyrics> ReloadAsync(string trackId, IReadOnlyList<string> sourceOrder, CancellationToken ct)
    {
        var track = await _repo.GetTrackAsync(trackId, ct);
        if (track is null)
        {
            return ResolvedLyrics.None;
        }

        var versions = await _repo.GetVersionsAsync(trackId, ct);
        var selection = VersionSelector.Select(versions, sourceOrder, VersionPickMode.SourceFirst);
        return selection.Version is null
            ? new ResolvedLyrics(track, null, ParsedLyrics.Empty, ResolveState.NotFound, SelectionReason.None)
            : Build(track, selection.Version, selection.Reason);
    }

    public ParsedLyrics Parse(LyricsVersion version)
    {
        if (version.IsInstrumental || string.IsNullOrWhiteSpace(version.Content))
        {
            return ParsedLyrics.Empty;
        }

        if (!_parser.Supports(version.Format))
        {
            _logger.LogWarning("No parser for format {Format}", version.Format);
            return ParsedLyrics.Empty;
        }

        return _parser.Parse(version.Content, version.Format, version.Translation).WithOffset(version.OffsetMs);
    }

    public async Task<LyricsVersion?> FetchAndSaveAsync(string trackId, string source, string? localFolder, CancellationToken ct)
    {
        var track = await _repo.GetTrackAsync(trackId, ct);
        if (track is null)
        {
            return null;
        }

        var candidate = await FetchAsync(source, track, localFolder, ct);
        if (candidate is null || (!candidate.IsInstrumental && string.IsNullOrWhiteSpace(candidate.Content)))
        {
            if (!LyricsSources.IsPlayerCache(source))
            {
                await _repo.RecordMissAsync(track.Id, source, ct);
            }

            return null;
        }

        return await SaveCandidateAsync(track.Id, candidate, ct);
    }

    public Task<LyricsVersion> SaveCandidateAsync(string trackId, LyricsCandidate candidate, CancellationToken ct)
    {
        var parsed = string.IsNullOrWhiteSpace(candidate.Content) ? ParsedLyrics.Empty : _parser.Parse(candidate.Content, candidate.Format, candidate.Translation);
        return _repo.SaveCandidateAsync(trackId, candidate, LyricsFlags.From(parsed), ct);
    }

    private async Task<LyricsVersion?> RetryPlayerCacheAsync(Track track, IReadOnlyList<string> sourceOrder, string? localFolder, CancellationToken ct)
    {
        var caches = sourceOrder.Where(LyricsSources.IsPlayerCache).Where(_cache.HasFolders).ToArray();
        if (caches.Length == 0 || _cacheRetryDelays.Length == 0)
        {
            return null;
        }

        foreach (var delay in _cacheRetryDelays)
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, ct);
            }

            foreach (var source in caches)
            {
                ct.ThrowIfCancellationRequested();
                LyricsCandidate? candidate;
                try
                {
                    candidate = await FetchAsync(source, track, localFolder, ct);
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    continue;
                }

                if (candidate is null || (!candidate.IsInstrumental && string.IsNullOrWhiteSpace(candidate.Content)))
                {
                    continue;
                }

                return await SaveCandidateAsync(track.Id, candidate, ct);
            }
        }

        return null;
    }

    private async Task<LyricsCandidate?> FetchAsync(string source, Track track, string? localFolder, CancellationToken ct)
    {
        switch (source)
        {
            case LyricsSources.LocalFile:
                return await Task.Run(() => _local.Find(localFolder, track.Title, track.Artist), ct);

            case LyricsSources.QqCache:
            case LyricsSources.NeteaseCache:
            case LyricsSources.KugouCache:
                return await Task.Run(() => _cache.Find(source, track), ct);

            case LyricsSources.Embedded:
                return _embedded is null ? null : await _embedded.FindAsync(track.Title, track.Artist, track.DurationMs, ct);

            case LyricsSources.Lrclib:
                return await FetchLrclibAsync(track, ct);

            default:
                return null;
        }
    }

    /// <summary>
    /// Players (especially QQ Music) send compilation / concert album names and jumpy durations.
    /// Exact /api/get with those fields 404s, and a huge search can time out — try signature first without
    /// album, then fall back through broader searches. Get failures must not skip search.
    /// </summary>
    private async Task<LyricsCandidate?> FetchLrclibAsync(Track track, CancellationToken ct)
    {
        var title = TrackNormalizer.SearchTitle(track.Title);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = TrackNormalizer.CleanTitle(track.Title);
        }

        var artist = TrackNormalizer.PrimaryArtist(track.Artist);
        Exception? lastError = null;
        bool completed = false;

        if (!string.IsNullOrWhiteSpace(artist))
        {
            var (exact, ok, error) = await TryGetAsync(title, artist, track.DurationMs, ct);
            completed |= ok;
            lastError ??= error;
            if (exact is not null)
            {
                return await WithTranslationAsync(exact, title, artist, track, ct);
            }

            if (track.DurationMs is > 0)
            {
                (exact, ok, error) = await TryGetAsync(title, artist, durationMs: null, ct);
                completed |= ok;
                lastError ??= error;
                if (exact is not null)
                {
                    return await WithTranslationAsync(exact, title, artist, track, ct);
                }
            }
        }

        foreach (var query in LrclibSearchQueries(title, artist, track.Title))
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<LyricsCandidate> results;
            try
            {
                results = query.Artist is null && query.AsFreeText
                    ? await _lrclib.SearchAsync(query.Title, ct)
                    : await _lrclib.SearchAsync(query.Title, query.Artist, album: null, ct);
            }
            catch (Exception ex) when (ex is LrclibException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "LRCLIB search failed for {Title}", query.Title);
                lastError ??= ex;
                continue;
            }

            completed = true;
            var picked = PickLrclib(results, track);
            if (picked is not null)
            {
                return AttachTranslation(picked, results, track);
            }
        }

        if (!completed && lastError is not null)
        {
            throw lastError;
        }

        return null;
    }

    private async Task<(LyricsCandidate? Candidate, bool Completed, Exception? Error)> TryGetAsync(string title, string artist, long? durationMs, CancellationToken ct)
    {
        try
        {
            return (await _lrclib.GetAsync(title, artist, album: null, durationMs, ct), true, null);
        }
        catch (Exception ex) when (ex is LrclibException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "LRCLIB exact lookup failed");
            return (null, false, ex);
        }
    }

    private static IEnumerable<(string Title, string? Artist, bool AsFreeText)> LrclibSearchQueries(string title, string artist, string rawTitle)
    {
        string? credited = string.IsNullOrWhiteSpace(artist) ? null : artist;
        yield return (title, credited, false);

        var cleaned = TrackNormalizer.CleanTitle(rawTitle);
        if (!string.IsNullOrWhiteSpace(cleaned) && !string.Equals(cleaned, title, StringComparison.Ordinal))
        {
            yield return (cleaned, credited, false);
        }

        if (credited is not null)
        {
            yield return ($"{title} {credited}", null, true);
        }

        if (credited is not null)
        {
            yield return (title, null, true);
        }
    }

    private async Task<LyricsCandidate> WithTranslationAsync(LyricsCandidate main, string title, string artist, Track track, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(main.Translation))
        {
            return main;
        }

        try
        {
            var pool = await _lrclib.SearchAsync(title, artist, album: null, ct);
            return AttachTranslation(main, pool, track);
        }
        catch (Exception ex) when (ex is LrclibException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "LRCLIB translation search failed for {Title}", title);
            return main;
        }
    }

    internal static LyricsCandidate AttachTranslation(LyricsCandidate main, IReadOnlyList<LyricsCandidate> pool, Track track)
    {
        if (!string.IsNullOrWhiteSpace(main.Translation) || string.IsNullOrWhiteSpace(main.Content))
        {
            return main;
        }

        if (!LyricScript.IsEnglishMain(main.Content))
        {
            return main;
        }

        var sibling = pool
            .Where(r => r.SourceSongId != main.SourceSongId && !string.IsNullOrWhiteSpace(r.Content) && !r.IsInstrumental)
            .Where(r => TrackNormalizer.SameSong(track.Title, track.Artist, r.Title, r.Artist)
                || TrackNormalizer.Score(track.Title, track.Artist, track.DurationMs, r.Title, r.Artist, r.DurationMs) >= MinSearchScore)
            .Where(r => LyricScript.ContainsCjk(r.Content))
            .OrderByDescending(r => r.HasSyncedContent)
            .FirstOrDefault();

        return sibling is null ? main : main with { Translation = sibling.Content };
    }

    private static LyricsCandidate? PickLrclib(IReadOnlyList<LyricsCandidate> results, Track track)
    {
        return results
            .Select(r => (
                r,
                score: TrackNormalizer.Score(track.Title, track.Artist, track.DurationMs, r.Title, r.Artist, r.DurationMs)
                    + (r.HasSyncedContent ? 0.05 : 0),
                strong: TrackNormalizer.SameSong(track.Title, track.Artist, r.Title, r.Artist)))
            .Where(x => x.score >= MinSearchScore || (x.strong && x.score >= 0.5))
            .OrderByDescending(x => x.r.HasSyncedContent)
            .ThenByDescending(x => x.score)
            .Select(x => x.r)
            .FirstOrDefault();
    }

    private ResolvedLyrics Build(Track track, LyricsVersion version, SelectionReason reason)
    {
        if (version.IsInstrumental)
        {
            return new ResolvedLyrics(track, version, ParsedLyrics.Empty, ResolveState.Instrumental, reason);
        }

        var parsed = Parse(version);
        return new ResolvedLyrics(track, version, parsed, parsed.IsEmpty ? ResolveState.NotFound : ResolveState.Found, reason);
    }
}
