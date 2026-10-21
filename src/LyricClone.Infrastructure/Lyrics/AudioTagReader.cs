using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace LyricClone.Infrastructure.Lyrics;

public readonly record struct SyncedText(long TimeMs, string Text);

/// <summary>Tags read from one audio file. <see cref="Lyrics"/> is unsynced text (often LRC); <see cref="Synced"/> is an ID3 SYLT frame.</summary>
public sealed record AudioTags(
    string Title,
    string Artist,
    string Album,
    long? DurationMs,
    string? Lyrics,
    IReadOnlyList<SyncedText>? Synced)
{
    public bool HasLyrics => !string.IsNullOrWhiteSpace(Lyrics) || Synced is { Count: > 0 };
}

/// <summary>
/// Minimal reader for the tags lyric files actually use: ID3v2.3 / 2.4 (TIT2, TPE1, TALB, TLEN, USLT, SYLT),
/// FLAC Vorbis comments (TITLE, ARTIST, ALBUM, LYRICS, UNSYNCEDLYRICS) and MP4 ilst atoms (©nam, ©ART, ©alb, ©lyr).
/// Large frames such as cover art are skipped with a seek, so indexing a library does not read whole files.
/// </summary>
public static class AudioTagReader
{
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".flac", ".m4a", ".mp4", ".aac" };

    private const int MaxTextFrame = 1 << 20;

    public static AudioTags? Read(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16 * 1024);
            return Read(fs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    public static AudioTags? Read(Stream s)
    {
        Span<byte> head = stackalloc byte[12];
        if (ReadFully(s, head) < 12)
        {
            return null;
        }

        s.Position = 0;
        if (head[0] == 'I' && head[1] == 'D' && head[2] == '3')
        {
            return ReadId3(s);
        }

        if (head[0] == 'f' && head[1] == 'L' && head[2] == 'a' && head[3] == 'C')
        {
            return ReadFlac(s);
        }

        if (head[4] == 'f' && head[5] == 't' && head[6] == 'y' && head[7] == 'p')
        {
            return ReadMp4(s);
        }

        return null;
    }

    // ID3v2

    private static AudioTags? ReadId3(Stream s)
    {
        var header = ReadBytes(s, 10);
        int major = header[3];
        if (major is not (3 or 4))
        {
            return null;
        }

        byte flags = header[5];
        int size = SyncSafe(header.AsSpan(6, 4));
        long end = 10 + size;

        Stream body = s;
        if ((flags & 0x80) != 0)
        {
            body = new MemoryStream(RemoveUnsync(ReadBytes(s, size)));
            end = body.Length;
        }

        if ((flags & 0x40) != 0)
        {
            var ext = ReadBytes(body, 4);
            int extSize = major == 4 ? SyncSafe(ext) - 4 : BinaryPrimitives.ReadInt32BigEndian(ext);
            body.Position += Math.Max(0, extSize);
        }

        string title = "", artist = "", album = "";
        long? duration = null;
        string? lyrics = null;
        List<SyncedText>? synced = null;

        while (body.Position + 10 <= end)
        {
            var fh = ReadBytes(body, 10);
            if (fh[0] == 0)
            {
                break;
            }

            var id = Encoding.ASCII.GetString(fh, 0, 4);
            int frameSize = major == 4 ? SyncSafe(fh.AsSpan(4, 4)) : BinaryPrimitives.ReadInt32BigEndian(fh.AsSpan(4, 4));
            long next = body.Position + frameSize;
            if (frameSize <= 0 || next > end)
            {
                break;
            }

            bool wanted = id is "TIT2" or "TPE1" or "TALB" or "TLEN" or "USLT" or "SYLT";
            byte format = fh[9];
            bool compressedOrEncrypted = major == 4 ? (format & 0x0C) != 0 : (format & 0xC0) != 0;
            if (!wanted || compressedOrEncrypted || frameSize > MaxTextFrame)
            {
                body.Position = next;
                continue;
            }

            var data = ReadBytes(body, frameSize);
            int skip = 0;
            if (major == 4)
            {
                if ((format & 0x40) != 0)
                {
                    skip += 1;
                }

                if ((format & 0x01) != 0)
                {
                    skip += 4;
                }

                if ((format & 0x02) != 0)
                {
                    data = RemoveUnsync(data[skip..]);
                    skip = 0;
                }
            }
            else if ((format & 0x20) != 0)
            {
                skip += 1;
            }

            var span = data.AsSpan(Math.Min(skip, data.Length));
            switch (id)
            {
                case "TIT2":
                    title = TextFrame(span);
                    break;
                case "TPE1":
                    artist = TextFrame(span);
                    break;
                case "TALB":
                    album = TextFrame(span);
                    break;
                case "TLEN":
                    if (long.TryParse(TextFrame(span), NumberStyles.None, CultureInfo.InvariantCulture, out var ms) && ms > 0)
                    {
                        duration = ms;
                    }

                    break;
                case "USLT" when lyrics is null:
                    lyrics = UsltFrame(span);
                    break;
                case "SYLT" when synced is null:
                    synced = SyltFrame(span);
                    break;
            }

            body.Position = next;
        }

        return new AudioTags(title, artist, album, duration, string.IsNullOrWhiteSpace(lyrics) ? null : lyrics, synced is { Count: > 0 } ? synced : null);
    }

    private static string TextFrame(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1)
        {
            return "";
        }

        var text = Decode(data[0], data[1..]);
        var parts = text.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join("/", parts);
    }

    private static string? UsltFrame(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5)
        {
            return null;
        }

        byte enc = data[0];
        var rest = data[4..];
        int descEnd = Terminator(rest, enc, out int termLen);
        if (descEnd < 0)
        {
            return null;
        }

        return Decode(enc, rest[(descEnd + termLen)..]).TrimEnd('\0').Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static List<SyncedText>? SyltFrame(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6)
        {
            return null;
        }

        byte enc = data[0];
        byte timeFormat = data[4];
        if (timeFormat != 2)
        {
            return null;
        }

        var rest = data[6..];
        int descEnd = Terminator(rest, enc, out int termLen);
        if (descEnd < 0)
        {
            return null;
        }

        rest = rest[(descEnd + termLen)..];
        var list = new List<SyncedText>();
        while (rest.Length > 0)
        {
            int textEnd = Terminator(rest, enc, out termLen);
            if (textEnd < 0 || textEnd + termLen + 4 > rest.Length)
            {
                break;
            }

            var text = Decode(enc, rest[..textEnd]);
            long time = BinaryPrimitives.ReadUInt32BigEndian(rest.Slice(textEnd + termLen, 4));
            list.Add(new SyncedText(time, text));
            rest = rest[(textEnd + termLen + 4)..];
        }

        return list;
    }

    /// <summary>Index of the string terminator for the encoding: one zero byte, or two at an even offset for UTF-16.</summary>
    private static int Terminator(ReadOnlySpan<byte> data, byte enc, out int length)
    {
        if (enc is 1 or 2)
        {
            length = 2;
            for (int i = 0; i + 1 < data.Length; i += 2)
            {
                if (data[i] == 0 && data[i + 1] == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        length = 1;
        return data.IndexOf((byte)0);
    }

    private static string Decode(byte enc, ReadOnlySpan<byte> bytes)
    {
        switch (enc)
        {
            case 1:
                if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                {
                    return Encoding.BigEndianUnicode.GetString(bytes[2..]);
                }

                return Encoding.Unicode.GetString(bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE ? bytes[2..] : bytes);
            case 2:
                return Encoding.BigEndianUnicode.GetString(bytes);
            case 3:
                return Encoding.UTF8.GetString(bytes);
            default:
                return DecodeLegacy(bytes);
        }
    }

    /// <summary>
    /// Encoding 0 is Latin-1 by the spec, but many taggers on Chinese systems wrote GBK there and others wrote UTF-8.
    /// ASCII stays ASCII; valid UTF-8 is taken as UTF-8; otherwise the system ANSI code page.
    /// </summary>
    private static string DecodeLegacy(ReadOnlySpan<byte> bytes)
    {
        bool ascii = true;
        foreach (var b in bytes)
        {
            if (b >= 0x80)
            {
                ascii = false;
                break;
            }
        }

        if (ascii)
        {
            return Encoding.ASCII.GetString(bytes);
        }

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var cp = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
            return Encoding.GetEncoding(cp == 1252 ? 28591 : cp).GetString(bytes);
        }
    }

    private static byte[] RemoveUnsync(byte[] data)
    {
        var output = new byte[data.Length];
        int n = 0;
        for (int i = 0; i < data.Length; i++)
        {
            output[n++] = data[i];
            if (data[i] == 0xFF && i + 1 < data.Length && data[i + 1] == 0x00)
            {
                i++;
            }
        }

        return output[..n];
    }

    private static int SyncSafe(ReadOnlySpan<byte> b) => b[0] << 21 | b[1] << 14 | b[2] << 7 | b[3];

    // FLAC

    private static AudioTags? ReadFlac(Stream s)
    {
        s.Position = 4;
        long? duration = null;
        var comments = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        bool last = false;
        while (!last && s.Position + 4 <= s.Length)
        {
            var bh = ReadBytes(s, 4);
            last = (bh[0] & 0x80) != 0;
            int type = bh[0] & 0x7F;
            int length = bh[1] << 16 | bh[2] << 8 | bh[3];
            long next = s.Position + length;
            if (type == 0 && length >= 18)
            {
                var si = ReadBytes(s, 18);
                int rate = si[10] << 12 | si[11] << 4 | si[12] >> 4;
                long samples = (long)(si[13] & 0x0F) << 32 | BinaryPrimitives.ReadUInt32BigEndian(si.AsSpan(14, 4));
                if (rate > 0 && samples > 0)
                {
                    duration = samples * 1000 / rate;
                }
            }
            else if (type == 4 && length <= 16 * MaxTextFrame)
            {
                ReadVorbisComments(ReadBytes(s, length), comments);
            }

            s.Position = next;
        }

        string Get(string key) => comments.TryGetValue(key, out var v) ? string.Join("/", v) : "";
        var lyrics = comments.TryGetValue("LYRICS", out var l) ? l[0] : comments.TryGetValue("UNSYNCEDLYRICS", out var u) ? u[0] : null;
        return new AudioTags(Get("TITLE"), Get("ARTIST"), Get("ALBUM"), duration, string.IsNullOrWhiteSpace(lyrics) ? null : lyrics.Replace("\r\n", "\n"), null);
    }

    private static void ReadVorbisComments(ReadOnlySpan<byte> block, Dictionary<string, List<string>> into)
    {
        int p = 0;
        if (block.Length < 8)
        {
            return;
        }

        int vendor = BinaryPrimitives.ReadInt32LittleEndian(block);
        p = 4 + vendor;
        if (vendor < 0 || p + 4 > block.Length)
        {
            return;
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(block[p..]);
        p += 4;
        for (int i = 0; i < count && p + 4 <= block.Length; i++)
        {
            int len = BinaryPrimitives.ReadInt32LittleEndian(block[p..]);
            p += 4;
            if (len < 0 || p + len > block.Length)
            {
                return;
            }

            var entry = Encoding.UTF8.GetString(block.Slice(p, len));
            p += len;
            int eq = entry.IndexOf('=');
            if (eq > 0)
            {
                var key = entry[..eq];
                if (!into.TryGetValue(key, out var list))
                {
                    into[key] = list = [];
                }

                list.Add(entry[(eq + 1)..]);
            }
        }
    }

    // MP4

    private static AudioTags? ReadMp4(Stream s)
    {
        string title = "", artist = "", album = "";
        string? lyrics = null;
        long? duration = null;

        foreach (var (type, start, end) in Boxes(s, 0, s.Length))
        {
            if (type != "moov")
            {
                continue;
            }

            foreach (var (t, st, en) in Boxes(s, start, end))
            {
                if (t == "mvhd")
                {
                    duration = MvhdDuration(s, st);
                }
                else if (t == "udta")
                {
                    foreach (var (ut, us, ue) in Boxes(s, st, en))
                    {
                        if (ut != "meta")
                        {
                            continue;
                        }

                        long metaStart = MetaChildrenStart(s, us);
                        foreach (var (mt, ms, me) in Boxes(s, metaStart, ue))
                        {
                            if (mt != "ilst")
                            {
                                continue;
                            }

                            foreach (var (it, ist, ien) in Boxes(s, ms, me))
                            {
                                switch (it)
                                {
                                    case "\u00A9nam":
                                        title = DataAtom(s, ist, ien) ?? title;
                                        break;
                                    case "\u00A9ART":
                                        artist = DataAtom(s, ist, ien) ?? artist;
                                        break;
                                    case "\u00A9alb":
                                        album = DataAtom(s, ist, ien) ?? album;
                                        break;
                                    case "\u00A9lyr":
                                        lyrics = DataAtom(s, ist, ien);
                                        break;
                                }
                            }
                        }
                    }
                }
            }
        }

        return new AudioTags(title, artist, album, duration, string.IsNullOrWhiteSpace(lyrics) ? null : lyrics.Replace("\r\n", "\n").Replace('\r', '\n'), null);
    }

    /// <summary>Children of a box between <paramref name="start"/> and <paramref name="end"/>: type, content start, content end.</summary>
    private static IEnumerable<(string Type, long Start, long End)> Boxes(Stream s, long start, long end)
    {
        long p = start;
        while (p + 8 <= end)
        {
            s.Position = p;
            var h = ReadBytes(s, 8);
            long size = BinaryPrimitives.ReadUInt32BigEndian(h);
            var type = Encoding.Latin1.GetString(h, 4, 4);
            long contentStart = p + 8;
            if (size == 1)
            {
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(ReadBytes(s, 8));
                contentStart += 8;
            }
            else if (size == 0)
            {
                size = end - p;
            }

            if (size < contentStart - p || p + size > end)
            {
                yield break;
            }

            yield return (type, contentStart, p + size);
            p += size;
        }
    }

    /// <summary>"meta" is a full box (4 bytes version / flags) in MP4 but not in some QuickTime files.</summary>
    private static long MetaChildrenStart(Stream s, long contentStart)
    {
        s.Position = contentStart + 4;
        var probe = ReadBytes(s, 4);
        return Encoding.Latin1.GetString(probe) == "hdlr" ? contentStart : contentStart + 4;
    }

    private static long? MvhdDuration(Stream s, long start)
    {
        s.Position = start;
        var v = ReadBytes(s, 4)[0];
        if (v == 1)
        {
            s.Position += 16;
            uint scale = BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(s, 4));
            ulong d = BinaryPrimitives.ReadUInt64BigEndian(ReadBytes(s, 8));
            return scale > 0 ? (long)(d * 1000 / scale) : null;
        }

        s.Position += 8;
        uint timescale = BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(s, 4));
        uint dur = BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(s, 4));
        return timescale > 0 && dur > 0 ? (long)dur * 1000 / timescale : null;
    }

    private static string? DataAtom(Stream s, long start, long end)
    {
        foreach (var (t, st, en) in Boxes(s, start, end))
        {
            if (t == "data" && en - st >= 8 && en - st <= MaxTextFrame)
            {
                s.Position = st + 8;
                return Encoding.UTF8.GetString(ReadBytes(s, (int)(en - st - 8)));
            }
        }

        return null;
    }

    // IO

    private static byte[] ReadBytes(Stream s, int count)
    {
        var buffer = new byte[count];
        if (ReadFully(s, buffer) != count)
        {
            throw new InvalidDataException("Unexpected end of file");
        }

        return buffer;
    }

    private static int ReadFully(Stream s, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = s.Read(buffer[total..]);
            if (n == 0)
            {
                break;
            }

            total += n;
        }

        return total;
    }
}
