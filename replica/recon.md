# Recon map: Lyricify Fusion (Windows desktop)

Scope: full feature set — SMTC session tracking, synced lyrics (line, word, syllable, translation), all lyric views (Dynamic Lyrics Island, Desktop Lyrics, Taskbar Lyrics, Apple-Music-style fullscreen, Lyrics Stage fullscreen), Tracks & Lyrics Management, per-app configuration and delays, tray control.
For: personal use / open-source learning. Not for sale.
Stack chosen: C# / .NET 8 + WPF.
Lyrics sources for the clone: LRCLIB public API, local lyric files, lyrics embedded in audio files.
Date: 2026-10-20

Working name for the clone is `lyric-clone` until `/replica-brand`. Nothing in the clone uses the Lyricify name, logo, icons or copy.

## Sources

| # | source | URL | notes |
| --- | --- | --- | --- |
| 1 | user guide index | https://docs.lyricify.app/lyricify-fusion/ | list of views, FAQ topics |
| 2 | getting started | https://docs.lyricify.app/lyricify-fusion/getting-started/ | tray control, Windows 10.0.17763+, .NET Desktop Runtime 8 |
| 3 | tracks & lyrics management | https://docs.lyricify.app/lyricify-fusion/track-and-lyrics-management/ | most complete source: windows, buttons, version states, import, offset, filters |
| 4 | supported apps list | https://docs.lyricify.app/lyricify-fusion/supported-apps/ | per-player SMTC quality, timer fallback when no timeline |
| 5 | Apple Music FAQ | https://docs.lyricify.app/lyricify-fusion/app-faq/apple-music/ | per-app config path, session delay, timeline stabilizer |
| 6 | SMTC unavailable FAQ | https://docs.lyricify.app/lyricify-fusion/faq/smtc-unavailable/ | error state when SMTC is missing |
| 7 | Store vs GitHub version | https://docs.lyricify.app/lyricify-fusion/faq/version-info/ | store-exclusive features: custom fonts, Apple Music timeline stabilizer |
| 8 | Spotify FAQ | https://docs.lyricify.app/en/lyricify-fusion/app-faq/spotify/ | positioning vs Lyricify 4 |
| 9 | product repo README | https://github.com/WXRIW/Lyricify-App | feature list; CC BY-SA 4.0 notice on Dynamic Lyrics Island and others |
| 10 | Lyricify Lyrics Helper (Apache-2.0) | https://github.com/WXRIW/Lyricify-Lyrics-Helper | open-source lyric parsers; may be used as a dependency under its licence |
| 11 | LRCLIB API docs | https://lrclib.net/docs | public lyrics API used by the clone |

No screenshots were collected. If you want layout references, take your own screenshots of the views and save them in `replica/screens/` (reference only, never shipped).

## Core loop

Any SMTC player is playing a song → the app identifies the track, finds synced lyrics, and shows them in real time in a floating, always-on-top view.

## Screens

| ID | screen | route / how to reach | purpose | key components | states seen |
| --- | --- | --- | --- | --- | --- |
| S01 | Tray icon + context menu | always running, notification area | master control | tray icon, menu with toggles, submenu "Tracks & Lyrics Management" (Current Playback / All Tracks), Settings, Exit | normal, no session |
| S02 | Dynamic Lyrics Island | tray toggle | pill-shaped floating view at the top of the screen, current line with word highlighting | pill container, cover thumb, lyric line, expand animation | no session, paused, playing, no lyrics, loading, line-only lyrics, word lyrics |
| S03 | Desktop Lyrics | tray toggle | transparent topmost window with current line (and translation / next line) | lyric line(s), lock / unlock, drag handle, hover toolbar | locked (click-through), unlocked, no lyrics, paused |
| S04 | Taskbar Lyrics | tray toggle | lyric line drawn over the taskbar area | single line, small cover | playing, no lyrics, taskbar auto-hide |
| S05 | Apple-Music-style lyrics (fullscreen) | tray / menu | fullscreen scrolling lyrics with blurred cover background | blurred background, cover, track info, scrolling line list, word highlight, translation | playing, paused, no lyrics, performance-lag FAQ exists |
| S06 | Lyrics Stage (fullscreen) | tray / menu | alternate fullscreen view | large centered lines, animated background | playing, no lyrics |
| S07 | Settings | tray → Settings | all preferences | nav list, sections: General, Lyrics (source order, track management, auto-update saved online lyrics), Views (one page per view), Per-app configuration, Delays, About | default |
| S08 | Per-app configuration edit | S07 → Per-app configuration → app → Edit | override per player | session list, enable toggle, lyric source order, session delay (ms), Apple Music timeline smoothing | detected app, never-seen app |
| S09 | Tracks & Lyrics: Current Playback | S01 → Tracks & Lyrics Management → Current Playback | manage lyrics for the playing track; follows track changes | header with track info, "Lyric sources" list (online sources + local versions + status), version detail + lyric preview, buttons: Change version / Get lyrics, Pin / Confirm / Unpin / Unconfirm, Add lyrics, More (···), Effect preview, rules info button | no session, no track, track with no lyrics, track with versions |
| S10 | Tracks & Lyrics: All Tracks | S01 → All Tracks, or S07 → Lyrics → Manage saved lyrics → Open | browse every saved track | left track list, search (Ctrl+F), filters (usage mode, has local lyrics, saved sources), sort (recently managed, recently played, title), refresh library, session selector for rules; right side = S09 layout | empty library, filtered empty, filled |
| S11 | Search & select version | S09 → Change version → Search and select version | find the right online version | search box, results list (title, artist, album, duration), lyric preview, "Get and save this version" | searching, no results, error, results |
| S12 | Specify version by ID or link | S11 / S09 → "Specify version by ID or link" | import a precise online version | ID/link input, Parse, track info preview, Get and save | invalid input, parsed |
| S13 | Local lyrics editor | S09 → Add lyrics → Import local file / Add from text; More → Edit local lyrics | create / edit a local version | format picker (LRC, QRC, KRC, YRC, TTML, Lyricify Lines, Lyricify Syllable), text area, tab "Translation (optional)", Save | new, editing, parse error |
| S14 | Effect preview / lyric offset | S09 → Effect preview, or More → Adjust lyric offset | check sync and tune offset | live lyric preview, "Follow playback" toggle, offset (ms) input, Reset, Save offset | simulated playback (no session), following real playback |
| S15 | Selection rules popup | S09 → info button next to "Lyric sources" | explains how the version is chosen | text list of rules for the selected session | — |
| S16 | System error dialogs | startup / no player | SMTC unavailable; no player detected | message, help link | — |
| S17 | Rename version dialog | S09 → More → Rename | rename a saved version | text input, OK / Cancel | — |

## Flows

```
F01 First run: see lyrics for whatever is playing
    start app -> S01 tray icon -> play music in any SMTC player -> S02 island shows synced line
    happy path clicks: 0 (after launch)
    edge: SMTC unavailable (S16), no player, player without timeline (use internal timer, drifts on seek), no lyrics found, line-only lyrics (no word highlight)

F02 Replace wrong lyrics for the current track
    S01 -> S09 -> pick source -> Change version -> S11 search -> pick result -> Get and save -> S09 -> Pin
    happy path clicks: 6
    edge: no results, live / cover version confusion, network error, track changes while window is open (window follows)

F03 Import a precise online version by ID
    S09 -> Get lyrics / Change version -> S12 -> paste ID -> Parse -> Get and save -> S09 -> Pin or Confirm
    happy path clicks: 6
    edge: ID belongs to another source, ID not a single song

F04 Import a local lyric file
    S09 -> Add lyrics -> Import local file -> file picker -> S13 check format -> Save -> S09 select -> Use version
    happy path clicks: 6
    edge: .txt without timestamps rejected, unknown format, .xml TTML

F05 Add lyrics from pasted text (+ optional translation)
    S09 -> Add lyrics -> Add from text -> S13 choose format, paste, (Translation tab) -> Save -> Use version
    happy path clicks: 5
    edge: no timestamps, translation lines not aligned

F06 Fix a global timing offset for one version
    S09 -> More -> Adjust lyric offset -> S14 Follow playback -> change ms -> Save offset
    happy path clicks: 4
    edge: no session (simulated playback), uneven drift (offset cannot fix -> change version)

F07 Tune a player: lyric source order and session delay
    S01 -> S07 -> Per-app configuration -> app -> Edit -> S08 -> change delay / sources -> save
    happy path clicks: 5
    edge: app not yet seen, browser sessions disabled by default

F08 Manage the library
    S01 -> S10 -> search / filter / sort -> pick track -> S09 actions (refetch, auto replace, delete version)
    happy path clicks: 3
    edge: new track not visible until refresh, empty filter result

F09 Turn views on and off; lock desktop lyrics
    S01 -> toggle island / desktop / taskbar / fullscreen; S03 hover -> lock (click-through) / unlock from tray
    happy path clicks: 2
    edge: multi-monitor, DPI scaling, fullscreen game on top
```

## Components

| component | variants | states | used on |
| --- | --- | --- | --- |
| Tray menu | item, toggle item, submenu, separator | checked, unchecked, disabled | S01 |
| Lyric line renderer | line-only, word/syllable highlight, with translation, with romanization (guess) | past, current, next, upcoming | S02-S06, S09, S11, S14 |
| Floating window shell | island pill, desktop strip, taskbar strip, fullscreen | locked / click-through, hover, hidden | S02-S06 |
| Cover image | thumb, large, blurred background | loading, missing | S02, S04-S06, S09 |
| Source / version list item | online source, local version | not fetched, cached, confirmed, pinned, "in use" tag, labels: line / word / syllable / translation | S09, S10 |
| Split button | primary + dropdown (Pin / Confirm / Unpin / Unconfirm / Use version / Restore auto) | default, disabled | S09 |
| More (···) menu | refetch, edit local, rename, adjust offset, delete | — | S09 |
| Search box | normal, Ctrl+F focus | empty, typing | S10, S11 |
| Filter / sort dropdowns | — | — | S10 |
| Results list | track result row | selected, hover | S11 |
| Code-like text editor | lyric text, translation tab | valid, parse error | S13 |
| Number input (ms) | delay, offset | valid, out of range | S08, S14 |
| Toggle switch | — | on, off, disabled | S07, S08, S14 |
| Settings nav | sections | selected | S07 |
| Dialog | info, error, confirm delete | — | S12, S15, S16, S17 |

## Inferred data model

```
MediaSession   app_id (SMTC source app user model id), title, artist, album, album_artist,
               duration_ms, position_ms, last_position_update, playback_status (playing | paused | stopped),
               has_timeline (bool), thumbnail
               evidence: supported apps list (timeline / track info quality per app), getting started
               confidence: high (in memory only, not persisted)

Track          id, title, artist, album, duration_ms, first_seen_app_id,
               last_played_at, last_managed_at
               evidence: S10 search by title/artist/album, sort by recently managed / played
               confidence: high

LyricsVersion  id, track_id, kind (online | local), source (lrclib | local | embedded),
               source_song_id (online only), name, format (lrc | qrc | krc | yrc | ttml | lyricify_lines | lyricify_syllable),
               content (raw text), translation (raw text, optional),
               has_line, has_word, has_syllable, has_translation,
               status (cached | confirmed | pinned)   -- only one pinned per track
               offset_ms, fetched_at, updated_at
               evidence: S09 labels and buttons, version usage table, offset section, edit / rename
               confidence: high

AppProfile     app_id, display_name, enabled, source_order (list), session_delay_ms,
               timeline_smoothing (bool), browser (bool, disabled by default)
               evidence: Apple Music FAQ (per-app config, session delay), supported apps notes (browsers off by default)
               confidence: medium

Settings       device_delay_ms, default_source_order, auto_update_saved_online (bool),
               views: { island, desktop, taskbar, fullscreen_am, stage } each with enabled, font, size, colours,
               position, show_translation, lines_shown
               evidence: getting started ("device and app delay", "display mode of each view"), store-exclusive custom font
               confidence: medium (exact options are a guess)

ParsedLyrics   lines[ start_ms, end_ms, text, syllables[ start_ms, duration_ms, text ], translation ]
               evidence: line / word / syllable / translation labels
               confidence: high (runtime only)
```

Relationships: Track 1-n LyricsVersion; AppProfile 1-n MediaSession (runtime); at most one pinned LyricsVersion per Track.

Selection rule (from S09 docs): pinned version → confirmed online version following source order → cached / fresh result following source order and match rules. Actual version shows an "in use" tag.

## Feature matrix

See `features.csv`. Must: 29, should: 14, could: 7, skip: 8.

## Out of scope (cannot or should not be cloned)

- QQ Music, NetEase Cloud Music, Kugou lyric sources and the "import by QQ / NetEase link" variants: these use private, undocumented APIs. The clone uses LRCLIB, local files and embedded tags instead.
- Lyricify 4 public lyrics vault and Lyricify accounts: data and network the original owns.
- Player plugins (QQ Music / NetEase / AIMP / Foobar perfect-match plugins): third-party integrations outside SMTC.
- Lyricify name, logo, icons, sounds, UI copy and the "Lyricify Intelligent Engine" brand. The clone's own timeline smoothing is fine; it just is not called that.
- Lyrics content: never bundled in the clone, only fetched at runtime or supplied by you.
- Unlocking the original's Microsoft Store-only features: not done. The clone implements its own custom fonts and its own timeline smoothing.
- Dynamic Lyrics Island and Magic Strip designs are published under CC BY-SA 4.0. Building an island-style view in the clone is allowed, but you must credit "Lyricify (WXRIW)" and release that view's design under CC BY-SA 4.0. Recorded as a `must` attribution row.

## Size

Screens 17, flows 9, persisted entities 4 (+2 runtime). Hard parts:

1. Smooth, low-CPU word / syllable highlight animation in several transparent topmost WPF windows at once (and the fullscreen blurred background).
2. Reliable progress: SMTC timeline extrapolation, players without timeline (internal timer), session / device delays, and smoothing jumpy players like Apple Music.
3. Track matching and version selection against LRCLIB (fuzzy title / artist / duration), plus parsing seven lyric formats (mitigated by Lyricify-Lyrics-Helper, Apache-2.0).
4. Taskbar Lyrics: positioning over the Windows 11 taskbar without breaking it.

Size: L (a quarter for one developer). The core loop (SMTC → LRCLIB → island + desktop lyrics) is an M slice and is built first.
