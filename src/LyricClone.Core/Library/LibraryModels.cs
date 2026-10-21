using LyricClone.Core.Lyrics;

namespace LyricClone.Core.Library;

public enum VersionKind
{
    Online,
    Local,
}

public enum VersionStatus
{
    Cached,
    Confirmed,
    Pinned,
}

public static class LyricsSources
{
    public const string Lrclib = "lrclib";
    public const string LocalFile = "local_file";
    public const string Embedded = "embedded";
    public const string User = "user";
    public const string QqCache = "qq_cache";
    public const string NeteaseCache = "netease_cache";
    public const string KugouCache = "kugou_cache";

    public static readonly IReadOnlyList<string> PlayerCaches = [QqCache, NeteaseCache, KugouCache];

    public static readonly IReadOnlyList<string> DefaultOrder = [QqCache, NeteaseCache, KugouCache, LocalFile, Embedded, Lrclib];

    public static bool IsOnline(string source) => source == Lrclib;

    public static bool IsPlayerCache(string source) =>
        source is QqCache or NeteaseCache or KugouCache;

    public static bool CanFetch(string source) => IsOnline(source) || IsPlayerCache(source);

    /// <summary>Keeps a custom order and inserts any newly added built-in sources in their default place.</summary>
    public static IReadOnlyList<string> WithKnownDefaults(IReadOnlyList<string> order)
    {
        var list = order.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        int cursor = 0;
        foreach (var source in DefaultOrder)
        {
            int i = list.IndexOf(source);
            if (i < 0)
            {
                list.Insert(Math.Min(cursor, list.Count), source);
                cursor++;
            }
            else
            {
                cursor = i + 1;
            }
        }

        return list;
    }
}

public sealed record Track(
    string Id,
    string Title,
    string Artist,
    string Album,
    long? DurationMs,
    string MatchKey,
    string? FirstSeenAppId,
    DateTimeOffset? LastPlayedAt,
    DateTimeOffset? LastManagedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record LyricsVersion(
    string Id,
    string TrackId,
    VersionKind Kind,
    string Source,
    string? SourceSongId,
    string Name,
    LyricsFormat Format,
    string Content,
    string? Translation,
    bool HasLineSync,
    bool HasWordSync,
    bool HasSyllable,
    bool HasTranslation,
    bool IsInstrumental,
    VersionStatus Status,
    long OffsetMs,
    DateTimeOffset? FetchedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A lyrics result from a provider, before it is saved as a version.</summary>
public sealed record LyricsCandidate(
    string Source,
    string? SourceSongId,
    string Title,
    string Artist,
    string Album,
    long? DurationMs,
    LyricsFormat Format,
    string? Content,
    string? Translation,
    bool IsInstrumental,
    bool HasWordSync)
{
    public bool HasSyncedContent => !string.IsNullOrWhiteSpace(Content) && Format != LyricsFormat.Plain;
}
