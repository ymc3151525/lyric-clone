using LyricClone.Core.Lyrics;

namespace LyricClone.Core.Tests;

// Fixtures are made-up sample text, not real song lyrics.
public class TtmlParserTests
{
    [Fact]
    public void Parses_line_level_ttml()
    {
        const string ttml = """
            <tt xmlns="http://www.w3.org/ns/ttml"><body><div>
              <p begin="00:01.000" end="00:03.500">a sample line</p>
              <p begin="3.5s" end="6s">another sample</p>
            </div></body></tt>
            """;

        var result = TtmlParser.Parse(ttml);

        Assert.True(result.IsSynced);
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(("a sample line", 1000L, 3500L), (result.Lines[0].Text, result.Lines[0].StartMs, result.Lines[0].EndMs));
        Assert.Equal(3500, result.Lines[1].StartMs);
        Assert.False(result.HasWordSync);
    }

    [Fact]
    public void Parses_word_spans_and_keeps_spaces_on_previous_syllable()
    {
        const string ttml = """
            <tt xmlns="http://www.w3.org/ns/ttml"><body><div><p begin="1.0s" end="2.0s"><span begin="1.0s" end="1.4s">made</span> <span begin="1.4s" end="2.0s">up</span></p></div></body></tt>
            """;

        var line = TtmlParser.Parse(ttml).Lines.Single();

        Assert.Equal("made up", line.Text);
        Assert.Equal(new Syllable(1000, 400, "made "), line.Syllables[0]);
        Assert.Equal(new Syllable(1400, 600, "up"), line.Syllables[1]);
    }

    [Fact]
    public void Reads_inline_and_head_translations()
    {
        const string ttml = """
            <tt xmlns="http://www.w3.org/ns/ttml" xmlns:ttm="http://www.w3.org/ns/ttml#metadata" xmlns:itunes="http://music.apple.com/lyric-ttml-internal">
              <head><metadata><iTunesMetadata xmlns="http://music.apple.com/lyric-ttml-internal">
                <translations><translation type="replacement" xml:lang="zh"><text for="L2">第二行示例</text></translation></translations>
              </iTunesMetadata></metadata></head>
              <body><div>
                <p begin="00:00:01.000" end="00:00:02.000" itunes:key="L1">first sample<span ttm:role="x-translation">第一行示例</span></p>
                <p begin="00:00:02.000" end="00:00:03.000" itunes:key="L2">second sample</p>
              </div></body></tt>
            """;

        var result = TtmlParser.Parse(ttml);

        Assert.Equal("first sample", result.Lines[0].Text);
        Assert.Equal("第一行示例", result.Lines[0].Translation);
        Assert.Equal("第二行示例", result.Lines[1].Translation);
    }

    [Fact]
    public void Invalid_xml_returns_empty()
    {
        Assert.True(TtmlParser.Parse("<tt><p>").IsEmpty);
    }
}
