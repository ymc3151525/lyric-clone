using System.Buffers.Binary;
using System.Text;

namespace LyricClone.Tests.Shared;

/// <summary>
/// Builds tiny audio files byte by byte: just the tag structures plus a few bytes of fake audio.
/// Every text written by tests is made up.
/// </summary>
public static class TestAudio
{
    public sealed class Id3
    {
        private readonly List<byte[]> _frames = [];
        private readonly int _major;

        public Id3(int major = 3) => _major = major;

        public Id3 Text(string id, string value, byte encoding = 3) => Frame(id, [encoding, .. Encode(encoding, value)]);

        public Id3 Uslt(string text, byte encoding = 1, string description = "") =>
            Frame("USLT", [encoding, .. "zho"u8, .. Encode(encoding, description), .. Terminator(encoding), .. Encode(encoding, text)]);

        public Id3 Sylt(IEnumerable<(string Text, uint TimeMs)> entries, byte encoding = 3)
        {
            var body = new List<byte> { encoding };
            body.AddRange("zho"u8.ToArray());
            body.Add(2);
            body.Add(1);
            body.AddRange(Terminator(encoding));
            foreach (var (text, time) in entries)
            {
                body.AddRange(Encode(encoding, text));
                body.AddRange(Terminator(encoding));
                var t = new byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(t, time);
                body.AddRange(t);
            }

            return Frame("SYLT", [.. body]);
        }

        /// <summary>A cover-art sized frame the reader should skip without reading.</summary>
        public Id3 Picture(int size) => Frame("APIC", new byte[size]);

        public Id3 Frame(string id, byte[] data)
        {
            var h = new byte[10];
            Encoding.ASCII.GetBytes(id, h);
            if (_major == 4)
            {
                SyncSafe(data.Length, h.AsSpan(4));
            }
            else
            {
                BinaryPrimitives.WriteInt32BigEndian(h.AsSpan(4), data.Length);
            }

            _frames.Add([.. h, .. data]);
            return this;
        }

        public byte[] Build(bool unsync = false, int padding = 64)
        {
            byte[] body = [.. _frames.SelectMany(f => f), .. new byte[padding]];
            if (unsync)
            {
                var o = new List<byte>();
                for (int i = 0; i < body.Length; i++)
                {
                    o.Add(body[i]);
                    if (body[i] == 0xFF && (i + 1 == body.Length || body[i + 1] == 0 || body[i + 1] >= 0xE0))
                    {
                        o.Add(0);
                    }
                }

                body = [.. o];
            }

            var header = new byte[10];
            "ID3"u8.CopyTo(header);
            header[3] = (byte)_major;
            header[5] = (byte)(unsync ? 0x80 : 0);
            SyncSafe(body.Length, header.AsSpan(6));
            return [.. header, .. body, .. FakeMpegFrame()];
        }
    }

    public static byte[] Flac(IEnumerable<(string Key, string Value)> comments, long totalSamples = 44_100L * 200, int sampleRate = 44_100)
    {
        var info = new byte[34];
        info[10] = (byte)(sampleRate >> 12);
        info[11] = (byte)(sampleRate >> 4);
        info[12] = (byte)((sampleRate & 0xF) << 4 | 0x02);
        info[13] = (byte)(0xF0 | (totalSamples >> 32) & 0x0F);
        BinaryPrimitives.WriteUInt32BigEndian(info.AsSpan(14), (uint)totalSamples);

        var vc = new List<byte>();
        var vendor = "made-up encoder"u8.ToArray();
        vc.AddRange(Le(vendor.Length));
        vc.AddRange(vendor);
        var list = comments.ToList();
        vc.AddRange(Le(list.Count));
        foreach (var (k, v) in list)
        {
            var e = Encoding.UTF8.GetBytes($"{k}={v}");
            vc.AddRange(Le(e.Length));
            vc.AddRange(e);
        }

        return [.. "fLaC"u8, .. Block(0, info, last: false), .. Block(6, new byte[300], last: false), .. Block(4, [.. vc], last: true), .. new byte[16]];
    }

    public static byte[] Mp4(string title, string artist, string album, string? lyrics, uint timescale = 1000, uint duration = 200_000)
    {
        byte[] Data(string text) => Box("data", [0, 0, 0, 1, 0, 0, 0, 0, .. Encoding.UTF8.GetBytes(text)]);
        var items = new List<byte>();
        items.AddRange(Box("\u00A9nam", Data(title)));
        items.AddRange(Box("\u00A9ART", Data(artist)));
        items.AddRange(Box("\u00A9alb", Data(album)));
        items.AddRange(Box("covr", Box("data", [0, 0, 0, 13, 0, 0, 0, 0, .. new byte[500]])));
        if (lyrics is not null)
        {
            items.AddRange(Box("\u00A9lyr", Data(lyrics)));
        }

        var mvhd = new byte[100];
        BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(12), timescale);
        BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(16), duration);
        var hdlr = Box("hdlr", new byte[25]);
        var meta = Box("meta", [0, 0, 0, 0, .. hdlr, .. Box("ilst", [.. items])]);
        var moov = Box("moov", [.. Box("mvhd", mvhd), .. Box("trak", new byte[64]), .. Box("udta", meta)]);
        return [.. Box("ftyp", [.. "M4A "u8, 0, 0, 0, 0, .. "isomM4A "u8]), .. Box("mdat", new byte[256]), .. moov];
    }

    public static byte[] Box(string type, byte[] content)
    {
        var h = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(h, (uint)(content.Length + 8));
        Encoding.Latin1.GetBytes(type, h.AsSpan(4));
        return [.. h, .. content];
    }

    private static byte[] Block(int type, byte[] data, bool last) =>
        [(byte)((last ? 0x80 : 0) | type), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length, .. data];

    private static byte[] Le(int v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b, v);
        return b;
    }

    private static byte[] Encode(byte encoding, string text) => encoding switch
    {
        1 => [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(text)],
        2 => Encoding.BigEndianUnicode.GetBytes(text),
        3 => Encoding.UTF8.GetBytes(text),
        _ => Encoding.Latin1.GetBytes(text),
    };

    private static byte[] Terminator(byte encoding) => encoding is 1 or 2 ? [0, 0] : [0];

    private static void SyncSafe(int v, Span<byte> into)
    {
        into[0] = (byte)(v >> 21 & 0x7F);
        into[1] = (byte)(v >> 14 & 0x7F);
        into[2] = (byte)(v >> 7 & 0x7F);
        into[3] = (byte)(v & 0x7F);
    }

    private static byte[] FakeMpegFrame() => [0xFF, 0xFB, 0x90, 0x64, .. new byte[60]];
}
