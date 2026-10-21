# Test plan — LyricClone

Built from the flows in `recon.md` (F01–F09). Tested against our own build only, never the original
or its servers. LRCLIB and the media session are faked in automated runs; every lyric line in the
tests is made up.

How to run: `dotnet test LyricClone.sln` (Core 72, Infrastructure 39, Flows 42; all green on 2026-10-20).
Flow tests live in `tests/LyricClone.Flows.Tests`; they drive the real WPF windows on an STA thread.

Types: H = happy path, E = edge case, N = negative input. Auto = name of the automated test, or
`manual` (see checklist at the bottom).

| case | flow | type | steps | expected | auto | result |
|---|---|---|---|---|---|---|
| F01-H1 | F01 | H | Player starts a track LRCLIB knows; play it a second time | Lyrics shown, saved as cached LRCLIB version; second play makes no network call | `F01_H1_playing_track_resolves_from_lrclib_and_is_cached` | pass |
| F01-E1 | F01 | E | Track LRCLIB doesn't have | "没有找到歌词" state; miss remembered, no repeat request | `F01_E1_no_lyrics_found_shows_not_found_and_remembers_the_miss` | pass |
| F01-E2 | F01 | E | LRCLIB returns 503 | Error state, app keeps running, no saved version | `F01_E2_network_error_is_reported_not_thrown` | pass |
| F01-E3 | F01 | E | LRCLIB marks track instrumental | "纯音乐" state, no lines | `F01_E3_instrumental_record_is_shown_as_instrumental` | pass |
| F01-E4 | F01 | E | Title/artist with emoji and accents | Query sent intact; lyrics resolve | `F01_E4_emoji_and_accents_in_metadata_still_match` | pass |
| F01-E5 | F01 | E | Track changes while first lookup is in flight | Only the newest track's lyrics are shown | `F01_E5_track_change_mid_resolve_shows_the_new_track` | pass |
| F01-E6 | F01 | E | Start with no player; check "不再提示"; start again | S16 dialog once; setting saved; not shown again | `F01_E6_no_player_hint_shows_once_and_dont_show_again_is_saved` | pass |
| F01-E7 | F01 | E | Only ignored/browser sessions exist | Dialog names them; "打开按应用设置" opens that page | `F01_E7_all_sessions_ignored_hint_names_them_and_opens_per_app_settings` | pass |
| F01-E8 | F01 | E | SMTC unavailable with hints off; then a real playing track | SMTC dialog still shown; playing track shows nothing | `F01_E8_smtc_unavailable_is_shown_even_when_hints_are_off_and_playing_shows_nothing` | pass |
| F01-M1 | F01 | E | Real Spotify / Apple Music / foobar2000, seek around, jumpy timeline | Line follows playback within smoothing | manual | — |
| F02-H1 | F02 | H | Manager → 更换 → 搜索并选择版本 → pick result with 60 s length gap | Duration warning shown; saved version pinned and playing | `F02_H1_search_pick_another_version_then_pin_it` | pass |
| F02-E1 | F02 | E | Search with no results | Empty-results text, save disabled | `F02_E1_no_search_results_shows_hint_and_save_stays_disabled` | pass |
| F02-E2 | F02 | E | Manager open on "正在播放", track changes, then playback stops | Window follows the new track, then shows the empty panel | `F02_E2_window_follows_track_changes` | pass |
| F02-M1 | F02 | H | Real LRCLIB search from the dialog (a few requests, by hand) | Results with durations; preview loads | manual | — |
| F03-H1 | F03 | H | 通过 ID 或链接 → paste `https://lrclib.net/api/get/4242` → 获取 → 保存 | Version 4242 saved and in use | `F03_H1_specify_version_by_lrclib_link` | pass |
| F03-N1 | F03 | N | Paste garbage, then an unknown ID | "无法识别…" without a network call; "没有 ID 为 … 的记录" | `F03_N1_unknown_id_and_garbage_input_show_messages` | pass |
| F04-H1 | F04 | H | Put `Artist - Title.lrc` in the lyric folder, play the track (local-first order) | Local file wins over cached LRCLIB | `F07_H1_per_app_source_order_prefers_local_folder` | pass |
| F04-H2 | F04 | H | MP3 in the music folder with a made-up USLT tag; play the matching track | Embedded version used; LRCLIB not called | `F04_H2_embedded_lyrics_in_music_folder_are_used_before_lrclib` | pass |
| F04-E1 | F04 | N | Editor: blank name, broken TTML, then plain text | Save disabled with reason; enabled when valid | `F04_E1_editor_rejects_broken_ttml_and_accepts_plain_text` | pass |
| F04-E2 | F04 | E | LRCLIB already cached; add a tagged FLAC; play again | Embedded wins on the next play | `F04_E2_audio_file_added_later_wins_over_cached_lrclib_on_next_play` | pass |
| F04-M1 | F04 | H | 导入本地歌词文件… via the file picker (.lrc, .ttml, wrong extension) | Imports good files, rejects others with a message | manual | — |
| F04-M2 | F04 | H | Your own MP3/FLAC with real embedded lyrics | Embedded version found from the music folder | manual | — |
| F05-H1 | F05 | H | 从文本添加 with translation → saved → 使用此版本 → 取消固定 | Not in use until chosen; then in use; unpin returns to automatic pick | `F05_H1_add_from_text_is_not_used_until_use_version_then_restore_auto` | pass |
| F06-H1 | F06 | H | Offset dialog: +500, +50, 保存 | Offset 550 ms stored; first line starts at 1550 ms; label "偏移 +550 ms" | `F06_H1_offset_saved_from_preview_shifts_playing_lyrics` | pass |
| F06-E1 | F06 | E | Open offset dialog for a track that isn't playing | Follow-playback disabled; simulated timeline selected | `F06_E1_not_playing_track_uses_simulated_playback` | pass |
| F06-E2 | F06 | E | Playback stops while dialog is open | Falls back to simulated timeline | `F06_E2_playback_stops_while_preview_is_open_switches_to_simulation` | pass |
| F07-H1 | F07 | H | Per-app order with local first | Local version used even after LRCLIB was cached | `F07_H1_per_app_source_order_prefers_local_folder` | pass |
| F07-H2 | F07 | H | Change device and per-app delay while playing | Highlight shifts live, no restart | `F07_H2_session_delay_and_device_delay_shift_the_clock` | pass |
| F07-H3 | F07 | H | Settings → 应用: delay, custom order, 上移, bad input, reset | Values saved; bad input ignored; reset clears profile | `F07_H3_settings_per_app_delay_and_source_order_are_saved` | pass |
| F07-E1 | F07 | E | Browser session; disabled app with different casing; opt-in mode | Browser ignored; disabled app ignored regardless of case; opt-in only listed apps | `F07_E1_browsers_are_ignored_by_default_and_disabled_apps_are_skipped` | pass |
| F08-H1 | F08 | H | 全部曲目: search, filter, sort, delete a version (confirm) | Lists update; deleted version gone after confirm | `F08_H1_all_tracks_search_filter_and_delete` | pass |
| F08-E1 | F08 | E | New track played while manager open → 刷新曲目库 | New track listed | `F08_E1_new_track_appears_after_refresh` | pass |
| F08-E2 | F08 | E | 获取歌词 for a track LRCLIB lacks | Info bar "没有找到歌词" | `F08_E2_fetch_for_track_with_no_lyrics_shows_message` | pass |
| F09-H1 | F09 | H | Desktop lyrics on, lock | Window gets WS_EX_TRANSPARENT; shows current line | `F09_H1_desktop_lyrics_lock_makes_window_click_through_and_unlock_restores` | pass |
| F09-H2 | F09 | H | Island on: idle, playing, auto-hide off | Tool window, centred, hides when idle | `F09_H2_island_is_a_tool_window_hides_when_idle_and_shows_when_playing` | pass |
| F09-E1 | F09 | E | Island play/pause button | Forwarded to the session | `F09_E1_island_play_pause_button_reaches_the_player` | pass |
| F09-E2 | F09 | E | Switch theme dark → light → system | Colour dictionary swapped each time | `F09_E2_theme_switch_changes_brushes_live` | pass |
| F09-E3 | F09 | E | Desktop font cleared / not installed | Falls back to the sans font | `F09_E3_desktop_font_cleared_falls_back_to_default` | pass |
| F09-E4 | F09 | E | Settings → 视图: swatch, invalid hex, typed hex, 默认; island sung colour | Desktop/island colours apply live; bad input ignored; default restores theme | `F09_E4_colours_from_settings_apply_live_to_desktop_and_island_and_default_restores_theme` | pass |
| F09-H3 | F09 | H | Tray → 全屏歌词 while a track with cover is playing | All lines listed; current highlighted; blurred cover; play/pause forwarded | `F09_H3_fullscreen_lists_all_lines_highlights_current_and_blurs_the_cover` | pass |
| F09-E5 | F09 | E | Fullscreen with no lyrics and no cover | Empty message; cover placeholder; no blur | `F09_E5_fullscreen_without_lyrics_or_cover_shows_message_and_placeholder` | pass |
| F09-H4 | F09 | H | Settings → 常规: 开机时启动 on, then off; move the exe | Run key written, repaired, then removed | `Autostart_toggle_writes_run_key_and_repairs_a_moved_exe` | pass |
| F09-H5 | F09 | H | Taskbar lyrics on a bottom bar; a line is playing | Strip sits left of the tray; current line shown | `F09_H5_taskbar_lyrics_sit_on_a_bottom_bar_and_hide_when_fullscreen` | pass |
| F09-E6 | F09 | E | Vertical taskbar | Unsupported; window hidden | `F09_E6_vertical_taskbar_is_marked_unsupported` | pass |
| F07-H4 | F07 | H | Settings: language English, auto-update on, hotkey saved, open 支持 | All four persist / show | `F07_H4_language_and_auto_update_and_hotkey_are_saved` | pass |
| F09-H6 | F09 | H | Tray → 歌词舞台 while a track is playing | Current line centred; font from settings; close button exits | `F09_H6_stage_centres_the_current_line_and_closes_on_escape` | pass |
| F09-H7 | F09 | H | Settings → 关于 → 检查更新 with no feed URL | Explains there is no published update source | `About_check_update_without_a_feed_explains_itself` | pass |
| F04-H3 | F04 | H | Parse made-up QRC / KRC / YRC / Lyricify Lines / Syllable | Timed lines (word-sync where the format has it) | `Parses_helper_formats_into_timed_lines` | pass |
| F09-M1 | F09 | E | Locked desktop lyrics: real mouse clicks pass through to the app below; unlock from tray | Clicks go through; tray unlocks | manual | — |
| F09-M2 | F09 | E | Two monitors, 100 % + 150 % DPI; drag desktop lyrics across | Stays sharp, position remembered per run | manual | — |
| F09-M3 | F09 | E | Island over a fullscreen game / video | Stays on top or hides per setting, no focus steal | manual | — |

## Manual checklist

Run these by hand on your own machine; they need real hardware, real players or OS dialogs.

- [ ] Real players: Spotify, Apple Music (Windows), foobar2000 or MusicBee with SMTC. Seek, pause, skip.
- [ ] A player whose timeline jumps (some browsers' SMTC): lines don't flicker backward.
- [ ] A player that reports no timeline: clone shows lyrics without highlight instead of breaking.
- [ ] Tray menu: every item works; "退出" leaves no process behind.
- [ ] Locked desktop lyrics: mouse clicks really pass through; unlock from the tray.
- [ ] Multi-monitor and mixed DPI (desktop lyrics + island).
- [ ] Island over a fullscreen game or video.
- [ ] File picker (导入本地歌词文件…) and folder picker (Settings → 歌词 → 本地文件夹).
- [ ] Embedded lyrics in real MP3/FLAC files you own (put them in the music folder or pick that folder in Settings → 歌词).
- [ ] Real LRCLIB search and fetch: a handful of requests by hand, never a loop.
- [ ] Fullscreen lyrics over a real player with cover art; Esc exits; blur toggle.
- [ ] Lyrics Stage: cover-tinted gradient stays dark; Esc / F11 / double-click exit.
- [ ] Paste a QRC / KRC / YRC file you own into 添加本地歌词 (no private APIs).
- [ ] 开机时启动: log out and in; confirm the tray icon appears and the no-player dialog does not.
- [ ] Taskbar lyrics on a real bottom taskbar; hide in a fullscreen game; vertical taskbar shows the settings hint.
- [ ] Global hotkey for lock desktop while another app is focused.
