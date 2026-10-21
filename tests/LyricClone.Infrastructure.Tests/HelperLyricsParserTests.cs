using LyricClone.Core.Lyrics;
using LyricClone.Infrastructure.Lyrics;

namespace LyricClone.Infrastructure.Tests;

/// <summary>Made-up timestamps and filler words — not real song lyrics.</summary>
public sealed class HelperLyricsParserTests
{
    private readonly HelperLyricsParser _parser = new();

    public const string Qrc = "[0,1500]one(0,700)two(700,800)\n";
    public const string Krc = "[0,1500]<0,700,0>one<700,800,0>two\n";
    public const string Yrc = "[0,1500,0](0,700,0)one(700,800,0)two\n";
    public const string Lines = "[0,1500]first line\n[1500,3200]second line\n";
    public const string Syllable = "[0]one(0,700)two(700,800)\n";

    [Theory]
    [InlineData(LyricsFormat.Qrc, Qrc, "onetwo")]
    [InlineData(LyricsFormat.Krc, Krc, "onetwo")]
    [InlineData(LyricsFormat.Yrc, Yrc, "onetwo")]
    [InlineData(LyricsFormat.LyricifyLines, Lines, "firstline")]
    [InlineData(LyricsFormat.LyricifySyllable, Syllable, "onetwo")]
    public void Parses_helper_formats_into_timed_lines(LyricsFormat format, string content, string firstText)
    {
        var parsed = _parser.Parse(content, format);
        Assert.False(parsed.IsEmpty);
        Assert.True(parsed.IsSynced);
        Assert.Equal(firstText, parsed.Lines[0].Text.Replace(" ", ""));
        Assert.Equal(0, parsed.Lines[0].StartMs);
    }

    [Fact]
    public void Word_synced_formats_expose_syllables()
    {
        Assert.True(_parser.Parse(Qrc, LyricsFormat.Qrc).HasWordSync);
        Assert.True(_parser.Parse(Krc, LyricsFormat.Krc).HasWordSync);
        Assert.True(_parser.Parse(Yrc, LyricsFormat.Yrc).HasWordSync);
        Assert.True(_parser.Parse(Syllable, LyricsFormat.LyricifySyllable).HasWordSync);
        Assert.False(_parser.Parse(Lines, LyricsFormat.LyricifyLines).HasWordSync);
    }

    [Fact]
    public void Detect_keeps_ttml_and_lrc()
    {
        Assert.Equal(LyricsFormat.Ttml, _parser.Detect("<tt xmlns=\"http://www.w3.org/ns/ttml\"><body/></tt>"));
        Assert.Equal(LyricsFormat.Lrc, _parser.Detect("[00:01.00]hello"));
        Assert.Equal(LyricsFormat.Plain, _parser.Detect("just words"));
    }

    [Fact]
    public void Still_parses_lrc_and_plain()
    {
        var lrc = _parser.Parse("[00:01.00]hello\n[00:02.00]there", LyricsFormat.Lrc);
        Assert.Equal(2, lrc.Lines.Count);
        Assert.Equal("hello", _parser.Parse("hello\nthere", LyricsFormat.Plain).Lines[0].Text);
    }

    [Fact]
    public void Qrc_translation_file_is_merged_onto_english_lines()
    {
        var parsed = _parser.Parse(Qrc, LyricsFormat.Qrc, "[0,1500]一(0,1500)\n");
        Assert.Equal("一", parsed.Lines[0].Translation);
    }

    [Fact]
    public void Qrc_does_not_merge_translation_onto_chinese_lines()
    {
        var parsed = _parser.Parse("[0,1500]你好示例(0,1500)\n", LyricsFormat.Qrc, "[0,1500]hello sample\n");
        Assert.True(string.IsNullOrEmpty(parsed.Lines[0].Translation));
    }

    [Fact]
    public void Garbage_returns_empty_instead_of_throwing()
    {
        Assert.True(_parser.Parse("not a lyric file", LyricsFormat.Qrc).IsEmpty);
        Assert.True(_parser.Parse("", LyricsFormat.Krc).IsEmpty);
    }

    [Fact]
    public void Qrc_container_that_holds_lrc_still_parses()
    {
        var parsed = _parser.Parse("[00:01.00]hello sample\n[00:04.00]next sample\n", LyricsFormat.Qrc);
        Assert.False(parsed.IsEmpty);
        Assert.Equal("hello sample", parsed.Lines[0].Text);
        Assert.Equal("next sample", parsed.Lines[1].Text);
    }

    [Fact]
    public void Local_folder_matches_qrc_by_file_name()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lyric-clone-qrc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "虚构乐队 - 示例曲目.qrc"), Qrc);
            var found = new LocalFileProvider().Find(dir, "示例曲目", "虚构乐队");
            Assert.NotNull(found);
            Assert.Equal(LyricsFormat.Qrc, found.Format);
            Assert.Contains("one", found.Content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
