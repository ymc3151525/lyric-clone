using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;
using LyricClone.Infrastructure.Data;
using Microsoft.Data.Sqlite;

namespace LyricClone.Infrastructure.Tests;

public sealed class LibraryDatabaseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lyric-clone-db-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Migrates_old_source_check_so_player_cache_can_be_saved()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "library.db");
        await using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE schema_version (version INTEGER NOT NULL PRIMARY KEY, applied_at INTEGER NOT NULL);
                INSERT INTO schema_version VALUES (1, 0);
                CREATE TABLE tracks (
                  id TEXT NOT NULL PRIMARY KEY, title TEXT NOT NULL, artist TEXT NOT NULL DEFAULT '',
                  album TEXT NOT NULL DEFAULT '', duration_ms INTEGER, match_key TEXT NOT NULL,
                  first_seen_app_id TEXT, last_played_at INTEGER, last_managed_at INTEGER,
                  created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL);
                CREATE TABLE lyrics_versions (
                  id TEXT NOT NULL PRIMARY KEY,
                  track_id TEXT NOT NULL REFERENCES tracks (id) ON DELETE CASCADE,
                  kind TEXT NOT NULL CHECK (kind IN ('online', 'local')),
                  source TEXT NOT NULL CHECK (source IN ('lrclib', 'local_file', 'embedded', 'user')),
                  source_song_id TEXT, name TEXT NOT NULL DEFAULT '',
                  format TEXT NOT NULL DEFAULT 'lrc',
                  content TEXT NOT NULL, translation TEXT,
                  has_line_sync INTEGER NOT NULL DEFAULT 0, has_word_sync INTEGER NOT NULL DEFAULT 0,
                  has_syllable INTEGER NOT NULL DEFAULT 0, has_translation INTEGER NOT NULL DEFAULT 0,
                  is_instrumental INTEGER NOT NULL DEFAULT 0,
                  status TEXT NOT NULL DEFAULT 'cached',
                  offset_ms INTEGER NOT NULL DEFAULT 0,
                  fetched_at INTEGER, created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL);
                INSERT INTO tracks VALUES ('t1','Sample Song','Sample Band','',200000,'k',null,null,null,1,1);
                INSERT INTO lyrics_versions (id, track_id, kind, source, source_song_id, name, format, content, status, created_at, updated_at)
                VALUES ('v1','t1','online','lrclib','1','old','lrc','[00:01.00]sample','cached',1,1);
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var repo = new LibraryRepository(new LibraryDatabase(path));
        var saved = await repo.SaveCandidateAsync("t1", new LyricsCandidate(
            LyricsSources.QqCache, "x.qrc", "Sample Song", "Sample Band", "", 200_000,
            LyricsFormat.Qrc, "[0,100]one(0,100)", null, false, true), new LyricsFlags(true, true, false, false));

        Assert.Equal(LyricsSources.QqCache, saved.Source);
        var versions = await repo.GetVersionsAsync("t1");
        Assert.Contains(versions, v => v.Source == LyricsSources.Lrclib && v.Content.Contains("sample"));
        Assert.Contains(versions, v => v.Source == LyricsSources.QqCache);
    }
}
