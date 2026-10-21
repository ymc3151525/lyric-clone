using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LyricClone.Core.Library;

/// <summary>Cleans the messy metadata players put into SMTC so the same song always gets the same key.</summary>
public static partial class TrackNormalizer
{
    [GeneratedRegex(@"\s*[\(\[（【](?:feat\.?|ft\.?|featuring|with)\s[^\)\]）】]*[\)\]）】]", RegexOptions.IgnoreCase)]
    private static partial Regex FeatBracket();

    [GeneratedRegex(@"\s+(?:feat\.?|ft\.?|featuring)\s.+$", RegexOptions.IgnoreCase)]
    private static partial Regex FeatTail();

    [GeneratedRegex(@"\s*[\(\[（【][^\)\]）】]*(?:remaster(?:ed)?|explicit|clean|radio edit)[^\)\]）】]*[\)\]）】]", RegexOptions.IgnoreCase)]
    private static partial Regex Decorations();

    [GeneratedRegex(@"\s+-\s+(?:\d{4}\s+)?remaster(?:ed)?(?:\s+\d{4})?(?:\s+version)?$", RegexOptions.IgnoreCase)]
    private static partial Regex RemasterTail();

    [GeneratedRegex(@"\s*[\(\[（【][^\)\]）】]*(?:live|live version|现场|现场版|現場|演唱會|演唱会)[^\)\]）】]*[\)\]）】]", RegexOptions.IgnoreCase)]
    private static partial Regex LiveTag();

    [GeneratedRegex(@"\s*[\(\[（【][^\)\]）】]*(?:single|album|studio|acoustic|karaoke|instrumental|deluxe|extended|special|original|official|version|ver\.?|mix|edition|cut|单曲|單曲|专辑|專輯|录音室|錄音室|伴奏|完整|特别|特別|重制|重製|正式|原唱|独唱|獨唱)[^\)\]）】]*[\)\]）】]", RegexOptions.IgnoreCase)]
    private static partial Regex VersionTag();

    [GeneratedRegex(@"\s*[\(\[（【\[][^\)\]）】\]]*(?:hi-?res|mqa|atmos|dolby|sq|hq|无损|臻品|母带|杜比|蝰蛇|空间音频|空間音頻|试听|試聽)[^\)\]）】\]]*[\)\]）】\]]", RegexOptions.IgnoreCase)]
    private static partial Regex QualityTag();

    [GeneratedRegex(@"\s*-\s*QQ(?:音乐|音樂| Music)$", RegexOptions.IgnoreCase)]
    private static partial Regex PlayerSuffix();

    [GeneratedRegex(@"\.(?:mp3|flac|m4a|aac|ogg|opus|wav|wma|ape|alac)$", RegexOptions.IgnoreCase)]
    private static partial Regex FileExtension();

    private static readonly char[] ArtistSeparators = ['/', '、', ';', '；', '&', '，', ','];

    /// <summary>Title cleaned for searching: no feat. credits, no remaster tags, no file extension.</summary>
    public static string CleanTitle(string title)
    {
        var t = FileExtension().Replace(title.Trim(), "");
        t = FeatBracket().Replace(t, "");
        t = FeatTail().Replace(t, "");
        t = Decorations().Replace(t, "");
        t = RemasterTail().Replace(t, "");
        t = PlayerSuffix().Replace(t, "");
        return t.Trim();
    }

    /// <summary>Title used for online search: also drops live / quality / version tags players such as QQ Music append.</summary>
    public static string SearchTitle(string title)
    {
        var t = LiveTag().Replace(CleanTitle(title), "");
        t = QualityTag().Replace(t, "");
        t = VersionTag().Replace(t, "");
        return t.Trim();
    }

    /// <summary>First credited artist, used for searching.</summary>
    public static string PrimaryArtist(string artist)
    {
        var a = FeatTail().Replace(artist.Trim(), "");
        int i = a.IndexOfAny(ArtistSeparators);
        if (i > 0)
        {
            a = a[..i];
        }

        int x = a.IndexOf(" x ", StringComparison.OrdinalIgnoreCase);
        if (x > 0)
        {
            a = a[..x];
        }

        return a.Trim();
    }

    /// <summary>
    /// Players such as file-based ones put "Artist - Title" in the title and leave artist empty.
    /// Returns the split values, or the inputs unchanged.
    /// </summary>
    public static (string Title, string Artist) SplitCombined(string title, string artist)
    {
        var cleaned = PlayerSuffix().Replace(FileExtension().Replace(title.Trim(), ""), "");
        if (!string.IsNullOrWhiteSpace(artist))
        {
            var a = artist.Trim();
            if (cleaned.StartsWith(a + " - ", StringComparison.Ordinal))
            {
                return (cleaned[(a.Length + 3)..].Trim(), a);
            }

            return (cleaned, a);
        }

        int i = cleaned.IndexOf(" - ", StringComparison.Ordinal);
        if (i > 0 && i < cleaned.Length - 3)
        {
            return (cleaned[(i + 3)..].Trim(), cleaned[..i].Trim());
        }

        return (cleaned, artist);
    }

    /// <summary>Lower-case, width-folded, punctuation-free text for comparisons.</summary>
    public static string Fold(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var normalised = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var sb = new StringBuilder(normalised.Length);
        foreach (var c in normalised.Normalize(NormalizationForm.FormD))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public static string MatchKey(string title, string artist, string album) =>
        string.Join('|', Fold(CleanTitle(title)), Fold(PrimaryArtist(artist)), Fold(album));

    /// <summary>0..1 similarity used to rank search results against the playing track.</summary>
    public static double Score(string title, string artist, long? durationMs, string candTitle, string candArtist, long? candDurationMs)
    {
        double titleScore = Similarity(Fold(SearchTitle(title)), Fold(SearchTitle(candTitle)));
        double artistScore = string.IsNullOrWhiteSpace(artist)
            ? 0.5
            : Similarity(Fold(PrimaryArtist(artist)), Fold(PrimaryArtist(candArtist)));

        double durationScore = 0.5;
        if (durationMs is > 0 && candDurationMs is > 0)
        {
            double diff = Math.Abs(durationMs.Value - candDurationMs.Value) / 1000.0;
            durationScore = diff <= 2 ? 1 : diff <= 5 ? 0.7 : diff <= 15 ? 0.3 : 0;
        }

        return titleScore * 0.5 + artistScore * 0.3 + durationScore * 0.2;
    }

    /// <summary>True when title (and artist, if both present) match after search-normalisation.</summary>
    public static bool SameSong(string title, string artist, string candTitle, string candArtist)
    {
        if (Fold(SearchTitle(title)) != Fold(SearchTitle(candTitle)))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(candArtist))
        {
            return true;
        }

        return Fold(PrimaryArtist(artist)) == Fold(PrimaryArtist(candArtist));
    }

    private static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0)
        {
            return 1;
        }

        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (a == b)
        {
            return 1;
        }

        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
        {
            return 0.85;
        }

        int distance = Levenshtein(a, b);
        return Math.Max(0, 1 - (double)distance / Math.Max(a.Length, b.Length));
    }

    private static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }
}
