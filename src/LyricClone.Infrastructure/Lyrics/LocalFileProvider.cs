using System.Text;
using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;

namespace LyricClone.Infrastructure.Lyrics;

/// <summary>Finds lyric files in a user-chosen folder by "Artist - Title" or "Title" file names.</summary>
public sealed class LocalFileProvider
{
    private static readonly string[] Extensions = [".ttml", ".lrc", ".xml", ".qrc", ".krc", ".yrc", ".lys", ".lyl"];

    public LyricsCandidate? Find(string? folder, string title, string artist)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        var cleanTitle = TrackNormalizer.CleanTitle(title);
        var primary = TrackNormalizer.PrimaryArtist(artist);
        var wanted = new[]
        {
            TrackNormalizer.Fold($"{artist} - {title}"),
            TrackNormalizer.Fold($"{primary} - {cleanTitle}"),
            TrackNormalizer.Fold($"{cleanTitle} - {primary}"),
            TrackNormalizer.Fold(cleanTitle),
        };

        foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (!Extensions.Contains(ext))
            {
                continue;
            }

            var name = TrackNormalizer.Fold(Path.GetFileNameWithoutExtension(file));
            if (!wanted.Contains(name))
            {
                continue;
            }

            var content = ReadText(file);
            var format = FormatOf(ext);
            if (format == LyricsFormat.Lrc && !LrcParser.LooksLikeLrc(content))
            {
                continue;
            }

            return new LyricsCandidate(LyricsSources.LocalFile, null, cleanTitle, primary, "", null, format, content, null, false, false);
        }

        return null;
    }

    private static LyricsFormat FormatOf(string ext) => ext switch
    {
        ".lrc" => LyricsFormat.Lrc,
        ".qrc" => LyricsFormat.Qrc,
        ".krc" => LyricsFormat.Krc,
        ".yrc" => LyricsFormat.Yrc,
        ".lys" => LyricsFormat.LyricifySyllable,
        ".lyl" => LyricsFormat.LyricifyLines,
        _ => LyricsFormat.Ttml,
    };

    /// <summary>UTF-8 / UTF-16 by BOM, else UTF-8 if valid, else the system ANSI code page (GBK on zh-CN systems).</summary>
    public static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF)
            || bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var ansi = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
            return ansi.GetString(bytes);
        }
    }
}
