using LyricClone.Core.Lyrics;
using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;
using HelperSyllableLine = Lyricify.Lyrics.Models.SyllableLineInfo;

namespace LyricClone.Infrastructure.Lyrics;

/// <summary>
/// Extends the built-in LRC / TTML / plain parsers with QRC, KRC, YRC, Lyricify Lines and
/// Lyricify Syllable via Lyricify.Lyrics.Helper (Apache-2.0). Searchers and web providers are not used.
/// </summary>
public sealed class HelperLyricsParser : LyricsParser
{
    public override bool Supports(LyricsFormat format) => format is
        LyricsFormat.Lrc or LyricsFormat.Ttml or LyricsFormat.Plain
        or LyricsFormat.Qrc or LyricsFormat.Krc or LyricsFormat.Yrc
        or LyricsFormat.LyricifyLines or LyricsFormat.LyricifySyllable
        or LyricsFormat.LrclibLyricsfile;

    public override LyricsFormat Detect(string content)
    {
        content = PlayerCacheProvider.ExtractLyricContent(content);
        var t = content.TrimStart();
        if (t.Contains("[type:LyricifyLines]", StringComparison.OrdinalIgnoreCase))
        {
            return LyricsFormat.LyricifyLines;
        }

        if (t.Contains("[type:LyricifySyllable]", StringComparison.OrdinalIgnoreCase))
        {
            return LyricsFormat.LyricifySyllable;
        }

        if (t.StartsWith("<tt", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && t.Contains("ttml", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith('<') && !t.Contains("qrc", StringComparison.OrdinalIgnoreCase))
        {
            return LyricsFormat.Ttml;
        }

        try
        {
            var raw = TypeHelper.GetLyricsTypes(content);
            var mapped = Map(raw);
            if (mapped is not LyricsFormat.Plain)
            {
                return mapped;
            }
        }
        catch (Exception)
        {
            // Fall through to the built-in detector.
        }

        return base.Detect(content);
    }

    public override ParsedLyrics Parse(string content, LyricsFormat format, string? translation = null)
    {
        content = PlayerCacheProvider.ExtractLyricContent(content);
        if (!string.IsNullOrWhiteSpace(translation))
        {
            translation = PlayerCacheProvider.ExtractLyricContent(translation);
        }

        if (format is LyricsFormat.Lrc or LyricsFormat.Ttml or LyricsFormat.Plain)
        {
            return base.Parse(content, format, translation);
        }

        if (format == LyricsFormat.LrclibLyricsfile)
        {
            return MergeTranslation(LrcParser.Parse(content), translation);
        }

        var parsed = TryHelper(content, format, translation);
        if (!parsed.IsEmpty)
        {
            return parsed;
        }

        if (format is LyricsFormat.Qrc or LyricsFormat.Krc)
        {
            var decrypted = TryDecrypt(content, format);
            if (!string.IsNullOrWhiteSpace(decrypted) && decrypted != content)
            {
                parsed = TryHelper(decrypted, format, translation);
                if (!parsed.IsEmpty)
                {
                    return parsed;
                }

                content = decrypted;
            }
        }

        // QQ Music often writes ordinary LRC into a .qrc cache file.
        if (LrcParser.LooksLikeLrc(content))
        {
            return base.Parse(content, LyricsFormat.Lrc, translation);
        }

        var timed = ParseHelperTimedLines(content);
        return timed.IsEmpty ? ParsedLyrics.Empty : MergeSyncedTranslation(timed, translation).SpreadSharedStarts();
    }

    private ParsedLyrics TryHelper(string content, LyricsFormat format, string? translation)
    {
        try
        {
            var raw = ToRaw(format);
            var data = ParseHelper.ParseLyrics(content, raw);
            if (data is null && format == LyricsFormat.Qrc)
            {
                data = ParseHelper.ParseLyrics(content, LyricsRawTypes.QrcFull);
            }

            if (data is null && format == LyricsFormat.Yrc)
            {
                data = ParseHelper.ParseLyrics(content, LyricsRawTypes.YrcFull);
            }

            return Convert(data, translation);
        }
        catch (Exception)
        {
            return ParsedLyrics.Empty;
        }
    }

    private static string? TryDecrypt(string content, LyricsFormat format)
    {
        try
        {
            return format == LyricsFormat.Qrc
                ? Lyricify.Lyrics.Decrypter.Qrc.Decrypter.DecryptLyrics(content)
                : Lyricify.Lyrics.Decrypter.Krc.Decrypter.DecryptLyrics(content);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private ParsedLyrics Convert(LyricsData? data, string? translation)
    {
        if (data?.Lines is not { Count: > 0 } lines)
        {
            return ParsedLyrics.Empty;
        }

        var result = new List<LyricLine>(lines.Count);
        bool synced = false;
        foreach (var line in lines)
        {
            if (line is null)
            {
                continue;
            }

            long start = line.StartTime ?? 0;
            long end = line.EndTime ?? start;
            if (line.StartTime.HasValue || line.EndTime.HasValue)
            {
                synced = true;
            }

            IReadOnlyList<Syllable> syllables = [];
            if (line is HelperSyllableLine { IsSyllable: true } syllableLine)
            {
                syllables = syllableLine.Syllables
                    .Select(s => new Syllable(s.StartTime, Math.Max(0, s.EndTime - s.StartTime), s.Text ?? ""))
                    .ToArray();
            }

            string text = line.Text ?? "";
            string? tr = line is IFullLineInfo full ? full.ChineseTranslation : null;
            if (!LyricScript.IsEnglishMain(text))
            {
                tr = null;
            }

            if (string.IsNullOrEmpty(text) && syllables.Count == 0)
            {
                continue;
            }

            result.Add(new LyricLine(start, end, text, syllables, tr));
        }

        if (result.Count == 0)
        {
            return ParsedLyrics.Empty;
        }

        return MergeSyncedTranslation(new ParsedLyrics(result, synced), translation).SpreadSharedStarts();
    }

    /// <summary>QQ cache translations are QRC, not LRC — pair them by timestamp onto English lines.</summary>
    private ParsedLyrics MergeSyncedTranslation(ParsedLyrics lyrics, string? translation)
    {
        if (string.IsNullOrWhiteSpace(translation) || !lyrics.IsSynced)
        {
            return lyrics;
        }

        ParsedLyrics tr;
        if (LrcParser.LooksLikeLrc(translation))
        {
            tr = LrcParser.Parse(translation);
        }
        else
        {
            var format = LooksLikeHelperTimed(translation) ? LyricsFormat.Qrc : Detect(translation);
            tr = format is LyricsFormat.Lrc or LyricsFormat.Ttml or LyricsFormat.Plain
                ? base.Parse(translation, format)
                : TryHelper(translation, format, null);
            if (tr.IsEmpty && format is LyricsFormat.Qrc or LyricsFormat.Krc)
            {
                var decrypted = TryDecrypt(translation, format);
                if (!string.IsNullOrWhiteSpace(decrypted))
                {
                    tr = TryHelper(decrypted, format, null);
                }
            }

            if (tr.IsEmpty)
            {
                tr = ParseHelperTimedLines(translation);
            }
        }

        if (tr.IsEmpty)
        {
            return lyrics;
        }

        const long toleranceMs = 800;
        var merged = lyrics.Lines.Select(line =>
        {
            if (!string.IsNullOrEmpty(line.Translation) || line.Text.Length == 0 || !LyricScript.IsEnglishMain(line.Text))
            {
                return line;
            }

            var match = tr.Lines
                .Where(t => t.Text.Length > 0)
                .OrderBy(t => Math.Abs(t.StartMs - line.StartMs))
                .FirstOrDefault();
            return match is not null && Math.Abs(match.StartMs - line.StartMs) <= toleranceMs
                ? line with { Translation = match.Text }
                : line;
        }).ToArray();

        return new ParsedLyrics(merged, lyrics.IsSynced);
    }

    private static ParsedLyrics ParseHelperTimedLines(string content)
    {
        var lines = new List<LyricLine>();
        foreach (var raw in content.AsSpan().EnumerateLines())
        {
            var line = raw.Trim();
            if (line.Length < 4 || line[0] != '[')
            {
                continue;
            }

            int close = line.IndexOf(']');
            int comma = line.IndexOf(',');
            if (close < 3 || comma < 1 || comma > close)
            {
                continue;
            }

            if (!long.TryParse(line[1..comma], out long start) || !long.TryParse(line[(comma + 1)..close], out long dur))
            {
                continue;
            }

            var text = line[(close + 1)..].ToString();
            int paren = text.IndexOf('(');
            if (paren > 0)
            {
                text = text[..paren];
            }

            text = text.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            lines.Add(new LyricLine(start, start + Math.Max(0, dur), text, []));
        }

        return lines.Count == 0 ? ParsedLyrics.Empty : new ParsedLyrics(lines, isSynced: true);
    }

    private static bool LooksLikeHelperTimed(string text)
    {
        var t = text.TrimStart();
        int comma = t.IndexOf(',');
        int close = t.IndexOf(']');
        return t.Length > 3 && t[0] == '[' && comma > 1 && close > comma;
    }

    private static LyricsRawTypes ToRaw(LyricsFormat format) => format switch
    {
        LyricsFormat.Qrc => LyricsRawTypes.Qrc,
        LyricsFormat.Krc => LyricsRawTypes.Krc,
        LyricsFormat.Yrc => LyricsRawTypes.Yrc,
        LyricsFormat.LyricifyLines => LyricsRawTypes.LyricifyLines,
        LyricsFormat.LyricifySyllable => LyricsRawTypes.LyricifySyllable,
        LyricsFormat.Ttml => LyricsRawTypes.Ttml,
        LyricsFormat.Lrc => LyricsRawTypes.Lrc,
        _ => LyricsRawTypes.Unknown,
    };

    private static LyricsFormat Map(LyricsRawTypes raw) => raw.GetLyricsType() switch
    {
        LyricsTypes.Qrc => LyricsFormat.Qrc,
        LyricsTypes.Krc => LyricsFormat.Krc,
        LyricsTypes.Yrc => LyricsFormat.Yrc,
        LyricsTypes.LyricifyLines => LyricsFormat.LyricifyLines,
        LyricsTypes.LyricifySyllable => LyricsFormat.LyricifySyllable,
        LyricsTypes.Ttml => LyricsFormat.Ttml,
        LyricsTypes.Lrc => LyricsFormat.Lrc,
        _ => LyricsFormat.Plain,
    };
}
