# Components: lyric-clone

Every component from `replica/recon.md`, as WPF control templates in `src/LyricClone.App/Themes/Controls.xaml` (management UI) and custom controls in `src/LyricClone.App/Lyrics/` (lyric rendering). Tokens only, never raw hex. Icons: Lucide (ISC licence) path geometries in `Themes/Icons.xaml`. Copy is written fresh; see "Copy" at the end. Placeholder lyric text in the gallery is made up, never real song lyrics.

Shared rules:

- Every interactive control has a visible focus state: 2px `Brush.Accent` ring, 2px outside the control, radius = control radius + 2. Use `FocusVisualStyle`, keyboard only.
- `AutomationProperties.Name` on every icon-only button, and a `ToolTip` with the same text.
- Disabled: 40 % opacity, no hover, `IsHitTestVisible` stays true for the tooltip explaining why.
- Theme follows Windows app mode (light / dark) unless overridden in settings; switching swaps `Colors.Light.xaml` / `Colors.Dark.xaml`.
- Reduced motion (`SystemParameters.ClientAreaAnimation == false` or the in-app toggle): no easing, no glow animation, line swaps are instant.

## Management UI

```
Button
  variants  primary, secondary, subtle (ghost), danger
  sizes     sm 28px, md 32px (default), lg 40px
  states    default, hover (+HoverOverlay), pressed (+PressedOverlay), focus-visible, disabled, busy (spinner left of label, label kept)
  tokens    primary: bg Accent, fg OnAccent; secondary: bg Surface, border Border, fg Text; subtle: transparent, fg Text; danger: bg Danger, fg OnAccent
            radius Md, font Body / 600, padding Pad.Control
  a11y      real Button, Enter / Space activate, busy sets AutomationProperties.ItemStatus = "busy"
  used on   S07-S17
```

```
SplitButton
  variants  primary + dropdown chevron
  states    as Button; dropdown open; menu items disabled when not applicable
  content   main action depends on version state:
            online cached     -> main "Pin"      menu: Confirm
            online confirmed  -> main "Pin"      menu: Remove confirmation
            online pinned     -> main "Unpin"    menu: Remove confirmation
            local not pinned  -> main "Use this version"
            local pinned      -> main "Back to automatic"
  a11y      two tab stops (main, chevron); Alt+Down opens the menu
  used on   S09, S10
```

```
IconButton
  sizes     28px, 32px
  states    as Button subtle; toggled (bg AccentSubtle, icon Accent)
  a11y      Name + ToolTip required
  used on   S03 hover toolbar, S09 More (···), S09 rules info, S10 refresh
```

```
MenuFlyout / ContextMenu
  item      icon (optional, 16px), label, shortcut text (TextMuted), check mark for toggles, submenu chevron
  states    hover, focused, checked, disabled
  tokens    bg Surface, border Border, radius Md, Shadow.Pop, item height 32
  a11y      arrow keys, Esc closes, access keys on top-level items
  used on   S01 tray menu, S09 More, S09 Add lyrics, split button menu
```

```
TextBox / SearchBox
  states    default, hover, focused (border Accent 2px), error (border Danger + message below), disabled, read-only
  tokens    bg Surface, border BorderInput 1px, radius Md, height 32
  search    leading search icon, clear (x) button when not empty, Ctrl+F focuses it in S10
  a11y      label via AutomationProperties.LabeledBy; error text announced (LiveSetting = Assertive)
  used on   S10, S11, S12, S17
```

```
NumberBox (ms)
  content   TextBox + unit suffix "ms" + spin buttons; Up / Down = ±10, Shift = ±100, PageUp / PageDown = ±1000
  states    as TextBox; out of range clamps on blur and shows the range in the error line
  ranges    session delay −5000..5000, device delay −2000..2000, version offset −600000..600000
  used on   S07, S08, S14
```

```
ToggleSwitch
  states    off, on, hover, focus-visible, disabled
  tokens    track off: border BorderInput; track on: bg Accent; thumb Surface / OnAccent
  a11y      CheckBox semantics, Space toggles, label clickable
  used on   S07, S08, S14
```

```
ComboBox / Dropdown
  states    closed, open, hover, focus-visible, disabled
  used on   S07 (theme, font, view position), S10 filters and sort, S13 lyric format
```

```
ReorderableList (lyric source order)
  item      drag handle, source name, enable toggle
  states    default, dragging (Shadow.Pop, 2px Accent insertion line), focus-visible
  a11y      Alt+Up / Alt+Down move the focused item; announces new position
  used on   S07 Lyrics, S08
```

```
SettingsNav
  item      icon + label, height 40, radius Md
  states    default, hover, selected (bg AccentSubtle, 3px Accent bar left), focus-visible
  used on   S07
```

```
SettingsCard
  content   title (Body / 600), description (Caption, TextMuted), trailing control
  tokens    bg Surface, border Border, radius Lg, padding 16, gap 8 between cards
  used on   S07, S08
```

```
TrackListItem (All Tracks)
  content   title (Body / 600), artist · album (Caption, TextMuted), trailing badges: pinned, local
  states    default, hover, selected (bg AccentSubtle), focus-visible
  a11y      ListBox with virtualisation; Name = "title, artist, album, status"
  used on   S10
```

```
SourceVersionItem (Lyric sources list)
  content   source name or version name, sub-line (track info of that version), labels row, status
  labels    Line, Word, Syllable, Translation, Instrumental (Caption, radius Sm, bg SurfaceAlt)
  status    not fetched (TextMuted "Not fetched"), cached, confirmed (check icon, Success), pinned (pin icon, Accent), in use (filled Accent tag "In use")
  states    default, hover, selected, focus-visible, loading (spinner instead of status), error (Danger text + retry)
  used on   S09, S10
```

```
SearchResultRow (Search and select version)
  content   title, artist, album, duration mm:ss, labels (Synced, Word sync, Plain only, Instrumental)
  states    default, hover, selected, focus-visible; duration differs > 2 s from the track: Warning text
  used on   S11
```

```
LyricEditor
  content   tabs "Lyrics" / "Translation (optional)", format ComboBox, monospace text area (Font.Mono 13), line numbers, parse status bar
  states    empty, editing, valid ("n lines, word timing found"), parse error (line number + reason, Danger)
  used on   S13
```

```
LyricPreview (inline)
  content   read-only lyric list using the same renderer as fullscreen, small scale (Body size)
  states    no lyrics, line-only, word-sync, with translation, instrumental
  used on   S09, S11, S12, S14
```

```
Dialog
  variants  info, confirm, destructive confirm (primary is Danger), form (S12, S17)
  tokens    bg Surface, radius Lg, Shadow.Pop, padding 24, max width 480
  a11y      modal, focus starts on the first input or the safest button, Esc = cancel, Enter = primary
  used on   S12, S15, S16, S17, delete version
```

```
InfoBar
  variants  info, success, warning, error
  content   icon, title, message, optional action, close
  used on   S09 (no session, track changed), S10 (filter empty), S16, network errors
```

```
EmptyState
  content   Lucide icon 32px (TextMuted), one-line title, one-line hint, optional action
  used on   S09 no session, S10 empty library / empty filter, S11 no results
```

## Lyric rendering

```
LyricLine (custom FrameworkElement, DrawingContext rendering)
  modes     line-only (whole line switches at start), word / syllable (left-to-right fill per syllable, linear over its duration)
  layers    base text (Lyric.Text or Lyric.Dim), sung fill (Lyric.Sung clipped by progress), optional outline (Lyric.Stroke, Lyric.OutlineThickness), optional glow (Shadow.LyricGlow)
  rows      main line, optional translation row (size × Lyric.Desktop.TranslationRatio, Lyric.Translation)
  states    upcoming, current, past, instrumental gap (three-dot pulse), no lyrics (title — artist), loading (shimmer on the title)
  perf      FormattedText cached per line and DPI; only the current line re-renders per frame; one shared CompositionTarget.Rendering tick
  a11y      AutomationPeer exposes the current line text; LiveSetting off (would be too chatty)
  used on   S02-S06, previews
```

```
LyricScroller (fullscreen list)
  content   lines stacked, current line centred at 40 % height, eased scroll (Motion.LineScroll), past and upcoming lines at Lyric.Fullscreen.Other.Opacity, blur 2px on distant lines (optional)
  input     mouse wheel pauses auto-follow for 3 s; click a line = no seek (SMTC seek is optional, only if the session supports it)
  used on   S05, S06, S14
```

```
IslandPill (S02)
  layout    top-centre of the chosen monitor, Layout.Island.* sizes, radius Pill, bg Lyric.OverlayBg
  content   collapsed: cover 24px + current line; expanded (hover): cover 40px, title / artist, current line, play / pause, previous, next (only when the session reports these controls)
  states    hidden (no session, auto-hide on), idle (paused: title only, dim), playing, no lyrics (title — artist), instrumental, expanded, fullscreen app on top (hidden)
  motion    width and height animate with Motion.IslandResize + Motion.Ease; text cross-fades on line change
  credit    island-style view: idea credited to Lyricify (WXRIW) under CC BY-SA 4.0, see About
```

```
DesktopLyricsWindow (S03)
  layout    borderless, transparent, topmost, toolwindow; 1 or 2 lines (current + next or current + translation); alignment left / centre / right
  states    locked (WS_EX_TRANSPARENT click-through, no hover UI), unlocked (hover shows a 32px toolbar: lock, smaller, larger, settings, close; drag to move), no lyrics, paused (dim)
  a11y      unlock always reachable from the tray menu and a global hotkey (configurable, off by default)
```

```
TaskbarLyricsWindow (S04)
  layout    topmost toolwindow aligned to the taskbar rect (ABM_GETTASKBARPOS), placed in free space left of the tray area; cover 24px + one line
  states    playing, no lyrics, taskbar auto-hidden (hidden), vertical taskbar (unsupported: hidden with an InfoBar in settings), fullscreen app (hidden)
```

```
FullscreenLyricsWindow (S05) and StageWindow (S06)
  S05       two columns: cover 320px + title / artist / progress on the left, LyricScroller on the right; background = cover blurred (80px) and darkened to keep Lyric.Text contrast
  S06       one centred column, Lyric.Stage.Size, slow animated gradient from two colours sampled from the cover (darkened to ≤ 0.15 luminance so white text stays AA)
  input     Esc or double-click exits; F11 toggles
```

```
TrayIcon (S01)
  icon      own monochrome glyph (designed in /replica-brand), light and dark variants
  tooltip   "title — artist" or "Nothing playing"
  menu      Island, Desktop lyrics, Taskbar lyrics (toggles) · Fullscreen lyrics, Stage · Tracks & lyrics ▸ Now playing / All tracks · Lock desktop lyrics · Settings · Exit
```

## Copy (fresh, not the original's wording)

| place | text (en) | text (zh-CN) |
| --- | --- | --- |
| nothing playing | Nothing playing | 没有正在播放的内容 |
| no lyrics | No lyrics for this track yet | 这首歌暂时没有歌词 |
| instrumental | Instrumental | 纯音乐 |
| SMTC unavailable | Windows media controls are not available on this PC, so playback can't be read. | 这台电脑上的 Windows 媒体控制不可用，无法读取播放信息。 |
| no player | Start playing something in a music app that shows up in the Windows media flyout. | 请在任意能出现在 Windows 媒体控制面板里的音乐软件中播放歌曲。 |
| pin | Pin | 固定 |
| confirm | Mark as correct | 标记为正确 |
| use local | Use this version | 使用此版本 |
| back to auto | Back to automatic | 恢复自动 |
| in use | In use | 使用中 |
| offset hint | Positive values show lyrics later, negative values earlier. | 正值让歌词更晚出现，负值更早。 |
| no timeline | This player doesn't report progress. Lyrics follow a timer and can drift after you seek. | 该播放器不提供进度，歌词按计时器推进，拖动进度后可能错位。 |
| gallery sample | This is a sample line for testing | 这是一行用于测试的示例文字 |
