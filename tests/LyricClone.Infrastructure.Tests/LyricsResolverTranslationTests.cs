using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;
using LyricClone.Infrastructure.Lyrics;

namespace LyricClone.Infrastructure.Tests;

public class LyricsResolverTranslationTests
{
    private static Track Track() =>
        new("id", "Sample Song", "Sample Band", "Album", 200_000, "k", null, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Attaches_chinese_lyrics_as_translation_of_an_english_result()
    {
        var en = new LyricsCandidate(LyricsSources.Lrclib, "1", "Sample Song", "Sample Band", "Album", 200_000, LyricsFormat.Lrc, "[00:01.00]hello sample", null, false, false);
        var zh = new LyricsCandidate(LyricsSources.Lrclib, "2", "Sample Song", "Sample Band", "Album", 201_000, LyricsFormat.Lrc, "[00:01.00]你好示例", null, false, false);

        var merged = LyricsResolver.AttachTranslation(en, [en, zh], Track());

        Assert.Equal("[00:01.00]你好示例", merged.Translation);
    }

    [Fact]
    public void Does_not_attach_english_as_translation_of_a_chinese_result()
    {
        var zh = new LyricsCandidate(LyricsSources.Lrclib, "1", "示例曲目", "虚构乐队", "专辑", 200_000, LyricsFormat.Lrc, "[00:01.00]你好示例", null, false, false);
        var en = new LyricsCandidate(LyricsSources.Lrclib, "2", "示例曲目", "虚构乐队", "专辑", 201_000, LyricsFormat.Lrc, "[00:01.00]hello sample", null, false, false);

        var merged = LyricsResolver.AttachTranslation(zh, [zh, en], new Track("id", "示例曲目", "虚构乐队", "专辑", 200_000, "k", null, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

        Assert.True(string.IsNullOrEmpty(merged.Translation));
    }
}
