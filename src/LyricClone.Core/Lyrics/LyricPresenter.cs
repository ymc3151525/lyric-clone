using System.Runtime.InteropServices;
using System.Text;

namespace LyricClone.Core.Lyrics;

public enum RomajiDisplay
{
    Off,
    Show,
}

public enum VersionPickMode
{
    SourceFirst,
    PreferQuality,
}

/// <summary>Display-only transforms: simplified Chinese, romaji, long-line split, and a small explicit filter.</summary>
public static partial class LyricPresenter
{
    private const int SplitOverChars = 18;
    private const uint LcmapSimplifiedChinese = 0x02000000;

    private static readonly char[] SplitAt = ['，', '。', '！', '？', '、', '；', ',', '.', '!', '?'];

    private static readonly string[] ExplicitTokens =
    [
        "fuck", "shit", "bitch", "asshole", "bastard", "dick", "pussy", "cunt", "nigger", "faggot",
        "操", "妈的", "傻逼", "操你妈",
    ];

    public static ParsedLyrics Apply(ParsedLyrics source, bool simplified, RomajiDisplay romaji, bool splitLong, bool showExplicit, bool showTranslation)
    {
        if (source.IsEmpty)
        {
            return source;
        }

        var lines = new List<LyricLine>(source.Lines.Count);
        foreach (var line in source.Lines)
        {
            foreach (var piece in Expand(line, simplified, romaji, splitLong, showExplicit, showTranslation))
            {
                lines.Add(piece);
            }
        }

        return new ParsedLyrics(lines, source.IsSynced);
    }

    private static IEnumerable<LyricLine> Expand(LyricLine line, bool simplified, RomajiDisplay romaji, bool splitLong, bool showExplicit, bool showTranslation)
    {
        var text = Transform(line.Text, simplified, showExplicit);
        var translation = showTranslation ? Transform(line.Translation, simplified, showExplicit) : null;
        if (romaji == RomajiDisplay.Show)
        {
            var r = ToRomaji(text);
            if (!string.IsNullOrWhiteSpace(r) && !string.Equals(r, text, StringComparison.Ordinal))
            {
                translation = string.IsNullOrWhiteSpace(translation) ? r : translation + "  " + r;
            }
        }

        var syllables = line.Syllables.Count == 0
            ? line.Syllables
            : line.Syllables.Select(s => s with { Text = Transform(s.Text, simplified, showExplicit) }).ToArray();

        var transformed = line with { Text = text, Translation = translation, Syllables = syllables };
        if (!splitLong)
        {
            yield return transformed;
            yield break;
        }

        var parts = Split(text);
        if (parts.Count <= 1)
        {
            yield return transformed;
            yield break;
        }

        int totalChars = 0;
        foreach (var part in parts)
        {
            totalChars += Math.Max(1, part.Length);
        }

        long span = Math.Max(1, line.EndMs - line.StartMs);
        int used = 0;
        for (int i = 0; i < parts.Count; i++)
        {
            int n = Math.Max(1, parts[i].Length);
            long s = line.StartMs + span * used / totalChars;
            used += n;
            long e = line.StartMs + span * used / totalChars;
            yield return new LyricLine(s, Math.Max(e, s + 1), parts[i], [], i == 0 ? translation : null);
        }
    }

    private static string Transform(string? value, bool simplified, bool showExplicit)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? "";
        }

        var t = simplified ? ToSimplified(value) : value;
        return showExplicit ? t : MaskExplicit(t);
    }

    public static string ToSimplified(string text)
    {
        if (string.IsNullOrEmpty(text) || !OperatingSystem.IsWindows())
        {
            return text;
        }

        var dest = new char[checked(text.Length * 2)];
        int n = LCMapStringEx("zh-CN", LcmapSimplifiedChinese, text, text.Length, dest, dest.Length, 0, 0, 0);
        return n > 0 ? new string(dest, 0, n) : text;
    }

    public static string MaskExplicit(string text)
    {
        var t = text;
        foreach (var token in ExplicitTokens)
        {
            t = ReplaceInsensitive(t, token);
        }

        return t;
    }

    public static IReadOnlyList<string> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [text];
        }

        return ContainsCjk(text) ? SplitCjk(text) : SplitLatin(text);
    }

    private static bool ContainsCjk(string text)
    {
        foreach (var c in text)
        {
            if (c >= 0x3400 && c <= 0x9FFF)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Chinese LRC often stores a whole verse as one timed line, phrases separated by spaces.
    /// Split those so each sentence can stay on screen for its share of the verse.
    /// </summary>
    private static IReadOnlyList<string> SplitCjk(string text)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        foreach (var c in text)
        {
            if (c is ' ' or '\u3000')
            {
                FlushCjk(parts, current);
                continue;
            }

            current.Append(c);
            if (SplitAt.Contains(c))
            {
                FlushCjk(parts, current);
            }
        }

        FlushCjk(parts, current);
        return parts.Count == 0 ? [text.Trim()] : parts.ToArray();
    }

    private static void FlushCjk(List<string> parts, StringBuilder current)
    {
        var piece = current.ToString().Trim();
        current.Clear();
        if (piece.Length > 0)
        {
            parts.Add(piece);
        }
    }

    private static IReadOnlyList<string> SplitLatin(string text)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        foreach (var c in text)
        {
            current.Append(c);
            if (current.Length >= SplitOverChars && SplitAt.Contains(c))
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString().Trim());
        }

        return parts.Count == 0 ? [text] : parts.Where(p => p.Length > 0).ToArray();
    }

    public static string ToRomaji(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Any(IsKana))
        {
            return "";
        }

        var sb = new StringBuilder(text.Length * 2);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            char next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (TryKana(c, next, out var romaji, out bool consumedNext))
            {
                sb.Append(romaji);
                if (consumedNext)
                {
                    i++;
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static string ReplaceInsensitive(string text, string token)
    {
        if (token.Length == 0)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            if (i + token.Length <= text.Length && text.AsSpan(i, token.Length).Equals(token, StringComparison.OrdinalIgnoreCase)
                && IsBoundary(text, i, token.Length))
            {
                sb.Append('*', token.Length);
                i += token.Length;
            }
            else
            {
                sb.Append(text[i]);
                i++;
            }
        }

        return sb.ToString();
    }

    private static bool IsBoundary(string text, int index, int length)
    {
        if (char.GetUnicodeCategory(text[index]) == System.Globalization.UnicodeCategory.OtherLetter)
        {
            return true;
        }

        bool before = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
        bool after = index + length >= text.Length || !char.IsLetterOrDigit(text[index + length]);
        return before && after;
    }

    private static bool IsKana(char c) =>
        c is (>= '\u3040' and <= '\u30FF') or (>= '\uFF66' and <= '\uFF9D');

    private static bool TryKana(char c, char next, out string romaji, out bool consumedNext)
    {
        consumedNext = false;
        romaji = "";
        char n = NormalizeKana(c);
        if (n == '\0')
        {
            return false;
        }

        if (n == 'っ' || n == 'ッ')
        {
            char n2 = NormalizeKana(next);
            if (n2 != '\0' && Kana.TryGetValue(n2, out var follow) && follow.Length > 0)
            {
                romaji = follow[0].ToString();
                return true;
            }

            romaji = "tsu";
            return true;
        }

        if (next is 'ゃ' or 'ャ' or 'ゅ' or 'ュ' or 'ょ' or 'ョ')
        {
            if (Youon.TryGetValue((n, NormalizeKana(next)), out var y))
            {
                romaji = y;
                consumedNext = true;
                return true;
            }
        }

        if (Kana.TryGetValue(n, out var r))
        {
            romaji = r;
            return true;
        }

        return false;
    }

    private static char NormalizeKana(char c)
    {
        if (c is >= '\u30A1' and <= '\u30F6')
        {
            return (char)(c - 0x60);
        }

        if (c is >= '\uFF66' and <= '\uFF9D')
        {
            return c;
        }

        return c is >= '\u3041' and <= '\u3096' ? c : '\0';
    }

    private static readonly Dictionary<char, string> Kana = new()
    {
        ['あ'] = "a", ['い'] = "i", ['う'] = "u", ['え'] = "e", ['お'] = "o",
        ['か'] = "ka", ['き'] = "ki", ['く'] = "ku", ['け'] = "ke", ['こ'] = "ko",
        ['が'] = "ga", ['ぎ'] = "gi", ['ぐ'] = "gu", ['げ'] = "ge", ['ご'] = "go",
        ['さ'] = "sa", ['し'] = "shi", ['す'] = "su", ['せ'] = "se", ['そ'] = "so",
        ['ざ'] = "za", ['じ'] = "ji", ['ず'] = "zu", ['ぜ'] = "ze", ['ぞ'] = "zo",
        ['た'] = "ta", ['ち'] = "chi", ['つ'] = "tsu", ['て'] = "te", ['と'] = "to",
        ['だ'] = "da", ['ぢ'] = "ji", ['づ'] = "zu", ['で'] = "de", ['ど'] = "do",
        ['な'] = "na", ['に'] = "ni", ['ぬ'] = "nu", ['ね'] = "ne", ['の'] = "no",
        ['は'] = "ha", ['ひ'] = "hi", ['ふ'] = "fu", ['へ'] = "he", ['ほ'] = "ho",
        ['ば'] = "ba", ['び'] = "bi", ['ぶ'] = "bu", ['べ'] = "be", ['ぼ'] = "bo",
        ['ぱ'] = "pa", ['ぴ'] = "pi", ['ぷ'] = "pu", ['ぺ'] = "pe", ['ぽ'] = "po",
        ['ま'] = "ma", ['み'] = "mi", ['む'] = "mu", ['め'] = "me", ['も'] = "mo",
        ['や'] = "ya", ['ゆ'] = "yu", ['よ'] = "yo",
        ['ら'] = "ra", ['り'] = "ri", ['る'] = "ru", ['れ'] = "re", ['ろ'] = "ro",
        ['わ'] = "wa", ['を'] = "wo", ['ん'] = "n",
        ['ぁ'] = "a", ['ぃ'] = "i", ['ぅ'] = "u", ['ぇ'] = "e", ['ぉ'] = "o",
        ['ー'] = "-", ['・'] = " ",
    };

    private static readonly Dictionary<(char, char), string> Youon = new()
    {
        [('き', 'ゃ')] = "kya", [('き', 'ゅ')] = "kyu", [('き', 'ょ')] = "kyo",
        [('ぎ', 'ゃ')] = "gya", [('ぎ', 'ゅ')] = "gyu", [('ぎ', 'ょ')] = "gyo",
        [('し', 'ゃ')] = "sha", [('し', 'ゅ')] = "shu", [('し', 'ょ')] = "sho",
        [('じ', 'ゃ')] = "ja", [('じ', 'ゅ')] = "ju", [('じ', 'ょ')] = "jo",
        [('ち', 'ゃ')] = "cha", [('ち', 'ゅ')] = "chu", [('ち', 'ょ')] = "cho",
        [('に', 'ゃ')] = "nya", [('に', 'ゅ')] = "nyu", [('に', 'ょ')] = "nyo",
        [('ひ', 'ゃ')] = "hya", [('ひ', 'ゅ')] = "hyu", [('ひ', 'ょ')] = "hyo",
        [('び', 'ゃ')] = "bya", [('び', 'ゅ')] = "byu", [('び', 'ょ')] = "byo",
        [('ぴ', 'ゃ')] = "pya", [('ぴ', 'ゅ')] = "pyu", [('ぴ', 'ょ')] = "pyo",
        [('み', 'ゃ')] = "mya", [('み', 'ゅ')] = "myu", [('み', 'ょ')] = "myo",
        [('り', 'ゃ')] = "rya", [('り', 'ゅ')] = "ryu", [('り', 'ょ')] = "ryo",
    };

    [LibraryImport("kernel32.dll", EntryPoint = "LCMapStringEx", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int LCMapStringEx(
        string locale,
        uint flags,
        string src,
        int srcLen,
        [Out] char[] dest,
        int destLen,
        nint version,
        nint reserved,
        nint sort);
}
