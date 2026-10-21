# Bugs — LyricClone

Found by `/replica-test` on 2026-10-20. BUG-001, 002, 004 and 005 were reproduced by a failing automated
test first, then fixed; BUG-003 was caught in review just before and is now covered by a test. No Git in this environment, so "fixed" means fixed in the local working tree
(build of 2026-10-20, .NET 10, Windows 11 26200).

Summary: 0 × S1, 0 × S2, 3 × S3, 2 × S4. All 5 fixed. 0 open.

### BUG-001: Local lyric file ignored once an LRCLIB version is cached

- Severity: S3
- Flow / case: F07 / F07-H1 (also F04-H1)
- Screen: S02 desktop lyrics, S09 manager
- Build: local 2026-10-20  Browser / device: Windows 11, .NET 10

Steps
1. Play a track; LRCLIB finds lyrics and caches them.
2. Settings → 应用 → set this player's source order to 本地文件夹 first.
3. Put `Artist - Title.lrc` in the lyric folder and play the track again.

Expected: the local file is used, because it ranks above LRCLIB.
Actual: the cached LRCLIB version kept winning; the local folder was never read again.
Evidence: `F07_H1_per_app_source_order_prefers_local_folder` (failed before fix)
Suspected cause: `LyricsResolver` returned any saved version picked by source order without checking whether an unsaved offline source ranked above it. Fixed with `VersionSelector.OfflineSourcesAhead` + two unit tests in `LibraryLogicTests`.
Status: fixed (local build 2026-10-20)

### BUG-002: Offset preview keeps "跟随播放" after playback stops

- Severity: S3
- Flow / case: F06 / F06-E1, F06-E2
- Screen: S14 offset dialog
- Build: local 2026-10-20  Browser / device: Windows 11, .NET 10

Steps
1. Play a track, open 管理 → 更多 → 调整偏移.
2. Stop playback in the player (or open the dialog for a track that is not playing).

Expected: "跟随播放" becomes disabled and the dialog switches to the simulated timeline.
Actual: "跟随播放" stayed selected; the preview froze on a stale position.
Evidence: `F06_E1_not_playing_track_uses_simulated_playback`, `F06_E2_playback_stops_while_preview_is_open_switches_to_simulation`
Suspected cause: playing state was computed once in the constructor. Now `UpdatePlayingState` runs on every `NowPlaying` change.
Status: fixed (local build 2026-10-20)

### BUG-003: Per-app settings lose case-insensitive matching after any settings change

- Severity: S3
- Flow / case: F07 / F07-E1
- Screen: S08 settings → 应用
- Build: local 2026-10-20  Browser / device: Windows 11, .NET 10

Steps
1. Disable a player in Settings → 应用.
2. Change any other setting (the store clones settings through JSON on every update).
3. The player reports its app ID with different casing (e.g. `Spotify.exe` vs `spotify.exe`).

Expected: the player stays disabled.
Actual: the cloned profile dictionary was case-sensitive, so the player was treated as new and enabled.
Evidence: found by code review at the end of `/replica-build`, before this test round; `F07_E1_browsers_are_ignored_by_default_and_disabled_apps_are_skipped` now guards it.
Suspected cause: JSON deserialisation replaced the dictionary and dropped the comparer. The `AppProfiles` setter now re-wraps it with `StringComparer.OrdinalIgnoreCase`.
Status: fixed (local build 2026-10-20)

### BUG-004: Lyrics editor allows saving with a blank name

- Severity: S4
- Flow / case: F04 / F04-E1
- Screen: S13 lyrics editor
- Build: local 2026-10-20  Browser / device: Windows 11, .NET 10

Steps
1. 管理 → 添加 → 从文本添加歌词….
2. Enter valid lyrics, clear the name field.

Expected: 保存 disabled with a reason.
Actual: 保存 stayed enabled; a version with an empty name could be saved.
Evidence: `F04_E1_editor_rejects_broken_ttml_and_accepts_plain_text`
Suspected cause: the name box had no `TextChanged` hook, so validation never re-ran for it.
Status: fixed (local build 2026-10-20)

### BUG-005: Theme fails to load when the app isn't the entry assembly

- Severity: S4
- Flow / case: F09 / F09-E2
- Screen: S07 settings → 常规 → 主题
- Build: local 2026-10-20  Browser / device: Windows 11, .NET 10

Steps
1. Host `LyricClone.App` from another process (test runner, future plugin host).
2. Switch theme.

Expected: brushes change.
Actual: `IOException` — relative resource URI resolved against the host assembly.
Evidence: `F09_E2_theme_switch_changes_brushes_live`
Suspected cause: `ThemeService` used a relative URI. Now uses `pack://application:,,,/LyricClone;component/Themes/...`.
Status: fixed (local build 2026-10-20)

## To check (not reproduced, needs manual run)

- Jumpy SMTC timelines from browsers: possible backward flicker of the current line.
- Mixed-DPI monitors: desktop lyrics position after moving between screens.
- Island over exclusive-fullscreen games: may be hidden by the game, not a bug if so.
- LRCLIB `Retry-After` with a real 429: covered by Infrastructure tests with a fake handler only.
