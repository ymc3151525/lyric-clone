namespace LyricClone.Core.Lyrics;

public enum LyricsFormat
{
    Lrc,
    Qrc,
    Krc,
    Yrc,
    Ttml,
    LyricifyLines,
    LyricifySyllable,
    LrclibLyricsfile,
    Plain,
}

public sealed record Syllable(long StartMs, long DurationMs, string Text)
{
    public long EndMs => StartMs + DurationMs;
}

public sealed record LyricLine(
    long StartMs,
    long EndMs,
    string Text,
    IReadOnlyList<Syllable> Syllables,
    string? Translation = null)
{
    public bool HasSyllables => Syllables.Count > 0;

    public LyricLine WithOffset(long offsetMs) => this with
    {
        StartMs = StartMs + offsetMs,
        EndMs = EndMs + offsetMs,
        Syllables = Syllables.Select(s => s with { StartMs = s.StartMs + offsetMs }).ToArray(),
    };
}

public sealed class ParsedLyrics
{
    public static readonly ParsedLyrics Empty = new([], isSynced: false);

    public ParsedLyrics(IReadOnlyList<LyricLine> lines, bool isSynced)
    {
        Lines = lines;
        IsSynced = isSynced;
    }

    public IReadOnlyList<LyricLine> Lines { get; }

    public bool IsSynced { get; }

    public bool HasWordSync => Lines.Any(l => l.HasSyllables);

    public bool HasTranslation => Lines.Any(l => !string.IsNullOrEmpty(l.Translation));

    public bool IsEmpty => Lines.Count == 0;

    /// <summary>
    /// Index of the line active at <paramref name="positionMs"/>, or -1 before the first synced line.
    /// Unsynced text is paced across <paramref name="durationMs"/> when the player reports a length.
    /// </summary>
    public int FindLineIndex(long positionMs, long? durationMs = null)
    {
        if (Lines.Count == 0)
        {
            return -1;
        }

        if (!IsSynced)
        {
            if (durationMs is not > 0)
            {
                return 0;
            }

            int i = (int)(positionMs * Lines.Count / durationMs.Value);
            return Math.Clamp(i, 0, Lines.Count - 1);
        }

        if (positionMs < Lines[0].StartMs)
        {
            return -1;
        }

        int lo = 0, hi = Lines.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (Lines[mid].StartMs <= positionMs)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }

    public ParsedLyrics WithOffset(long offsetMs) =>
        offsetMs == 0 ? this : new ParsedLyrics(Lines.Select(l => l.WithOffset(offsetMs)).ToArray(), IsSynced);

    /// <summary>
    /// Several lyric lines sometimes share one timestamp (a verse of three sentences).
    /// Spread them across the time until the next unique start so each sentence can stay on screen.
    /// </summary>
    public ParsedLyrics SpreadSharedStarts()
    {
        if (!IsSynced || Lines.Count < 2)
        {
            return this;
        }

        var result = Lines.ToArray();
        bool changed = false;
        int i = 0;
        while (i < result.Length)
        {
            int j = i + 1;
            while (j < result.Length && result[j].StartMs == result[i].StartMs)
            {
                j++;
            }

            int n = j - i;
            if (n > 1 && result.Skip(i).Take(n).All(l => !l.HasSyllables))
            {
                long start = result[i].StartMs;
                long until = j < result.Length ? result[j].StartMs : Math.Max(result[j - 1].EndMs, start + 5000L * n);
                if (until <= start)
                {
                    until = start + 5000L * n;
                }

                for (int k = 0; k < n; k++)
                {
                    long s = start + (until - start) * k / n;
                    long e = start + (until - start) * (k + 1) / n;
                    result[i + k] = result[i + k] with { StartMs = s, EndMs = Math.Max(e, s + 1) };
                }

                changed = true;
            }

            i = j;
        }

        return changed ? new ParsedLyrics(result, IsSynced) : this;
    }
}
