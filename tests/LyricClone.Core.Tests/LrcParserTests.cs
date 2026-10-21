using LyricClone.Core.Lyrics;

namespace LyricClone.Core.Tests;

// Fixtures are made-up sample text, not real song lyrics.
public class LrcParserTests
{
    [Fact]
    public void Parses_line_tags_and_sorts_by_time()
    {
        var lrc = """
            [ti:Sample]
            [00:10.50]second line
            [00:05.00]first line
            [01:00.123]third line
            """;

        var result = LrcParser.Parse(lrc);

        Assert.True(result.IsSynced);
        Assert.Equal(3, result.Lines.Count);
        Assert.Equal(("first line", 5000L, 10500L), (result.Lines[0].Text, result.Lines[0].StartMs, result.Lines[0].EndMs));
        Assert.Equal(10500, result.Lines[1].StartMs);
        Assert.Equal(60123, result.Lines[2].StartMs);
        Assert.Equal(65123, result.Lines[2].EndMs);
    }

    [Fact]
    public void Repeated_tags_on_one_line_create_one_line_each()
    {
        var result = LrcParser.Parse("[00:01.00][00:20.00]chorus sample");

        Assert.Equal(2, result.Lines.Count);
        Assert.All(result.Lines, l => Assert.Equal("chorus sample", l.Text));
        Assert.Equal(20000, result.Lines[1].StartMs);
    }

    [Fact]
    public void Offset_tag_moves_lines_earlier_when_positive()
    {
        var result = LrcParser.Parse("[offset:+500]\n[00:02.00]sample");

        Assert.Equal(1500, result.Lines[0].StartMs);
    }

    [Fact]
    public void Second_line_with_same_time_is_translation()
    {
        var lrc = """
            [00:03.00]hello sample
            [00:03.00]你好示例
            [00:06.00]next sample
            """;

        var result = LrcParser.Parse(lrc);

        Assert.Equal(2, result.Lines.Count);
        Assert.Equal("你好示例", result.Lines[0].Translation);
        Assert.Null(result.Lines[1].Translation);
        Assert.True(result.HasTranslation);
    }

    [Fact]
    public void Nearby_english_and_chinese_lines_are_translation()
    {
        var result = LrcParser.Parse("""
            [00:03.00]hello sample
            [00:03.20]你好示例
            [00:06.00]next sample
            """);

        Assert.Equal(2, result.Lines.Count);
        Assert.Equal("hello sample", result.Lines[0].Text);
        Assert.Equal("你好示例", result.Lines[0].Translation);
    }

    [Fact]
    public void Chinese_then_english_same_tag_is_not_a_translation()
    {
        var result = LrcParser.Parse("""
            [00:03.00]你好示例
            [00:03.00]hello sample
            [00:06.00]下一句示例
            """);

        Assert.Equal(3, result.Lines.Count);
        Assert.Equal("你好示例", result.Lines[0].Text);
        Assert.True(string.IsNullOrEmpty(result.Lines[0].Translation));
        Assert.Equal("hello sample", result.Lines[1].Text);
    }

    [Fact]
    public void Separate_translation_is_ignored_when_the_main_line_is_chinese()
    {
        var result = LrcParser.Parse("[00:03.00]你好示例\n[00:06.00]下一句示例", "[00:03.00]hello sample\n[00:06.00]next sample");

        Assert.True(string.IsNullOrEmpty(result.Lines[0].Translation));
        Assert.True(string.IsNullOrEmpty(result.Lines[1].Translation));
    }

    [Fact]
    public void Slash_separates_english_and_chinese_on_one_line()
    {
        var result = LrcParser.Parse("[00:03.00]hello sample / 你好示例");

        Assert.Equal("hello sample", result.Lines[0].Text);
        Assert.Equal("你好示例", result.Lines[0].Translation);
    }

    [Fact]
    public void Three_chinese_lines_with_the_same_tag_stay_on_screen_in_turn()
    {
        var lrc = """
            [00:03.00]第一句示例
            [00:03.00]第二句示例
            [00:03.00]第三句示例
            [00:09.00]下一段示例
            """;

        var result = LrcParser.Parse(lrc);

        Assert.Equal(4, result.Lines.Count);
        Assert.Equal("第一句示例", result.Lines[0].Text);
        Assert.Equal("第二句示例", result.Lines[1].Text);
        Assert.Equal("第三句示例", result.Lines[2].Text);
        Assert.True(result.Lines[0].StartMs < result.Lines[1].StartMs);
        Assert.True(result.Lines[1].StartMs < result.Lines[2].StartMs);
        Assert.Equal(0, result.FindLineIndex(3_200));
        Assert.Equal(1, result.FindLineIndex(5_200));
        Assert.Equal(2, result.FindLineIndex(7_200));
        Assert.Equal(3, result.FindLineIndex(9_000));
    }

    [Fact]
    public void Separate_translation_is_merged_by_time()
    {
        var result = LrcParser.Parse("[00:03.00]one\n[00:06.00]two", "[00:03.02]一\n[00:06.00]二");

        Assert.Equal("一", result.Lines[0].Translation);
        Assert.Equal("二", result.Lines[1].Translation);
    }

    [Fact]
    public void Enhanced_word_tags_produce_syllables()
    {
        var result = LrcParser.Parse("[00:01.00]<00:01.00>this <00:01.40>is <00:01.60>made <00:02.20>up<00:03.00>\n[00:04.00]end");

        var line = result.Lines[0];
        Assert.Equal("this is made up", line.Text);
        Assert.Equal(4, line.Syllables.Count);
        Assert.Equal(new Syllable(1000, 400, "this "), line.Syllables[0]);
        Assert.Equal(new Syllable(2200, 800, "up"), line.Syllables[3]);
        Assert.Equal(3000, line.EndMs);
        Assert.True(result.HasWordSync);
    }

    [Fact]
    public void Two_and_three_digit_fractions_and_colon_fraction()
    {
        var result = LrcParser.Parse("[00:01.5]a\n[00:02.25]b\n[00:03:40]c");

        Assert.Equal([1500L, 2250L, 3400L], result.Lines.Select(l => l.StartMs));
    }

    [Fact]
    public void Empty_or_untimed_text_is_empty()
    {
        Assert.True(LrcParser.Parse("").IsEmpty);
        Assert.True(LrcParser.Parse("just words\nno tags").IsEmpty);
        Assert.False(LrcParser.LooksLikeLrc("just words"));
        Assert.True(LrcParser.LooksLikeLrc("[ar:x]\n[00:01.00]a"));
    }

    [Fact]
    public void FindLineIndex_uses_start_times()
    {
        var result = LrcParser.Parse("[00:01.00]a\n[00:02.00]b\n[00:03.00]c");

        Assert.Equal(-1, result.FindLineIndex(500));
        Assert.Equal(0, result.FindLineIndex(1000));
        Assert.Equal(1, result.FindLineIndex(2999));
        Assert.Equal(2, result.FindLineIndex(99999));
    }

    [Fact]
    public void FindLineIndex_paces_unsynced_lines_across_duration()
    {
        var unsynced = LyricsParser.ParsePlain("one\n\ntwo\nthree");

        Assert.Equal(3, unsynced.Lines.Count);
        Assert.Equal(0, unsynced.FindLineIndex(0, 90_000));
        Assert.Equal(1, unsynced.FindLineIndex(40_000, 90_000));
        Assert.Equal(2, unsynced.FindLineIndex(80_000, 90_000));
        Assert.Equal(0, unsynced.FindLineIndex(1_000, durationMs: null));
    }

    [Fact]
    public void WithOffset_shifts_lines_and_syllables_later()
    {
        var result = LrcParser.Parse("[00:01.00]<00:01.00>a <00:01.50>b").WithOffset(250);

        Assert.Equal(1250, result.Lines[0].StartMs);
        Assert.Equal(1750, result.Lines[0].Syllables[1].StartMs);
    }
}
