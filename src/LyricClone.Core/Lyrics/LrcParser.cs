using System.Globalization;
using System.Text;

namespace LyricClone.Core.Lyrics;

/// <summary>
/// LRC with multiple time tags per line, the [offset:] tag, enhanced word tags (&lt;mm:ss.xx&gt;),
/// and translations given either as a second line with the same time tag or as a separate LRC text.
/// </summary>
public static class LrcParser
{
    private const long DefaultLastLineMs = 5000;
    private const long TranslationMatchToleranceMs = 400;

    public static ParsedLyrics Parse(string? content, string? translation = null)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return ParsedLyrics.Empty;
        }

        var entries = ReadEntries(content, out long offset);
        if (entries.Count == 0)
        {
            return ParsedLyrics.Empty;
        }

        entries.Sort((a, b) => a.StartMs != b.StartMs ? a.StartMs.CompareTo(b.StartMs) : a.Order.CompareTo(b.Order));

        var main = new List<RawEntry>(entries.Count);
        var inlineTranslations = new Dictionary<int, string>();
        foreach (var e in entries)
        {
            if (main.Count > 0 && e.Text.Length > 0 && main[^1].Text.Length > 0
                && Math.Abs(main[^1].StartMs - e.StartMs) <= TranslationMatchToleranceMs
                && LyricScript.IsEnglishToChinese(main[^1].Text, e.Text))
            {
                int idx = main.Count - 1;
                if (!inlineTranslations.ContainsKey(idx))
                {
                    inlineTranslations[idx] = e.Text;
                }

                continue;
            }

            main.Add(e);
        }

        var lines = new List<LyricLine>(main.Count);
        for (int i = 0; i < main.Count; i++)
        {
            var e = main[i];
            long start = e.StartMs - offset;
            long nextStart = i + 1 < main.Count ? main[i + 1].StartMs - offset : start + DefaultLastLineMs;
            long end = e.TrailingTagMs is long trailing ? trailing - offset : nextStart;
            if (end <= start)
            {
                end = nextStart > start ? nextStart : start + 1;
            }

            var syllables = BuildSyllables(e.Words, offset, end);
            inlineTranslations.TryGetValue(i, out var tr);
            var text = e.Text;
            if (string.IsNullOrEmpty(tr) && TrySplitBilingual(text, out var mainText, out var slashTr))
            {
                text = mainText;
                tr = slashTr;
            }

            lines.Add(new LyricLine(start, end, text, syllables, tr));
        }

        if (!string.IsNullOrWhiteSpace(translation))
        {
            lines = MergeTranslation(lines, translation);
        }

        return new ParsedLyrics(lines, isSynced: true).SpreadSharedStarts();
    }

    internal static bool TrySplitBilingual(string text, out string main, out string translation)
    {
        main = text;
        translation = "";
        foreach (var sep in new[] { " / ", "／", " /", "/ " })
        {
            int i = text.IndexOf(sep, StringComparison.Ordinal);
            if (i <= 0)
            {
                continue;
            }

            var a = text[..i].Trim();
            var b = text[(i + sep.Length)..].Trim();
            if (a.Length > 0 && b.Length > 0 && LyricScript.IsEnglishToChinese(a, b))
            {
                main = a;
                translation = b;
                return true;
            }
        }

        return false;
    }

    /// <summary>Same (or nearly the same) timestamp is a translation only when the main line is English and the other is Chinese.</summary>
    internal static bool LooksLikeTranslation(string a, string b) => LyricScript.IsEnglishToChinese(a, b);

    /// <summary>True when the text contains at least one line-level time tag.</summary>
    public static bool LooksLikeLrc(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return false;
        }

        foreach (var raw in content.AsSpan().EnumerateLines())
        {
            var line = raw.Trim();
            if (line.Length > 2 && line[0] == '[')
            {
                int close = line.IndexOf(']');
                if (close > 1 && TimeTag.TryParseClock(line[1..close], out _))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static List<LyricLine> MergeTranslation(List<LyricLine> lines, string translation)
    {
        var tr = ReadEntries(translation, out long trOffset)
            .Where(e => e.Text.Length > 0)
            .Select(e => (Start: e.StartMs - trOffset, e.Text))
            .ToList();
        if (tr.Count == 0)
        {
            return lines;
        }

        var result = new List<LyricLine>(lines.Count);
        foreach (var line in lines)
        {
            var match = tr.FirstOrDefault(t => Math.Abs(t.Start - line.StartMs) <= TranslationMatchToleranceMs);
            result.Add(match.Text is not null && line.Text.Length > 0 && LyricScript.IsEnglishMain(line.Text)
                ? line with { Translation = match.Text }
                : line);
        }

        return result;
    }

    private static List<Syllable> BuildSyllables(List<(long StartMs, string Text)> words, long offset, long lineEnd)
    {
        var result = new List<Syllable>(words.Count);
        for (int i = 0; i < words.Count; i++)
        {
            long start = words[i].StartMs - offset;
            long end = i + 1 < words.Count ? words[i + 1].StartMs - offset : lineEnd;
            if (end < start)
            {
                end = start;
            }

            if (words[i].Text.Length > 0)
            {
                result.Add(new Syllable(start, end - start, words[i].Text));
            }
        }

        return result;
    }

    private static List<RawEntry> ReadEntries(string content, out long offset)
    {
        offset = 0;
        var entries = new List<RawEntry>();
        int order = 0;

        foreach (var raw in content.AsSpan().EnumerateLines())
        {
            var line = raw.Trim();
            if (line.IsEmpty || line[0] != '[')
            {
                continue;
            }

            var starts = new List<long>(1);
            var rest = line;
            while (rest.Length > 0 && rest[0] == '[')
            {
                int close = rest.IndexOf(']');
                if (close < 0)
                {
                    break;
                }

                var tag = rest[1..close];
                if (TimeTag.TryParseClock(tag, out long t))
                {
                    starts.Add(t);
                }
                else if (tag.StartsWith("offset:", StringComparison.OrdinalIgnoreCase))
                {
                    if (long.TryParse(tag[7..].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long o))
                    {
                        offset = o;
                    }
                }

                rest = rest[(close + 1)..];
            }

            if (starts.Count == 0)
            {
                continue;
            }

            ParseBody(rest, out string text, out var words, out long? trailing);
            foreach (var s in starts)
            {
                var shiftedWords = words;
                long? shiftedTrailing = trailing;
                if (starts.Count > 1 && words.Count > 0)
                {
                    long delta = s - starts[0];
                    shiftedWords = words.Select(w => (w.StartMs + delta, w.Text)).ToList();
                    shiftedTrailing = trailing + delta;
                }

                entries.Add(new RawEntry(s, text, shiftedWords, shiftedTrailing, order++));
            }
        }

        return entries;
    }

    private static void ParseBody(ReadOnlySpan<char> body, out string text, out List<(long StartMs, string Text)> words, out long? trailingTag)
    {
        words = [];
        trailingTag = null;
        if (body.IndexOf('<') < 0)
        {
            text = body.Trim().ToString();
            return;
        }

        var plain = new StringBuilder(body.Length);
        var current = new StringBuilder();
        long? currentStart = null;
        int i = 0;
        while (i < body.Length)
        {
            if (body[i] == '<')
            {
                int close = body[i..].IndexOf('>');
                if (close > 0 && TimeTag.TryParseClock(body[(i + 1)..(i + close)], out long t))
                {
                    if (currentStart is long cs && current.Length > 0)
                    {
                        words.Add((cs, current.ToString()));
                    }

                    current.Clear();
                    currentStart = t;
                    i += close + 1;
                    continue;
                }
            }

            current.Append(body[i]);
            plain.Append(body[i]);
            i++;
        }

        if (currentStart is long last)
        {
            if (current.Length > 0)
            {
                words.Add((last, current.ToString()));
            }
            else
            {
                trailingTag = last;
            }
        }

        text = plain.ToString().Trim();
    }

    private sealed record RawEntry(long StartMs, string Text, List<(long StartMs, string Text)> Words, long? TrailingTagMs, int Order);
}
