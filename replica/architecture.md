# Architecture: lyric-clone (a rebuild of Lyricify Fusion's core features)

A single Windows desktop process. No server, no accounts, no payments, no email: the web-app rows of the default stack do not apply. The only network calls go to the public LRCLIB API.

## Stack

| layer | choice | why |
| --- | --- | --- |
| runtime | .NET 10 LTS, `net10.0-windows10.0.19041.0` | .NET 8 support ends 2026-11-10; .NET 10 is the current LTS. The Windows TFM gives the WinRT projection for SMTC without extra packages. Min OS 10.0.17763 kept via `SupportedOSPlatformVersion`. |
| UI | WPF + CommunityToolkit.Mvvm | Best control over transparent, topmost, click-through windows; MVVM source generators keep view models small. |
| tray | H.NotifyIcon.Wpf 2.4.x | Maintained WPF tray icon with context menus; 2.4.1 targets net10.0-windows. |
| media sessions | `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager` | The official SMTC API, the same source the original reads. |
| lyrics parsing | Lyricify.Lyrics.Helper 0.1.4 (Apache-2.0) for LRC / QRC / KRC / YRC / Lyricify Lines / Lyricify Syllable; own TTML parser | The library is open source and covers six of the seven formats; it does not support TTML. Its QQ / NetEase / Kugou providers are not used. |
| LRCLIB word sync | YamlDotNet | `/api/*` records carry a Lyricsfile YAML with `lines[].words` when `hasWordSync` is true. |
| embedded lyrics | TagLibSharp 2.3.0 | Reads USLT / SYLT (ID3), `LYRICS` (Vorbis / FLAC), `©lyr` (MP4). |
| database | SQLite via Microsoft.Data.Sqlite + Dapper | One file in `%AppData%\lyric-clone\library.db`; zero setup; plain SQL migrations. |
| settings | JSON (`System.Text.Json`) in `%AppData%\lyric-clone\settings.json` | Human-editable, easy to migrate / export. |
| HTTP | `HttpClient` via `IHttpClientFactory` + Polly retry honouring `Retry-After` | LRCLIB requires honouring `Retry-After` with jitter. |
| logging | Microsoft.Extensions.Logging + file sink | Diagnose SMTC quirks per player. |
| DI / hosting | Microsoft.Extensions.Hosting (generic host in `App.xaml.cs`) | Background services (session watcher, fetch queue) with clean shutdown. |
| tests | xUnit + FluentAssertions | Parsers, selection rules, timeline maths are pure and unit-testable. |
| packaging | `dotnet publish` self-contained, single file, x64 + arm64; optional MSIX later | Personal use: a zip is enough. |

## Solution layout

```
lyric-clone/
  src/
    LyricClone.App/            WPF exe: App, tray, windows (views), view models, DI host
    LyricClone.Core/           pure C#: models, parsers glue, selection rules, timeline clock, matching
    LyricClone.Infrastructure/ SMTC adapter, LRCLIB client, SQLite repositories, tag reader, settings store
  tests/
    LyricClone.Core.Tests/
    LyricClone.Infrastructure.Tests/
  replica/                     recon, architecture, design (never shipped)
```

Core has no reference to WPF or WinRT so it stays testable.

## Schema

Tables: 4 (+ `schema_version`). Access rules: single local user; no multi-tenant owner column. Data-layer checks only (repository methods validate input). See `replica/schema.sql`.

SQLite adaptations of the template rules:

- ids: `TEXT` UUIDs generated in C# (`Guid.NewGuid()`), not DB defaults.
- times: `INTEGER` Unix epoch milliseconds, UTC. Durations / offsets in ms.
- status fields: `CHECK` constraints.
- foreign keys on with `PRAGMA foreign_keys = ON`, `ON DELETE CASCADE` from track to versions.
- "one pinned version per track" is a partial unique index, not UI logic.
- one cached version per (track, online source): partial unique index; local versions can be many.

App profiles and view settings live in `settings.json`, not SQLite: they are small, edited as a whole, and exported together.

## Internal API (services instead of HTTP routes)

| service method | does | caller | input | output | flow |
| --- | --- | --- | --- | --- | --- |
| `ISessionWatcher.Current` / `SessionChanged` | track active SMTC session, metadata, status | views, `NowPlayingService` | — | `MediaSnapshot` events | F01 |
| `IPlaybackClock.PositionMs` | extrapolated position = last SMTC position + elapsed, minus session + device delay, smoothed | views (per frame) | snapshot, delays | ms | F01, F06, F07 |
| `ITrackResolver.ResolveAsync` | find or create `tracks` row for a snapshot (normalised title / artist / album / duration) | `NowPlayingService` | snapshot | `Track` | F01 |
| `ILyricsSelector.SelectAsync` | pinned → confirmed (source order) → cached (source order) → fetch next source | `NowPlayingService`, S09 | track, app profile | `LyricsVersion?` + reason | F01, F02 |
| `ILyricsProvider` (`LrclibProvider`, `LocalFileProvider`, `EmbeddedTagProvider`) `.GetBestAsync / SearchAsync / GetByIdAsync` | fetch candidates from one source | selector, S11, S12 | track info / query / id | candidates | F01, F02, F03 |
| `ILyricsParser.Parse` | raw text + format → `ParsedLyrics` (+ translation merge, offset applied at render) | everywhere lyrics are shown | raw, format | lines / syllables | F01, F04, F05 |
| `ILibraryRepository` | CRUD tracks / versions, pin / confirm / unpin / unconfirm, rename, offset, delete, search / filter / sort | S09, S10, S13, S14, S17 | — | — | F02-F08 |
| `ISettingsStore` | load / save / watch settings + app profiles | S07, S08, all views | — | `AppSettings` | F07, F09 |
| `IViewManager` | show / hide / lock each lyric window, restore positions per monitor | tray, settings | view id | — | F09 |

External calls (official, public, no keys):

| call | used for | notes |
| --- | --- | --- |
| `GET https://lrclib.net/api/get?track_name&artist_name&album_name&duration` | automatic best match | duration must be within ±2 s; 404 = not found |
| `GET https://lrclib.net/api/search?q=` or structured fields | S11 search | max 20 results |
| `GET https://lrclib.net/api/get/{id}` | S12 import by ID or `lrclib.net` link, refetch | stable ids |

Every request sends `User-Agent: lyric-clone/<version> (<your repo URL or email>)`. Publish / flag endpoints are not used.

Webhooks: none. Background jobs (in-process `BackgroundService`s):

- `SessionWatcherService`: subscribes to SMTC events; also polls every 1 s as a safety net for players that miss events.
- `FetchQueueService`: one LRCLIB request at a time, 300 ms between requests, honours `Retry-After` + up to 30 % jitter on 429 / 503.
- `AutoUpdateService` (could): refresh cached online versions older than N days when `auto_update_saved_online` is on, throttled the same way.

## The parts that bite

- **Timeline accuracy:** SMTC reports position only on events; extrapolate from `LastUpdatedTime`. Players without a timeline (Kugou, NetEase UWP, Foobar, AIMP) need an internal timer started at track change, which drifts on seek; say so in the UI. Smoothing: clamp backwards jumps under a threshold and ease forward corrections instead of snapping.
- **Delays:** effective position = SMTC position + elapsed − session delay − device delay + version offset. One function, unit-tested, used by every view.
- **Track matching:** SMTC metadata is messy ("Artist - Title" in the title field for PotPlayer, empty album for Apple Music, feat. suffixes). Normalise (case, width, traditional / simplified via the helper library, brackets, feat.) before lookup; include duration when known.
- **Races:** track changes while a fetch is in flight; tag every fetch with the track id and drop stale results. Current Playback window must follow track changes without losing unsaved edits (ask first).
- **Rate limits:** LRCLIB ~90 req/min identified; sequential queue + cache; never re-fetch a track that already has a version unless asked.
- **Rendering cost:** several transparent topmost windows at 60 fps. Use one shared `CompositionTarget.Rendering` tick, animate only the current line, freeze brushes, cache blurred backgrounds, drop to 30 fps when on battery.
- **Click-through + topmost:** locked desktop lyrics need `WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW`; unlock must stay reachable from the tray.
- **Taskbar lyrics:** Windows 11 taskbar has no supported extension point; draw a topmost toolwindow aligned to `SHAppBarMessage(ABM_GETTASKBARPOS)` and hide on fullscreen apps / auto-hide.
- **DPI / multi-monitor:** per-monitor v2 DPI awareness in the manifest; store window positions per monitor id.
- **File access for local / embedded lyrics:** SMTC rarely exposes the file path. Local lookup works by matching a configured lyrics folder by "Artist - Title"; embedded tags only when a path is known (some players put it in metadata). Mark this limitation in settings.
- **Encoding:** LRC files in GBK / Big5 / UTF-16; detect BOM, fall back to a charset detector, let the user pick in S13.
- **Licences:** About page must include the Apache-2.0 notice for Lyricify.Lyrics.Helper and the CC BY-SA 4.0 attribution for the island-style view.
- **Copyright of content:** no lyrics in the repo; test fixtures use made-up lines.

## Build order

1. **Vertical slice (M):** tray icon (S01) + SMTC watcher + playback clock + LRCLIB `/api/get` + LRC parser + SQLite cache (`tracks`, `lyrics_versions`) + Desktop Lyrics (S03, line-only, unlocked). Proves SMTC, network, DB and a topmost window end to end.
2. **Must-haves:**
   - Views: Dynamic Lyrics Island (S02), word / syllable highlight, lock / click-through, per-view display options, translation lines.
   - Lyrics: TTML parser, Lyricsfile YAML word sync, matching normaliser, selection rules (pinned / confirmed / source order).
   - Management: Current Playback (S09), All Tracks (S10), Search & select (S11), Pin / Confirm split button, import local file + add from text (S13), offset (S14).
   - Settings (S07), per-app configuration (S08), session / device delay, source order, settings persistence, error states (S16), About with licences.
3. **Should-haves:** QRC / KRC / YRC / Lyricify formats via the helper library, local folder and embedded tag providers, LRCLIB ID / link import (S12), edit / rename / refetch / replace / delete, effect preview with follow playback, timeline smoothing, browser sessions off by default, custom fonts, Taskbar Lyrics (S04), Apple-Music-style fullscreen (S05), multi-monitor, single instance + autostart.
4. **Could-haves:** Lyrics Stage (S06), rules popup (S15), auto-update saved lyrics, config import / export, zh-CN + en localisation, update checker, keyboard navigation.
5. **Fixes from replica-entrepreneur:** after it runs.
