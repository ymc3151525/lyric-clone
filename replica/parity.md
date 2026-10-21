# Parity — LyricClone vs Lyricify Fusion

Date: 2026-10-20. Matrix: `replica/features.csv`. Scored with `parity.py`
(`D:\daily_tool\Python315\python.exe`). Original General screenshot: `replica/screens/S07.png`.

## Parity: 98.8 / 100  (features)

features 98.8  (51 counted, must-haves 28 of 28 done)
layout   23.9  (S07 General only — DIFFERENT; not re-run this round)

**Verdict: shippable** on features (all must-haves, score ≥ 80, no open S1/S2).

## By area, weakest first

| area | score | features |
|---|---|---|
| i18n | 50.0 | 1 |
| views | 95.2 | 9 |
| shell | 100.0 | 4 |
| playback | 100.0 | 6 |
| lyrics | 100.0 | 11 |
| settings | 100.0 | 5 |
| management | 100.0 | 12 |
| errors | 100.0 | 1 |
| about | 100.0 | 1 |
| a11y | 100.0 | 1 |

## Missing, in build order

- [should] views: Multi-monitor + per-monitor DPI, partial (needs two real screens)
- [could] i18n: long settings hints still zh-CN (chrome / tray / dialogs / nav follow the language setting)

## This round

- QRC / KRC / YRC / Lyricify Lines / Syllable via Lyricify.Lyrics.Helper 0.2.0 (parsers only).
- S06 Lyrics Stage, with cover-sampled darkened gradient and stage fonts/colours.
- Update checker on About (no default feed).
- Timeline smoothing tuned for jumpy SMTC reports.
- Keyboard: Tab cycle, focus rings, Esc / F11 / Space on Stage and fullscreen.

`dotnet test LyricClone.sln`: Core 72, Infrastructure 39, Flows 42.

## Layout (S07)

`imgdiff.py` layout mode, original General vs clone General: score **23.9 DIFFERENT**.
Expected: we did not copy the original's cards, icons, or "免费版" badge.

## Left out on purpose

QQ / NetEase / Kugou sources and IDs, Lyricify 4 vault, player plugins, original branding,
bundled lyrics, Store-only unlocks, Microsoft Store distribution.
