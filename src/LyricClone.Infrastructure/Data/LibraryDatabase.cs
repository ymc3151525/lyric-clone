using System.Reflection;
using Microsoft.Data.Sqlite;

namespace LyricClone.Infrastructure.Data;

/// <summary>Opens the local SQLite library and applies migrations on first use.</summary>
public sealed class LibraryDatabase
{
    private const string SchemaResource = "LyricClone.Infrastructure.Data.schema.sql";
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialised;

    public LibraryDatabase(string databasePath)
    {
        DatabasePath = databasePath;
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = databasePath == ":memory:" ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate,
            Cache = databasePath == ":memory:" ? SqliteCacheMode.Shared : SqliteCacheMode.Default,
            ForeignKeys = true,
        };
        _connectionString = builder.ToString();
    }

    public string DatabasePath { get; }

    public static string DefaultPath => Path.Combine(DataPaths.Root, "library.db");

    public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
    {
        await EnsureCreatedAsync(ct);
        return await OpenRawAsync(ct);
    }

    public async Task EnsureCreatedAsync(CancellationToken ct = default)
    {
        if (_initialised)
        {
            return;
        }

        await _initGate.WaitAsync(ct);
        try
        {
            if (_initialised)
            {
                return;
            }

            if (DatabasePath != ":memory:")
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(DatabasePath));
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }

            await using var conn = await OpenRawAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = ReadSchema();
            await cmd.ExecuteNonQueryAsync(ct);
            await ApplyMigrationsAsync(conn, ct);
            _initialised = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    private async Task<SqliteConnection> OpenRawAsync(CancellationToken ct)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 3000;";
        await pragma.ExecuteNonQueryAsync(ct);
        return conn;
    }

    private static async Task ApplyMigrationsAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var versionCmd = conn.CreateCommand();
        versionCmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version";
        long version = Convert.ToInt64(await versionCmd.ExecuteScalarAsync(ct));
        if (version < 2)
        {
            await using var migrate = conn.CreateCommand();
            migrate.CommandText = Migration002;
            await migrate.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>Existing libraries were created with a source CHECK that rejected player-cache ids.</summary>
    private const string Migration002 = """
        PRAGMA foreign_keys = OFF;
        CREATE TABLE lyrics_versions_v2 (
          id               TEXT    NOT NULL PRIMARY KEY,
          track_id         TEXT    NOT NULL REFERENCES tracks (id) ON DELETE CASCADE,
          kind             TEXT    NOT NULL CHECK (kind IN ('online', 'local')),
          source           TEXT    NOT NULL CHECK (source IN ('lrclib', 'local_file', 'embedded', 'user', 'qq_cache', 'netease_cache', 'kugou_cache')),
          source_song_id   TEXT,
          name             TEXT    NOT NULL DEFAULT '',
          format           TEXT    NOT NULL CHECK (format IN
                             ('lrc', 'qrc', 'krc', 'yrc', 'ttml', 'lyricify_lines', 'lyricify_syllable', 'lrclib_lyricsfile', 'plain')),
          content          TEXT    NOT NULL,
          translation      TEXT,
          has_line_sync    INTEGER NOT NULL DEFAULT 0 CHECK (has_line_sync IN (0, 1)),
          has_word_sync    INTEGER NOT NULL DEFAULT 0 CHECK (has_word_sync IN (0, 1)),
          has_syllable     INTEGER NOT NULL DEFAULT 0 CHECK (has_syllable IN (0, 1)),
          has_translation  INTEGER NOT NULL DEFAULT 0 CHECK (has_translation IN (0, 1)),
          is_instrumental  INTEGER NOT NULL DEFAULT 0 CHECK (is_instrumental IN (0, 1)),
          status           TEXT    NOT NULL DEFAULT 'cached' CHECK (status IN ('cached', 'confirmed', 'pinned')),
          offset_ms        INTEGER NOT NULL DEFAULT 0 CHECK (offset_ms BETWEEN -600000 AND 600000),
          fetched_at       INTEGER,
          created_at       INTEGER NOT NULL,
          updated_at       INTEGER NOT NULL,
          CHECK (kind = 'local' OR source_song_id IS NOT NULL),
          CHECK (kind = 'online' OR status IN ('cached', 'pinned'))
        );
        INSERT INTO lyrics_versions_v2 (
          id, track_id, kind, source, source_song_id, name, format, content, translation,
          has_line_sync, has_word_sync, has_syllable, has_translation, is_instrumental,
          status, offset_ms, fetched_at, created_at, updated_at)
        SELECT
          id, track_id, kind, source, source_song_id, name, format, content, translation,
          has_line_sync, has_word_sync, has_syllable, has_translation, is_instrumental,
          status, offset_ms, fetched_at, created_at, updated_at
        FROM lyrics_versions;
        DROP TABLE lyrics_versions;
        ALTER TABLE lyrics_versions_v2 RENAME TO lyrics_versions;
        CREATE INDEX IF NOT EXISTS ix_lyrics_versions_track_id ON lyrics_versions (track_id);
        CREATE INDEX IF NOT EXISTS ix_lyrics_versions_status   ON lyrics_versions (track_id, status);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_lyrics_versions_one_pinned
          ON lyrics_versions (track_id) WHERE status = 'pinned';
        CREATE UNIQUE INDEX IF NOT EXISTS ux_lyrics_versions_one_per_online_source
          ON lyrics_versions (track_id, source) WHERE kind = 'online';
        CREATE UNIQUE INDEX IF NOT EXISTS ux_lyrics_versions_one_per_player_cache
          ON lyrics_versions (track_id, source) WHERE source IN ('qq_cache', 'netease_cache', 'kugou_cache');
        INSERT OR IGNORE INTO schema_version (version, applied_at)
        VALUES (2, CAST(strftime('%s', 'now') AS INTEGER) * 1000);
        PRAGMA foreign_keys = ON;
        """;

    private static string ReadSchema()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(SchemaResource)
            ?? throw new InvalidOperationException("Embedded schema.sql is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
