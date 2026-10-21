# Build log: lyric-clone

No git on this machine, so there are no per-screen commits yet. Install Git and run `git init` before the next build round.

| ID | date | status | missing | harder than expected |
| --- | --- | --- | --- | --- |
| shell | 2026-10-20 | done | — | H.NotifyIcon 2.4.x only targets .NET 10 (drove the .NET 8 → 10 switch); `LibraryImport` needs `AllowUnsafeBlocks` |
| design primitives | 2026-10-20 | done | Lucide icons (own simple glyphs for now), SplitButton, NumberBox, InfoBar as a control | WPF draws CornerRadius 999 as an ellipse; island uses an explicit 18 px radius token |
| vertical slice | 2026-10-20 | done | needs a check with real players (see below) | — |
| S01 tray | 2026-10-20 | done | taskbar toggle disabled until S04 | — |
| S02 island | 2026-10-20 | done | check over a real player and on a second monitor | SizeToContent + recentre on every size change |
| S03 desktop lyrics | 2026-10-20 | done | colour pickers | click-through needs Win32 styles; unlock lives in the tray menu |
| S07 settings + S08 per app | 2026-10-20 | done | colour pickers; autostart; auto-update setting | WPF 9+ has its own `System.Windows.ThemeMode`, so the settings enum needs an alias |
| S07 lyrics + island extras | 2026-10-20 | done | QQ / NetEase / Kugou sources still skip (private APIs); romaji is kana-only | screenshots in replica/screens/S07-lyrics-content.png and S07-island-*.png |
| S09 Current Playback / S10 All Tracks | 2026-10-20 | done | split buttons are plain buttons + menus | — |
| S11 search · S12 by ID · S13 editor · S14 offset · S15 rules · S17 rename | 2026-10-20 | done | S15 / S17 are small code-built dialogs | — |
| theme switching | 2026-10-20 | done | — | Colors dictionary swapped at runtime; "System" follows `AppsUseLightTheme` |
| S05 S06 | 2026-10-20 | stub | everything except the window | — |
| S04 S16 | — | not started | — | — |

## Vertical slice

SMTC session → `PlaybackClock` → `LyricsResolver` (SQLite cache → local folder → LRCLIB) → LRC / TTML parser → `DesktopLyricsWindow` / `IslandWindow` with word fill.

## Checked this round

- 58 unit tests still pass. Build has 0 warnings.
- Screenshots in `replica/clone-screens/`, rendered against a throwaway library with made-up tracks and made-up lyrics:
  `all.png` (S10), `now.png` (S09 empty state), `search.png` (S11), `byid.png` (S12), `editor.png` (S13), `offset.png` (S14), `island.png` (S02 idle), `settings*.png` (S07 / S08, light and dark).
- To regenerate (Debug build only):
  `$env:LYRIC_CLONE_DATA_DIR = "$env:TEMP\lyric-clone-shots"; LyricClone.exe --window-screenshot <all|now|search|byid|editor|offset|island|settings|settings-views|settings-apps|settings-about|settings-dark> <out.png>`

Not checked yet: clicking through the manager actions against a real playing track, the island over a real player, Apple Music's jumpy timeline, players without a timeline, two monitors. That is `/replica-test`'s job, and it needs you at the screen.

## Fixes made along the way

- `AppSettings.AppProfiles` lost its case-insensitive comparer after the JSON clone in `UpdateAsync`; the setter now re-wraps it.
- Desktop lyrics did not fall back to the default font when the font setting was cleared, and "move back to default position" had no effect until restart.
- `ILyricsResolver` gained `FetchAndSaveAsync` (one source, used by "获取歌词" / auto replace) and `SaveCandidateAsync` (used by S11 / S12).

## Test round (/replica-test, 2026-10-20)

- New project `tests/LyricClone.Flows.Tests`: 28 flow tests drive the real WPF windows (fake SMTC sessions, fake LRCLIB, temp data dir). Plan and results in `replica/test-plan.md`; bugs in `replica/bugs.md`.
- `dotnet test LyricClone.sln`: Core 42, Infrastructure 18, Flows 28, all pass; Flows run three times in a row with no flakes.
- Fixed with a failing test first: local folder ignored after LRCLIB cache (resolver + `VersionSelector.OfflineSourcesAhead`), offset dialog "跟随播放" after stop, editor blank name, theme pack URI.
- Test harness: buttons are pressed through UI Automation `Invoke` so `IsCancel` / `IsDefault` behave like a real click; `WhenDialog` waits until the dialog has actually closed.

## Gap round (2026-10-20)

Closed the five items from `parity.md`. `dotnet test LyricClone.sln`: Core 57, Infrastructure 29,
Flows 37, all pass. `parity.py` (Python 3.15 at `D:\daily_tool\Python315\python.exe`): **90.3 / 100**,
must-haves 28 of 28. `imgdiff.py` is installed; there are still no original screenshots to compare.

- S16 `PlayerHintDialog`: no player / all ignored / SMTC down. 10 s delay; skipped on `--autostart`.
- Per-view colours: swatches + hex + 默认, live on desktop / island / fullscreen.
- Embedded lyrics: ID3v2 USLT/SYLT, FLAC Vorbis LYRICS, MP4 `©lyr`; music-folder index.
- S05 `FullscreenLyricsWindow`: all lines, current line anchored, blurred SMTC cover, Esc.
- Start with Windows: HKCU Run key from Settings → 常规; repairs the path if the exe moved.

## Settings screenshot round (2026-10-20)

User supplied Lyricify Fusion's General settings. Saved as `replica/screens/S07.png`.
`imgdiff.py` vs old clone `settings.png`: layout 23.9 (different nav / cards — on purpose).

Built from that screenshot's jobs, not its look:

- Language selector; autostart already present; theme labelled 颜色模式.
- S04 taskbar lyrics strip (`TaskbarLayout` + `SHAppBarMessage`); tray toggle.
- Global hotkeys page; Support page; auto-update of stale LRCLIB cache (7 days).
- `dotnet test`: Core 66, Infrastructure 29, Flows 40. `parity.py`: **93.4**, must-haves 28/28.

## Missing-features round (2026-10-20)

Closed the leftover list from `parity.md` (except multi-monitor, which needs two screens).

- QRC / KRC / YRC / Lyricify Lines / Syllable via `Lyricify.Lyrics.Helper` 0.2.0. Searchers unused.
- S06 `StageWindow` replaces the stub: centred oversized line, cover-sampled gradient, Esc / F11 / double-click.
- Update checker on About: honest "no feed" unless the user pastes a JSON URL.
- Timeline smoothing: ignore ±80 ms noise; 1200 ms jitter / ease windows; 700 ms ease.
- Stage font / colours in Settings → 视图; Apache-2.0 notice for the helper.
- `dotnet test`: Core 72, Infrastructure 39, Flows 42.

## Notes for the next round

- `Microsoft.Data.Sqlite` pinned to 10.0.13: 10.0.0 pulled `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 with a known high-severity advisory (GHSA-2m69-gcr7-jv3q); 10.0.13 brings 2.1.13.
- LRCLIB `User-Agent` is still a placeholder in `App.UserAgent`; put your own contact (repo URL or email) there before regular use, as the LRCLIB docs ask.
- Must-haves: all done.
- Should-haves left: multi-monitor check on two real screens.
