-- lyric-clone local library (SQLite). Migration 001.
-- Run with PRAGMA foreign_keys = ON on every connection.
-- Times are Unix epoch milliseconds (UTC). Durations and offsets are milliseconds.

PRAGMA journal_mode = WAL;

CREATE TABLE IF NOT EXISTS schema_version (
  version     INTEGER NOT NULL PRIMARY KEY,
  applied_at  INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS tracks (
  id                TEXT    NOT NULL PRIMARY KEY,
  title             TEXT    NOT NULL CHECK (length(title) > 0),
  artist            TEXT    NOT NULL DEFAULT '',
  album             TEXT    NOT NULL DEFAULT '',
  duration_ms       INTEGER          CHECK (duration_ms IS NULL OR duration_ms > 0),
  match_key         TEXT    NOT NULL,          -- normalised title|artist|album, used for lookup
  first_seen_app_id TEXT,
  last_played_at    INTEGER,
  last_managed_at   INTEGER,
  created_at        INTEGER NOT NULL,
  updated_at        INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_tracks_match_key       ON tracks (match_key);
CREATE INDEX IF NOT EXISTS ix_tracks_last_played_at  ON tracks (last_played_at DESC);
CREATE INDEX IF NOT EXISTS ix_tracks_last_managed_at ON tracks (last_managed_at DESC);
CREATE INDEX IF NOT EXISTS ix_tracks_title           ON tracks (title COLLATE NOCASE);

-- Which player session a track was seen in; drives per-app rules in All Tracks.
CREATE TABLE IF NOT EXISTS track_sessions (
  track_id     TEXT    NOT NULL REFERENCES tracks (id) ON DELETE CASCADE,
  app_id       TEXT    NOT NULL,
  last_seen_at INTEGER NOT NULL,
  PRIMARY KEY (track_id, app_id)
);

CREATE INDEX IF NOT EXISTS ix_track_sessions_app_id ON track_sessions (app_id);

CREATE TABLE IF NOT EXISTS lyrics_versions (
  id               TEXT    NOT NULL PRIMARY KEY,
  track_id         TEXT    NOT NULL REFERENCES tracks (id) ON DELETE CASCADE,
  kind             TEXT    NOT NULL CHECK (kind IN ('online', 'local')),
  source           TEXT    NOT NULL CHECK (source IN ('lrclib', 'local_file', 'embedded', 'user', 'qq_cache', 'netease_cache', 'kugou_cache')),
  source_song_id   TEXT,                       -- LRCLIB record id for online versions
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
  CHECK (kind = 'online' OR status IN ('cached', 'pinned'))   -- local versions are never "confirmed"
);

CREATE INDEX IF NOT EXISTS ix_lyrics_versions_track_id ON lyrics_versions (track_id);
CREATE INDEX IF NOT EXISTS ix_lyrics_versions_status   ON lyrics_versions (track_id, status);

-- At most one pinned version per track.
CREATE UNIQUE INDEX IF NOT EXISTS ux_lyrics_versions_one_pinned
  ON lyrics_versions (track_id) WHERE status = 'pinned';

-- One saved version per online source per track ("Change version" replaces it).
CREATE UNIQUE INDEX IF NOT EXISTS ux_lyrics_versions_one_per_online_source
  ON lyrics_versions (track_id, source) WHERE kind = 'online';

CREATE UNIQUE INDEX IF NOT EXISTS ux_lyrics_versions_one_per_player_cache
  ON lyrics_versions (track_id, source) WHERE source IN ('qq_cache', 'netease_cache', 'kugou_cache');

-- Remembered misses so the app does not hammer LRCLIB for tracks it has no lyrics for.
CREATE TABLE IF NOT EXISTS lookup_misses (
  track_id    TEXT    NOT NULL REFERENCES tracks (id) ON DELETE CASCADE,
  source      TEXT    NOT NULL,
  missed_at   INTEGER NOT NULL,
  PRIMARY KEY (track_id, source)
);

INSERT OR IGNORE INTO schema_version (version, applied_at)
VALUES (1, CAST(strftime('%s', 'now') AS INTEGER) * 1000);
