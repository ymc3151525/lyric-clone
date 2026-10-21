using Dapper;
using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;
using Microsoft.Data.Sqlite;

namespace LyricClone.Infrastructure.Data;

public enum TrackSort
{
    RecentlyManaged,
    RecentlyPlayed,
    Title,
}

public sealed record TrackQuery(string? Search = null, bool? HasPinned = null, bool? HasLocal = null, string? Source = null, TrackSort Sort = TrackSort.RecentlyPlayed, int Limit = 500);

public interface ILibraryRepository
{
    Task<Track> GetOrCreateTrackAsync(string title, string artist, string album, long? durationMs, string? appId, CancellationToken ct = default);

    Task<Track?> GetTrackAsync(string trackId, CancellationToken ct = default);

    Task<IReadOnlyList<Track>> QueryTracksAsync(TrackQuery query, CancellationToken ct = default);

    Task MarkPlayedAsync(string trackId, string appId, CancellationToken ct = default);

    Task<IReadOnlyList<LyricsVersion>> GetVersionsAsync(string trackId, CancellationToken ct = default);

    Task<LyricsVersion?> GetVersionAsync(string versionId, CancellationToken ct = default);

    /// <summary>Saves an online or auto-found result; replaces the existing version of the same online source and keeps its status.</summary>
    Task<LyricsVersion> SaveCandidateAsync(string trackId, LyricsCandidate candidate, LyricsFlags flags, CancellationToken ct = default);

    Task<LyricsVersion> AddUserVersionAsync(string trackId, string name, LyricsFormat format, string content, string? translation, LyricsFlags flags, CancellationToken ct = default);

    Task UpdateUserVersionAsync(string versionId, LyricsFormat format, string content, string? translation, LyricsFlags flags, CancellationToken ct = default);

    Task RenameVersionAsync(string versionId, string name, CancellationToken ct = default);

    Task SetOffsetAsync(string versionId, long offsetMs, CancellationToken ct = default);

    /// <summary>Pins one version; any other pinned version of the track drops to confirmed (online) or cached (local).</summary>
    Task PinAsync(string versionId, CancellationToken ct = default);

    /// <summary>Unpin keeps online versions confirmed; local versions go back to cached.</summary>
    Task UnpinAsync(string versionId, CancellationToken ct = default);

    Task ConfirmAsync(string versionId, CancellationToken ct = default);

    Task UnconfirmAsync(string versionId, CancellationToken ct = default);

    Task DeleteVersionAsync(string versionId, CancellationToken ct = default);

    Task DeleteTrackAsync(string trackId, CancellationToken ct = default);

    Task RecordMissAsync(string trackId, string source, CancellationToken ct = default);

    Task<IReadOnlyCollection<string>> RecentMissesAsync(string trackId, TimeSpan within, CancellationToken ct = default);
}

public readonly record struct LyricsFlags(bool LineSync, bool WordSync, bool Syllable, bool Translation)
{
    public static LyricsFlags From(ParsedLyrics parsed) =>
        new(parsed.IsSynced, parsed.HasWordSync, false, parsed.HasTranslation);
}

public sealed class LibraryRepository : ILibraryRepository
{
    private readonly LibraryDatabase _db;
    private readonly Func<DateTimeOffset> _now;

    static LibraryRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public LibraryRepository(LibraryDatabase db, Func<DateTimeOffset>? now = null)
    {
        _db = db;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<Track> GetOrCreateTrackAsync(string title, string artist, string album, long? durationMs, string? appId, CancellationToken ct = default)
    {
        var key = TrackNormalizer.MatchKey(title, artist, album);
        await using var conn = await _db.OpenAsync(ct);

        var rows = await conn.QueryAsync<TrackRow>(new CommandDefinition(
            "SELECT * FROM tracks WHERE match_key = @key", new { key }, cancellationToken: ct));
        var existing = rows
            .OrderBy(r => DurationDistance(r.DurationMs, durationMs))
            .FirstOrDefault(r => DurationDistance(r.DurationMs, durationMs) <= 30_000);
        if (existing is not null)
        {
            if (durationMs is > 0 && (existing.DurationMs is not > 0 || durationMs.Value > existing.DurationMs.Value))
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE tracks SET duration_ms = @durationMs, updated_at = @now WHERE id = @id",
                    new { durationMs, now = Ms(_now()), id = existing.Id }, cancellationToken: ct));
                existing.DurationMs = durationMs;
            }

            return existing.ToModel();
        }

        var now = Ms(_now());
        var row = new TrackRow
        {
            Id = Guid.NewGuid().ToString(),
            Title = string.IsNullOrWhiteSpace(title) ? "Unknown" : title.Trim(),
            Artist = artist.Trim(),
            Album = album.Trim(),
            DurationMs = durationMs is > 0 ? durationMs : null,
            MatchKey = key,
            FirstSeenAppId = appId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO tracks (id, title, artist, album, duration_ms, match_key, first_seen_app_id, created_at, updated_at)
            VALUES (@Id, @Title, @Artist, @Album, @DurationMs, @MatchKey, @FirstSeenAppId, @CreatedAt, @UpdatedAt)
            """, row, cancellationToken: ct));
        return row.ToModel();
    }

    public async Task<Track?> GetTrackAsync(string trackId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<TrackRow>(new CommandDefinition(
            "SELECT * FROM tracks WHERE id = @trackId", new { trackId }, cancellationToken: ct));
        return row?.ToModel();
    }

    public async Task<IReadOnlyList<Track>> QueryTracksAsync(TrackQuery query, CancellationToken ct = default)
    {
        var where = new List<string>();
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            where.Add("(t.title LIKE @s ESCAPE '\\' OR t.artist LIKE @s ESCAPE '\\' OR t.album LIKE @s ESCAPE '\\')");
            p.Add("s", "%" + EscapeLike(query.Search.Trim()) + "%");
        }

        if (query.HasPinned is bool pinned)
        {
            where.Add((pinned ? "" : "NOT ") + "EXISTS (SELECT 1 FROM lyrics_versions v WHERE v.track_id = t.id AND v.status = 'pinned')");
        }

        if (query.HasLocal is bool local)
        {
            where.Add((local ? "" : "NOT ") + "EXISTS (SELECT 1 FROM lyrics_versions v WHERE v.track_id = t.id AND v.kind = 'local')");
        }

        if (!string.IsNullOrWhiteSpace(query.Source))
        {
            where.Add("EXISTS (SELECT 1 FROM lyrics_versions v WHERE v.track_id = t.id AND v.source = @source)");
            p.Add("source", query.Source);
        }

        var order = query.Sort switch
        {
            TrackSort.RecentlyManaged => "t.last_managed_at DESC NULLS LAST, t.title COLLATE NOCASE",
            TrackSort.Title => "t.title COLLATE NOCASE, t.artist COLLATE NOCASE",
            _ => "t.last_played_at DESC NULLS LAST, t.title COLLATE NOCASE",
        };

        p.Add("limit", Math.Clamp(query.Limit, 1, 5000));
        var sql = "SELECT t.* FROM tracks t"
            + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
            + " ORDER BY " + order + " LIMIT @limit";

        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<TrackRow>(new CommandDefinition(sql, p, cancellationToken: ct));
        return rows.Select(r => r.ToModel()).ToArray();
    }

    public async Task MarkPlayedAsync(string trackId, string appId, CancellationToken ct = default)
    {
        var now = Ms(_now());
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE tracks SET last_played_at = @now WHERE id = @trackId;
            INSERT INTO track_sessions (track_id, app_id, last_seen_at) VALUES (@trackId, @appId, @now)
              ON CONFLICT (track_id, app_id) DO UPDATE SET last_seen_at = excluded.last_seen_at;
            """, new { trackId, appId, now }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<LyricsVersion>> GetVersionsAsync(string trackId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<VersionRow>(new CommandDefinition(
            "SELECT * FROM lyrics_versions WHERE track_id = @trackId ORDER BY kind, created_at", new { trackId }, cancellationToken: ct));
        return rows.Select(r => r.ToModel()).ToArray();
    }

    public async Task<LyricsVersion?> GetVersionAsync(string versionId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<VersionRow>(new CommandDefinition(
            "SELECT * FROM lyrics_versions WHERE id = @versionId", new { versionId }, cancellationToken: ct));
        return row?.ToModel();
    }

    public async Task<LyricsVersion> SaveCandidateAsync(string trackId, LyricsCandidate candidate, LyricsFlags flags, CancellationToken ct = default)
    {
        bool online = LyricsSources.IsOnline(candidate.Source);
        bool replaceable = online || LyricsSources.IsPlayerCache(candidate.Source);
        var now = Ms(_now());
        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        VersionRow? existing = replaceable
            ? await conn.QuerySingleOrDefaultAsync<VersionRow>(new CommandDefinition(
                "SELECT * FROM lyrics_versions WHERE track_id = @trackId AND source = @source",
                new { trackId, source = candidate.Source }, tx, cancellationToken: ct))
            : null;

        var row = new VersionRow
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString(),
            TrackId = trackId,
            Kind = online ? "online" : "local",
            Source = candidate.Source,
            SourceSongId = candidate.SourceSongId,
            Name = string.IsNullOrWhiteSpace(candidate.Title) ? candidate.Source : $"{candidate.Title} — {candidate.Artist}".Trim(' ', '—'),
            Format = FormatToDb(candidate.Format),
            Content = candidate.Content ?? "",
            Translation = candidate.Translation,
            HasLineSync = flags.LineSync,
            HasWordSync = flags.WordSync || candidate.HasWordSync,
            HasSyllable = flags.Syllable,
            HasTranslation = flags.Translation,
            IsInstrumental = candidate.IsInstrumental,
            Status = existing?.Status ?? "cached",
            OffsetMs = existing?.OffsetMs ?? 0,
            FetchedAt = now,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
        };

        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO lyrics_versions (id, track_id, kind, source, source_song_id, name, format, content, translation,
              has_line_sync, has_word_sync, has_syllable, has_translation, is_instrumental, status, offset_ms, fetched_at, created_at, updated_at)
            VALUES (@Id, @TrackId, @Kind, @Source, @SourceSongId, @Name, @Format, @Content, @Translation,
              @HasLineSync, @HasWordSync, @HasSyllable, @HasTranslation, @IsInstrumental, @Status, @OffsetMs, @FetchedAt, @CreatedAt, @UpdatedAt)
            ON CONFLICT (id) DO UPDATE SET
              source_song_id = excluded.source_song_id, name = excluded.name, format = excluded.format, content = excluded.content,
              translation = excluded.translation, has_line_sync = excluded.has_line_sync, has_word_sync = excluded.has_word_sync,
              has_syllable = excluded.has_syllable, has_translation = excluded.has_translation, is_instrumental = excluded.is_instrumental,
              fetched_at = excluded.fetched_at, updated_at = excluded.updated_at;
            DELETE FROM lookup_misses WHERE track_id = @TrackId AND source = @Source;
            """, row, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return row.ToModel();
    }

    public async Task<LyricsVersion> AddUserVersionAsync(string trackId, string name, LyricsFormat format, string content, string? translation, LyricsFlags flags, CancellationToken ct = default)
    {
        var now = Ms(_now());
        var row = new VersionRow
        {
            Id = Guid.NewGuid().ToString(),
            TrackId = trackId,
            Kind = "local",
            Source = LyricsSources.User,
            Name = string.IsNullOrWhiteSpace(name) ? "Local lyrics" : name.Trim(),
            Format = FormatToDb(format),
            Content = content,
            Translation = string.IsNullOrWhiteSpace(translation) ? null : translation,
            HasLineSync = flags.LineSync,
            HasWordSync = flags.WordSync,
            HasSyllable = flags.Syllable,
            HasTranslation = flags.Translation,
            Status = "cached",
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO lyrics_versions (id, track_id, kind, source, source_song_id, name, format, content, translation,
              has_line_sync, has_word_sync, has_syllable, has_translation, is_instrumental, status, offset_ms, fetched_at, created_at, updated_at)
            VALUES (@Id, @TrackId, @Kind, @Source, NULL, @Name, @Format, @Content, @Translation,
              @HasLineSync, @HasWordSync, @HasSyllable, @HasTranslation, 0, @Status, 0, NULL, @CreatedAt, @UpdatedAt);
            UPDATE tracks SET last_managed_at = @UpdatedAt WHERE id = @TrackId;
            """, row, cancellationToken: ct));
        return row.ToModel();
    }

    public async Task UpdateUserVersionAsync(string versionId, LyricsFormat format, string content, string? translation, LyricsFlags flags, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        int n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE lyrics_versions SET format = @format, content = @content, translation = @translation,
              has_line_sync = @line, has_word_sync = @word, has_syllable = @syl, has_translation = @tr, updated_at = @now
            WHERE id = @versionId AND kind = 'local'
            """,
            new
            {
                versionId, format = FormatToDb(format), content,
                translation = string.IsNullOrWhiteSpace(translation) ? null : translation,
                line = flags.LineSync, word = flags.WordSync, syl = flags.Syllable, tr = flags.Translation, now = Ms(_now()),
            }, cancellationToken: ct));
        if (n == 0)
        {
            throw new InvalidOperationException("Only local versions can be edited.");
        }

        await TouchManagedAsync(conn, versionId, ct);
    }

    public Task RenameVersionAsync(string versionId, string name, CancellationToken ct = default) =>
        ExecuteOnVersionAsync(versionId, "UPDATE lyrics_versions SET name = @name, updated_at = @now WHERE id = @versionId", new { name = name.Trim() }, ct);

    public Task SetOffsetAsync(string versionId, long offsetMs, CancellationToken ct = default) =>
        ExecuteOnVersionAsync(versionId, "UPDATE lyrics_versions SET offset_ms = @offsetMs, updated_at = @now WHERE id = @versionId",
            new { offsetMs = Math.Clamp(offsetMs, -600_000, 600_000) }, ct);

    public async Task PinAsync(string versionId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var trackId = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT track_id FROM lyrics_versions WHERE id = @versionId", new { versionId }, tx, cancellationToken: ct))
            ?? throw new InvalidOperationException("Version not found.");
        var now = Ms(_now());
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE lyrics_versions SET status = CASE kind WHEN 'online' THEN 'confirmed' ELSE 'cached' END, updated_at = @now
              WHERE track_id = @trackId AND status = 'pinned' AND id <> @versionId;
            UPDATE lyrics_versions SET status = 'pinned', updated_at = @now WHERE id = @versionId;
            UPDATE tracks SET last_managed_at = @now WHERE id = @trackId;
            """, new { trackId, versionId, now }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    public Task UnpinAsync(string versionId, CancellationToken ct = default) =>
        ExecuteOnVersionAsync(versionId,
            "UPDATE lyrics_versions SET status = CASE kind WHEN 'online' THEN 'confirmed' ELSE 'cached' END, updated_at = @now WHERE id = @versionId AND status = 'pinned'",
            null, ct);

    public Task ConfirmAsync(string versionId, CancellationToken ct = default) =>
        ExecuteOnVersionAsync(versionId,
            "UPDATE lyrics_versions SET status = 'confirmed', updated_at = @now WHERE id = @versionId AND kind = 'online' AND status = 'cached'",
            null, ct);

    public Task UnconfirmAsync(string versionId, CancellationToken ct = default) =>
        ExecuteOnVersionAsync(versionId,
            "UPDATE lyrics_versions SET status = 'cached', updated_at = @now WHERE id = @versionId AND kind = 'online' AND status IN ('confirmed', 'pinned')",
            null, ct);

    public async Task DeleteVersionAsync(string versionId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        await TouchManagedAsync(conn, versionId, ct);
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM lyrics_versions WHERE id = @versionId", new { versionId }, cancellationToken: ct));
    }

    public async Task DeleteTrackAsync(string trackId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM tracks WHERE id = @trackId", new { trackId }, cancellationToken: ct));
    }

    public async Task RecordMissAsync(string trackId, string source, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO lookup_misses (track_id, source, missed_at) VALUES (@trackId, @source, @now)
              ON CONFLICT (track_id, source) DO UPDATE SET missed_at = excluded.missed_at
            """, new { trackId, source, now = Ms(_now()) }, cancellationToken: ct));
    }

    public async Task<IReadOnlyCollection<string>> RecentMissesAsync(string trackId, TimeSpan within, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        var since = Ms(_now() - within);
        var rows = await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT source FROM lookup_misses WHERE track_id = @trackId AND missed_at >= @since", new { trackId, since }, cancellationToken: ct));
        return rows.ToHashSet(StringComparer.Ordinal);
    }

    private async Task ExecuteOnVersionAsync(string versionId, string sql, object? extra, CancellationToken ct)
    {
        var p = new DynamicParameters(extra);
        p.Add("versionId", versionId);
        p.Add("now", Ms(_now()));
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, p, cancellationToken: ct));
        await TouchManagedAsync(conn, versionId, ct);
    }

    private async Task TouchManagedAsync(SqliteConnection conn, string versionId, CancellationToken ct) =>
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE tracks SET last_managed_at = @now WHERE id = (SELECT track_id FROM lyrics_versions WHERE id = @versionId)",
            new { versionId, now = Ms(_now()) }, cancellationToken: ct));

    private static long DurationDistance(long? a, long? b) =>
        a is > 0 && b is > 0 ? Math.Abs(a.Value - b.Value) : 0;

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    internal static long Ms(DateTimeOffset t) => t.ToUnixTimeMilliseconds();

    internal static DateTimeOffset? FromMs(long? ms) => ms is long v ? DateTimeOffset.FromUnixTimeMilliseconds(v) : null;

    internal static string FormatToDb(LyricsFormat f) => f switch
    {
        LyricsFormat.Lrc => "lrc",
        LyricsFormat.Qrc => "qrc",
        LyricsFormat.Krc => "krc",
        LyricsFormat.Yrc => "yrc",
        LyricsFormat.Ttml => "ttml",
        LyricsFormat.LyricifyLines => "lyricify_lines",
        LyricsFormat.LyricifySyllable => "lyricify_syllable",
        LyricsFormat.LrclibLyricsfile => "lrclib_lyricsfile",
        _ => "plain",
    };

    internal static LyricsFormat FormatFromDb(string s) => s switch
    {
        "lrc" => LyricsFormat.Lrc,
        "qrc" => LyricsFormat.Qrc,
        "krc" => LyricsFormat.Krc,
        "yrc" => LyricsFormat.Yrc,
        "ttml" => LyricsFormat.Ttml,
        "lyricify_lines" => LyricsFormat.LyricifyLines,
        "lyricify_syllable" => LyricsFormat.LyricifySyllable,
        "lrclib_lyricsfile" => LyricsFormat.LrclibLyricsfile,
        _ => LyricsFormat.Plain,
    };

    private sealed class TrackRow
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public long? DurationMs { get; set; }
        public string MatchKey { get; set; } = "";
        public string? FirstSeenAppId { get; set; }
        public long? LastPlayedAt { get; set; }
        public long? LastManagedAt { get; set; }
        public long CreatedAt { get; set; }
        public long UpdatedAt { get; set; }

        public Track ToModel() => new(Id, Title, Artist, Album, DurationMs, MatchKey, FirstSeenAppId,
            FromMs(LastPlayedAt), FromMs(LastManagedAt), FromMs(CreatedAt)!.Value, FromMs(UpdatedAt)!.Value);
    }

    private sealed class VersionRow
    {
        public string Id { get; set; } = "";
        public string TrackId { get; set; } = "";
        public string Kind { get; set; } = "online";
        public string Source { get; set; } = "";
        public string? SourceSongId { get; set; }
        public string Name { get; set; } = "";
        public string Format { get; set; } = "plain";
        public string Content { get; set; } = "";
        public string? Translation { get; set; }
        public bool HasLineSync { get; set; }
        public bool HasWordSync { get; set; }
        public bool HasSyllable { get; set; }
        public bool HasTranslation { get; set; }
        public bool IsInstrumental { get; set; }
        public string Status { get; set; } = "cached";
        public long OffsetMs { get; set; }
        public long? FetchedAt { get; set; }
        public long CreatedAt { get; set; }
        public long UpdatedAt { get; set; }

        public LyricsVersion ToModel() => new(
            Id, TrackId,
            Kind == "local" ? VersionKind.Local : VersionKind.Online,
            Source, SourceSongId, Name, FormatFromDb(Format), Content, Translation,
            HasLineSync, HasWordSync, HasSyllable, HasTranslation, IsInstrumental,
            Status switch { "pinned" => VersionStatus.Pinned, "confirmed" => VersionStatus.Confirmed, _ => VersionStatus.Cached },
            OffsetMs, FromMs(FetchedAt), FromMs(CreatedAt)!.Value, FromMs(UpdatedAt)!.Value);
    }
}
