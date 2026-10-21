using LyricClone.Core.Lyrics;

namespace LyricClone.Core.Tests;

public class LyricPresenterTests
{
    [Fact]
    public void Romaji_maps_hiragana() =>
        Assert.Equal("konnichiha", LyricPresenter.ToRomaji("こんにちは"));

    [Fact]
    public void Romaji_skips_text_without_kana() =>
        Assert.Equal("", LyricPresenter.ToRomaji("你好"));

    [Fact]
    public void Explicit_filter_stars_out_tokens()
    {
        Assert.Equal("**** this", LyricPresenter.MaskExplicit("fuck this"));
        Assert.Equal("hello", LyricPresenter.MaskExplicit("hello"));
    }

    [Fact]
    public void Split_breaks_long_lines_at_punctuation()
    {
        var parts = LyricPresenter.Split("这是一句需要拆开的特别长的中文歌词，后面还有另外半句也足够长。");
        Assert.True(parts.Count >= 2);
        Assert.All(parts, p => Assert.False(string.IsNullOrWhiteSpace(p)));
    }

    [Fact]
    public void Split_breaks_cjk_verse_phrases_on_spaces()
    {
        var parts = LyricPresenter.Split(" 甲句 乙句示例 丙句示例句");
        Assert.Equal(["甲句", "乙句示例", "丙句示例句"], parts);
    }

    [Fact]
    public void Split_does_not_break_english_on_every_space()
    {
        var parts = LyricPresenter.Split("a short sample line");
        Assert.Single(parts);
    }

    [Fact]
    public void Apply_split_keeps_each_cjk_phrase_on_screen_in_turn()
    {
        var src = new ParsedLyrics(
            [new LyricLine(3_000, 9_000, "甲句 乙句示例 丙句示例句", [])],
            isSynced: true);

        var shown = LyricPresenter.Apply(src, simplified: false, RomajiDisplay.Off, splitLong: true, showExplicit: true, showTranslation: false);

        Assert.Equal(["甲句", "乙句示例", "丙句示例句"], shown.Lines.Select(l => l.Text).ToArray());
        Assert.True(shown.Lines[0].StartMs < shown.Lines[1].StartMs);
        Assert.True(shown.Lines[1].StartMs < shown.Lines[2].StartMs);
        Assert.Equal(9_000, shown.Lines[2].EndMs);
        Assert.Equal(0, shown.FindLineIndex(3_200));
        Assert.Equal(1, shown.FindLineIndex(shown.Lines[1].StartMs + 20));
        Assert.Equal(2, shown.FindLineIndex(shown.Lines[2].StartMs + 20));
    }

    [Fact]
    public void Apply_can_hide_translation_and_mask_explicit()
    {
        var src = new ParsedLyrics(
            [new LyricLine(0, 1000, "fuck", [], "翻譯")],
            isSynced: true);

        var shown = LyricPresenter.Apply(src, simplified: true, RomajiDisplay.Off, splitLong: false, showExplicit: false, showTranslation: true);
        Assert.Equal("****", shown.Lines[0].Text);
        Assert.Equal("翻译", shown.Lines[0].Translation);

        var hidden = LyricPresenter.Apply(src, simplified: false, RomajiDisplay.Off, splitLong: false, showExplicit: true, showTranslation: false);
        Assert.True(string.IsNullOrEmpty(hidden.Lines[0].Translation));
    }
}
