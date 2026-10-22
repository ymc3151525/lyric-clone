using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;
using Lyricify.Lyrics.Decrypter.Qrc;

namespace LyricClone.Infrastructure.Lyrics;

/// <summary>
/// Reads lyrics the user already cached in QQ Music / NetEase Cloud Music / Kugou on this machine.
/// No unofficial player HTTP APIs — only files in well-known local folders.
/// </summary>
public sealed class PlayerCacheProvider
{
    private const double MinScore = 0.72;
    private const int MinUsefulBytes = 100;

    private static readonly byte[] QmcMagic = [0x98, 0x25, 0xB0, 0xAC, 0xE3, 0x02, 0x83, 0x68, 0xE8, 0xFC, 0x6C];

    private static readonly byte[] Qmc1Key =
    [
        0xc3, 0x4a, 0xd6, 0xca, 0x90, 0x67, 0xf7, 0x52, 0xd8, 0xa1, 0x66, 0x62, 0x9f, 0x5b, 0x09, 0x00,
        0xc3, 0x5e, 0x95, 0x23, 0x9f, 0x13, 0x11, 0x7e, 0xd8, 0x92, 0x3f, 0xbc, 0x90, 0xbb, 0x74, 0x0e,
        0xc3, 0x47, 0x74, 0x3d, 0x90, 0xaa, 0x3f, 0x51, 0xd8, 0xf4, 0x11, 0x84, 0x9f, 0xde, 0x95, 0x1d,
        0xc3, 0xc6, 0x09, 0xd5, 0x9f, 0xfa, 0x66, 0xf9, 0xd8, 0xf0, 0xf7, 0xa0, 0x90, 0xa1, 0xd6, 0xf3,
        0xc3, 0xf3, 0xd6, 0xa1, 0x90, 0xa0, 0xf7, 0xf0, 0xd8, 0xf9, 0x66, 0xfa, 0x9f, 0xd5, 0x09, 0xc6,
        0xc3, 0x1d, 0x95, 0xde, 0x9f, 0x84, 0x11, 0xf4, 0xd8, 0x51, 0x3f, 0xaa, 0x90, 0x3d, 0x74, 0x47,
        0xc3, 0x0e, 0x74, 0xbb, 0x90, 0xbc, 0x3f, 0x92, 0xd8, 0x7e, 0x11, 0x13, 0x9f, 0x23, 0x95, 0x5e,
        0xc3, 0x00, 0x09, 0x5b, 0x9f, 0x62, 0x66, 0xa1, 0xd8, 0x52, 0xf7, 0x67, 0x90, 0xca, 0xd6, 0x4a,
    ];

    private static readonly string[] LyricExtensions = [".qrc", ".krc", ".yrc", ".lrc", ".xml"];

    private static readonly Regex QqName = new(
        @"^(?<artist>.+?) - (?<title>.+?) - (?<dur>\d+) - (?<album>.+)_(?<kind>qmts|qmRoma|qm)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly Func<string, IReadOnlyList<string>> _folders;

    public PlayerCacheProvider(Func<string, IReadOnlyList<string>>? folders = null)
    {
        _folders = folders ?? DiscoverFolders;
    }

    public bool HasFolders(string source) => _folders(source).Any(d => !string.IsNullOrWhiteSpace(d) && Directory.Exists(d));

    public LyricsCandidate? Find(string source, Track track, IReadOnlyList<string>? folders = null)
    {
        var roots = folders ?? _folders(source);
        var ranked = new List<(string File, CacheFileInfo Info, double Score)>();
        var titleHits = new List<(string File, CacheFileInfo Info)>();
        var wantedTitle = TrackNormalizer.Fold(TrackNormalizer.SearchTitle(track.Title));

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in EnumerateLyricFiles(root))
            {
                if (!TryDescribe(file, source, out var info) || info.Kind is CacheKind.Translation or CacheKind.Roma)
                {
                    continue;
                }

                var score = Score(track, info);
                bool same = TrackNormalizer.SameSong(track.Title, track.Artist, info.Title, info.Artist);
                if (score >= MinScore || (same && score >= 0.5))
                {
                    ranked.Add((file, info, score));
                }
                else if (wantedTitle.Length > 0
                    && TrackNormalizer.Fold(TrackNormalizer.SearchTitle(info.Title)) == wantedTitle)
                {
                    titleHits.Add((file, info));
                }
            }
        }

        foreach (var row in ranked.OrderByDescending(x => x.Score))
        {
            var candidate = ReadCandidate(source, row.File, row.Info);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        if (titleHits.Count == 1)
        {
            return ReadCandidate(source, titleHits[0].File, titleHits[0].Info);
        }

        return null;
    }

    private static LyricsCandidate? ReadCandidate(string source, string file, CacheFileInfo info)
    {
        var content = ReadLyricFile(file);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        string? translation = null;
        if (LyricScript.IsEnglishMain(content) && info.TranslationPath is { } trPath && File.Exists(trPath))
        {
            var tr = ReadLyricFile(trPath);
            if (!string.IsNullOrWhiteSpace(tr))
            {
                translation = tr;
            }
        }

        var format = info.Format;
        if (format == LyricsFormat.Qrc && LrcParser.LooksLikeLrc(content))
        {
            format = LyricsFormat.Lrc;
        }

        return new LyricsCandidate(
            source,
            Path.GetFileName(file),
            info.Title,
            info.Artist,
            info.Album,
            info.DurationMs,
            format,
            content,
            translation,
            false,
            format is LyricsFormat.Qrc or LyricsFormat.Krc or LyricsFormat.Yrc);
    }

    public static IReadOnlyList<string> DiscoverFolders(string source) => source switch
    {
        LyricsSources.QqCache => DiscoverQq(),
        LyricsSources.NeteaseCache => Existing(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Netease", "CloudMusic", "webdata", "lyric"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Netease", "CloudMusic", "webdata", "file"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Netease", "CloudMusic", "webdata", "lyric")),
        LyricsSources.KugouCache => Existing(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KuGou8", "Lyrics"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kugou", "Lyrics"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KuGou7", "Lyrics")),
        _ => [],
    };

    internal static bool TryParseQqName(string fileName, out CacheFileInfo info)
    {
        info = default!;
        var match = QqName.Match(Path.GetFileNameWithoutExtension(fileName));
        if (!match.Success)
        {
            return false;
        }

        var artist = match.Groups["artist"].Value.Replace('_', '/');
        var title = match.Groups["title"].Value;
        var album = match.Groups["album"].Value;
        long? duration = long.TryParse(match.Groups["dur"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long sec)
            ? sec * 1000
            : null;
        var kind = match.Groups["kind"].Value switch
        {
            "qmts" => CacheKind.Translation,
            "qmRoma" => CacheKind.Roma,
            _ => CacheKind.Main,
        };
        info = new CacheFileInfo(artist, title, album, duration, LyricsFormat.Qrc, kind, TranslationPath: null);
        return true;
    }

    internal static string ReadLyricFile(string path)
    {
        var bytes = UnwrapLocalQmc(File.ReadAllBytes(path));
        if (bytes.Length == 0)
        {
            return "";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes[0] is (byte)'[' or (byte)'<')
        {
            return Encoding.UTF8.GetString(bytes);
        }

        if (bytes.Length < MinUsefulBytes)
        {
            return "";
        }

        if (IsAsciiHex(bytes))
        {
            return DecryptQrcHex(Encoding.ASCII.GetString(bytes).Trim());
        }

        return DecryptQrcHex(Convert.ToHexString(bytes));
    }

    private static string DecryptQrcHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return "";
        }

        try
        {
            var decrypted = Decrypter.DecryptLyrics(hex);
            return string.IsNullOrWhiteSpace(decrypted) ? "" : ExtractLyricContent(decrypted);
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static readonly Regex LyricContentAttr = new(
        @"LyricContent\s*=\s*""([\s\S]*)""\s*/?>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// QQ cache decrypts to a small XML wrapper; the actual QRC sits on LyricContent.
    /// Desktop files often leave raw newlines and '&lt;' in that attribute, so LoadXml fails.
    /// </summary>
    internal static string ExtractLyricContent(string text)
    {
        var t = text.TrimStart();
        if (t.Length == 0 || t[0] != '<' || t.IndexOf("LyricContent", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return text;
        }

        if (TryLoadXmlLyricContent(t, out var inner))
        {
            return inner;
        }

        var match = LyricContentAttr.Match(t);
        if (!match.Success)
        {
            return text;
        }

        inner = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
        return string.IsNullOrWhiteSpace(inner) ? text : inner;
    }

    private static bool TryLoadXmlLyricContent(string xml, out string inner)
    {
        inner = "";
        try
        {
            var doc = new XmlDocument { XmlResolver = null };
            doc.LoadXml(xml);
            var value = doc.SelectSingleNode("//*[@LyricContent]")?.Attributes?["LyricContent"]?.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            inner = value;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Desktop QQ Music wraps cached QRC in the public QMC1 stream (magic + XOR).
    /// After unwrapping, the remaining bytes are the same 3DES+zlib blob the Helper decrypts.
    /// </summary>
    internal static byte[] UnwrapLocalQmc(byte[] data)
    {
        if (data.Length < QmcMagic.Length || !data.AsSpan(0, QmcMagic.Length).SequenceEqual(QmcMagic))
        {
            return data;
        }

        var copy = (byte[])data.Clone();
        for (int i = 0; i < copy.Length; i++)
        {
            int offset = i > 0x7FFF ? i % 0x7FFF : i;
            copy[i] ^= Qmc1Key[offset & 0x7F];
        }

        return copy[QmcMagic.Length..];
    }

    private static bool TryDescribe(string file, string source, out CacheFileInfo info)
    {
        if (source == LyricsSources.QqCache && TryParseQqName(file, out info))
        {
            if (info.Kind == CacheKind.Main)
            {
                var dir = Path.GetDirectoryName(file) ?? "";
                var stem = Path.GetFileNameWithoutExtension(file);
                if (stem.EndsWith("_qm", StringComparison.Ordinal))
                {
                    stem = stem[..^3];
                }

                var tr = Path.Combine(dir, stem + "_qmts.qrc");
                info = info with { TranslationPath = File.Exists(tr) ? tr : null };
            }

            return true;
        }

        var ext = Path.GetExtension(file).ToLowerInvariant();
        var name = Path.GetFileNameWithoutExtension(file);
        if (!TrySplitArtistTitle(name, out var artist, out var title))
        {
            title = name;
            artist = "";
        }

        info = new CacheFileInfo(artist, title, "", null, FormatOf(ext), CacheKind.Main, null);
        return title.Length > 0;
    }

    private static double Score(Track track, CacheFileInfo info)
    {
        var artist = string.IsNullOrWhiteSpace(info.Artist) ? track.Artist : info.Artist;
        return TrackNormalizer.Score(track.Title, track.Artist, track.DurationMs, info.Title, artist, info.DurationMs);
    }

    private static IEnumerable<string> EnumerateLyricFiles(string root)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (LyricExtensions.Contains(ext))
            {
                yield return file;
            }
        }
    }

    private static IReadOnlyList<string> DiscoverQq()
    {
        var found = new List<string>();
        var ini = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tencent", "QQMusic", "WebkitCachePath.ini");
        if (File.Exists(ini))
        {
            foreach (var line in File.ReadLines(ini))
            {
                const string prefix = "Path=";
                if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var webkit = line[prefix.Length..].Trim();
                var parent = Directory.GetParent(webkit)?.FullName;
                if (parent is not null)
                {
                    found.Add(Path.Combine(parent, "QQMusicLyricNew"));
                    found.Add(Path.Combine(parent, "QQMusicLyric"));
                }
            }
        }

        found.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tencent", "QQMusic", "Lyric"));
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            found.Add(Path.Combine(drive.RootDirectory.FullName, "QQMusicCache", "QQMusicLyricNew"));
        }

        return Existing([.. found]);
    }

    private static IReadOnlyList<string> Existing(params string[] paths) =>
        paths.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool TrySplitArtistTitle(string name, out string artist, out string title)
    {
        artist = "";
        title = name;
        int i = name.IndexOf(" - ", StringComparison.Ordinal);
        if (i <= 0 || i >= name.Length - 3)
        {
            return false;
        }

        artist = name[..i].Trim().Replace('_', '/');
        title = name[(i + 3)..].Trim();
        return artist.Length > 0 && title.Length > 0;
    }

    private static LyricsFormat FormatOf(string ext) => ext switch
    {
        ".lrc" => LyricsFormat.Lrc,
        ".qrc" => LyricsFormat.Qrc,
        ".krc" => LyricsFormat.Krc,
        ".yrc" => LyricsFormat.Yrc,
        _ => LyricsFormat.Ttml,
    };

    private static bool IsAsciiHex(byte[] bytes)
    {
        int n = 0;
        foreach (var b in bytes)
        {
            if (b is (byte)'\r' or (byte)'\n' or (byte)' ' or (byte)'\t')
            {
                continue;
            }

            bool hex = b is >= (byte)'0' and <= (byte)'9'
                or >= (byte)'a' and <= (byte)'f'
                or >= (byte)'A' and <= (byte)'F';
            if (!hex)
            {
                return false;
            }

            n++;
        }

        return n >= 16 && n % 2 == 0;
    }

    internal enum CacheKind
    {
        Main,
        Translation,
        Roma,
    }

    internal sealed record CacheFileInfo(
        string Artist,
        string Title,
        string Album,
        long? DurationMs,
        LyricsFormat Format,
        CacheKind Kind,
        string? TranslationPath);
}
