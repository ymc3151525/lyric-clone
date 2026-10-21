namespace LyricClone.Core.Lyrics;

/// <summary>Script checks used to pair an English line with a Chinese translation — never the reverse.</summary>
public static class LyricScript
{
    public static bool ContainsCjk(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        foreach (var c in s)
        {
            if (c >= 0x3400 && c <= 0x9FFF)
            {
                return true;
            }
        }

        return false;
    }

    public static bool ContainsLatin(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        foreach (var c in s)
        {
            if (c <= 0x7F && char.IsLetter(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Main lyric is English (Latin letters, no CJK). Only these lines may carry a translation.</summary>
    public static bool IsEnglishMain(string? text) => ContainsLatin(text) && !ContainsCjk(text);

    /// <summary>True when <paramref name="main"/> is English and <paramref name="other"/> is Chinese.</summary>
    public static bool IsEnglishToChinese(string? main, string? other) =>
        IsEnglishMain(main) && ContainsCjk(other);
}
