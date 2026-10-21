using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;
using LyricClone.Infrastructure.Lyrics;
using LyricClone.Tests.Shared;

namespace LyricClone.Infrastructure.Tests;

// Fixtures are made-up sample text, not real song lyrics.
public sealed class EmbeddedLyricsTests : IDisposable
{
    private const string Lrc = "[00:01.00]编的第一行\n[00:04.50]编的第二行";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lyric-clone-tests", Guid.NewGuid().ToString("N"));

    public EmbeddedLyricsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static AudioTags Read(byte[] bytes) => AudioTagReader.Read(new MemoryStream(bytes))!;

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Id3_text_frames_and_utf16_uslt_are_read_and_cover_art_is_skipped(int major)
    {
        var bytes = new TestAudio.Id3(major)
            .Text("TIT2", "示例歌名")
            .Picture(200_000)
            .Text("TPE1", "示例乐队", encoding: 1)
            .Text("TALB", "Sample Album", encoding: 0)
            .Text("TLEN", "201500", encoding: 0)
            .Uslt(Lrc)
            .Build();

        var tags = Read(bytes);

        Assert.Equal("示例歌名", tags.Title);
        Assert.Equal("示例乐队", tags.Artist);
        Assert.Equal("Sample Album", tags.Album);
        Assert.Equal(201_500, tags.DurationMs);
        Assert.Equal(Lrc, tags.Lyrics);
        Assert.True(tags.HasLyrics);
    }

    [Fact]
    public void Id3_tag_level_unsynchronisation_is_undone()
    {
        var bytes = new TestAudio.Id3(3)
            .Text("TIT2", "Made-up Title")
            .Frame("PRIV", [0xFF, 0xE0, 0xFF, 0x00, 0x12])
            .Uslt("plain made-up words", encoding: 3)
            .Build(unsync: true);

        var tags = Read(bytes);

        Assert.Equal("Made-up Title", tags.Title);
        Assert.Equal("plain made-up words", tags.Lyrics);
    }

    [Fact]
    public void Sylt_one_entry_per_line_becomes_line_lrc()
    {
        var tags = Read(new TestAudio.Id3().Text("TIT2", "T").Sylt([("第一行", 1000u), ("第二行", 4500u)]).Build());

        var candidate = EmbeddedLyricsProvider.ToCandidate(tags);
        var parsed = LrcParser.Parse(candidate.Content!);

        Assert.Equal(LyricsFormat.Lrc, candidate.Format);
        Assert.Equal(LyricsSources.Embedded, candidate.Source);
        Assert.Equal(["第一行", "第二行"], parsed.Lines.Select(l => l.Text));
        Assert.Equal(4500, parsed.Lines[1].StartMs);
    }

    [Fact]
    public void Sylt_word_entries_with_line_breaks_become_enhanced_lrc()
    {
        var tags = Read(new TestAudio.Id3().Text("TIT2", "T")
            .Sylt([("编", 1000u), ("的", 1300u), ("\n第", 3000u), ("二", 3400u)]).Build());

        var candidate = EmbeddedLyricsProvider.ToCandidate(tags);
        var parsed = LrcParser.Parse(candidate.Content!);

        Assert.True(candidate.HasWordSync);
        Assert.Equal(2, parsed.Lines.Count);
        Assert.Equal("编的", parsed.Lines[0].Text);
        Assert.True(parsed.Lines[1].HasSyllables);
        Assert.Equal(3400, parsed.Lines[1].Syllables[1].StartMs);
    }

    [Fact]
    public void Flac_vorbis_comments_and_duration_are_read()
    {
        var tags = Read(TestAudio.Flac([("TITLE", "Sample Song"), ("ARTIST", "A"), ("ARTIST", "B"), ("ALBUM", "X"), ("LYRICS", Lrc)]));

        Assert.Equal("Sample Song", tags.Title);
        Assert.Equal("A/B", tags.Artist);
        Assert.Equal(200_000, tags.DurationMs);
        Assert.Equal(Lrc, tags.Lyrics);
    }

    [Fact]
    public void Mp4_ilst_atoms_and_duration_are_read()
    {
        var tags = Read(TestAudio.Mp4("Sample Song", "Sample Band", "Sample Album", "plain made-up words\r\nsecond line"));

        Assert.Equal("Sample Song", tags.Title);
        Assert.Equal("Sample Band", tags.Artist);
        Assert.Equal(200_000, tags.DurationMs);
        Assert.Equal("plain made-up words\nsecond line", tags.Lyrics);
        Assert.Equal(LyricsFormat.Plain, EmbeddedLyricsProvider.ToCandidate(tags).Format);
    }

    [Fact]
    public void Garbage_and_truncated_files_return_null_instead_of_throwing()
    {
        File.WriteAllBytes(Path.Combine(_dir, "junk.mp3"), [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13]);
        var full = new TestAudio.Id3().Text("TIT2", "T").Uslt(Lrc).Build();
        File.WriteAllBytes(Path.Combine(_dir, "cut.mp3"), full[..30]);

        Assert.Null(AudioTagReader.Read(Path.Combine(_dir, "junk.mp3")));
        var cut = AudioTagReader.Read(Path.Combine(_dir, "cut.mp3"));
        Assert.True(cut is null || !cut.HasLyrics);
    }

    [Fact]
    public async Task Provider_matches_the_playing_track_by_tags_not_file_name()
    {
        var sub = Directory.CreateDirectory(Path.Combine(_dir, "Album"));
        File.WriteAllBytes(Path.Combine(sub.FullName, "track01.mp3"),
            new TestAudio.Id3().Text("TIT2", "示例歌名").Text("TPE1", "示例乐队").Uslt(Lrc).Build());
        File.WriteAllBytes(Path.Combine(_dir, "other.flac"),
            TestAudio.Flac([("TITLE", "示例歌名"), ("ARTIST", "别的乐队"), ("LYRICS", "[00:01.00]不该被选中")]));
        File.WriteAllBytes(Path.Combine(_dir, "nolyrics.m4a"), TestAudio.Mp4("示例歌名", "示例乐队", "", null));

        var provider = new EmbeddedLyricsProvider(() => _dir);
        var found = await provider.FindAsync("示例歌名 (Live)", "示例乐队 feat. 某人", 200_000, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(Lrc, found.Content);
        Assert.Equal(LyricsSources.Embedded, found.Source);
        Assert.Equal(2, provider.IndexedCount);
    }

    [Fact]
    public async Task Provider_without_tags_falls_back_to_artist_dash_title_file_name()
    {
        File.WriteAllBytes(Path.Combine(_dir, "Sample Band - Sample Song.mp3"), new TestAudio.Id3().Uslt(Lrc).Build());

        var found = await new EmbeddedLyricsProvider(() => _dir).FindAsync("Sample Song", "Sample Band", null, CancellationToken.None);

        Assert.NotNull(found);
    }

    [Fact]
    public async Task Provider_picks_up_new_files_after_invalidate_and_ignores_missing_folder()
    {
        var provider = new EmbeddedLyricsProvider(() => _dir);
        Assert.Null(await provider.FindAsync("Sample Song", "Sample Band", null, CancellationToken.None));

        File.WriteAllBytes(Path.Combine(_dir, "a.m4a"), TestAudio.Mp4("Sample Song", "Sample Band", "", Lrc));
        provider.Invalidate();

        Assert.NotNull(await provider.FindAsync("Sample Song", "Sample Band", null, CancellationToken.None));
        Assert.Null(await new EmbeddedLyricsProvider(() => Path.Combine(_dir, "nope")).FindAsync("Sample Song", "Sample Band", null, CancellationToken.None));
    }
}
