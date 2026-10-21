namespace LyricClone.Core.Lyrics;

public interface ILyricsParser
{
    ParsedLyrics Parse(string content, LyricsFormat format, string? translation = null);

    bool Supports(LyricsFormat format);

    LyricsFormat Detect(string content);
}

/// <summary>Built-in formats. QRC / KRC / YRC / Lyricify formats are added by an adapter in Infrastructure.</summary>
public class LyricsParser : ILyricsParser
{
    public virtual bool Supports(LyricsFormat format) =>
        format is LyricsFormat.Lrc or LyricsFormat.Ttml or LyricsFormat.Plain;

    public virtual LyricsFormat Detect(string content)
    {
        var t = content.TrimStart();
        if (t.StartsWith('<'))
        {
            return LyricsFormat.Ttml;
        }

        return LrcParser.LooksLikeLrc(content) ? LyricsFormat.Lrc : LyricsFormat.Plain;
    }

    public virtual ParsedLyrics Parse(string content, LyricsFormat format, string? translation = null) => format switch
    {
        LyricsFormat.Lrc => LrcParser.Parse(content, translation),
        LyricsFormat.Ttml => MergeTranslation(TtmlParser.Parse(content), translation),
        LyricsFormat.Plain => ParsePlain(content),
        _ => throw new NotSupportedException($"Lyrics format {format} is not supported by this parser."),
    };

    public static ParsedLyrics ParsePlain(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return ParsedLyrics.Empty;
        }

        var lines = content
            .Split('\n')
            .Select(l => l.TrimEnd('\r').Trim())
            .Where(t => t.Length > 0)
            .Select(t => new LyricLine(0, 0, t, []))
            .ToArray();
        return new ParsedLyrics(lines, isSynced: false);
    }

    protected static ParsedLyrics MergeTranslation(ParsedLyrics lyrics, string? translation)
    {
        if (string.IsNullOrWhiteSpace(translation) || !lyrics.IsSynced)
        {
            return lyrics;
        }

        var tr = LrcParser.Parse(translation);
        if (tr.IsEmpty)
        {
            return lyrics;
        }

        var merged = lyrics.Lines
            .Select(line =>
            {
                if (!string.IsNullOrEmpty(line.Translation) || line.Text.Length == 0 || !LyricScript.IsEnglishMain(line.Text))
                {
                    return line;
                }

                var match = tr.Lines.FirstOrDefault(t => Math.Abs(t.StartMs - line.StartMs) <= 400 && t.Text.Length > 0);
                return match is null ? line : line with { Translation = match.Text };
            })
            .ToArray();
        return new ParsedLyrics(merged, lyrics.IsSynced);
    }
}
