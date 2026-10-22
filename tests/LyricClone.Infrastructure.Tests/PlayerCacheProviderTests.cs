using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;
using LyricClone.Infrastructure.Lyrics;

namespace LyricClone.Infrastructure.Tests;

public sealed class PlayerCacheProviderTests
{
    [Fact]
    public void Parses_qq_cache_file_name()
    {
        Assert.True(PlayerCacheProvider.TryParseQqName("虚构乐队 - 示例曲目 - 200 - 测试专辑_qm.qrc", out var info));
        Assert.Equal("虚构乐队", info.Artist);
        Assert.Equal("示例曲目", info.Title);
        Assert.Equal("测试专辑", info.Album);
        Assert.Equal(200_000, info.DurationMs);
        Assert.Equal(PlayerCacheProvider.CacheKind.Main, info.Kind);
    }

    [Fact]
    public void Underscore_in_qq_artist_is_a_separator()
    {
        Assert.True(PlayerCacheProvider.TryParseQqName("甲_乙 - 示例曲目 - 180 - 专辑_qmts.qrc", out var info));
        Assert.Equal("甲/乙", info.Artist);
        Assert.Equal(PlayerCacheProvider.CacheKind.Translation, info.Kind);
    }

    [Fact]
    public void Qq_qrc_file_that_holds_lrc_is_detected_as_lrc()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lyric-clone-qq-lrc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "虚构乐队 - 示例曲目 - 200 - 测试专辑_qm.qrc"), "[00:01.00]hello sample\n[00:04.00]next sample\n");
            var found = new PlayerCacheProvider(_ => [dir]).Find(LyricsSources.QqCache, Track("示例曲目", "虚构乐队", 200_000));
            Assert.NotNull(found);
            Assert.Equal(LyricsFormat.Lrc, found.Format);
            Assert.Contains("hello sample", found.Content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Finds_qq_qrc_by_title_artist_and_duration()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lyric-clone-qq-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "虚构乐队 - 示例曲目 - 200 - 测试专辑_qm.qrc"), HelperLyricsParserTests.Qrc);
            var provider = new PlayerCacheProvider(_ => [dir]);
            var found = provider.Find(LyricsSources.QqCache, Track("示例曲目", "虚构乐队", 200_000));

            Assert.NotNull(found);
            Assert.Equal(LyricsSources.QqCache, found.Source);
            Assert.Equal(LyricsFormat.Qrc, found.Format);
            Assert.Contains("one", found.Content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Attaches_qmts_only_when_the_main_lyric_is_english()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lyric-clone-qqts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Nobody Real - Sample Song - 180 - Album_qm.qrc"), "[00:01.00]hello sample\n[00:04.00]next sample\n");
            File.WriteAllText(Path.Combine(dir, "Nobody Real - Sample Song - 180 - Album_qmts.qrc"), "[00:01.00]你好示例\n[00:04.00]下一句示例\n");
            var en = new PlayerCacheProvider(_ => [dir]).Find(LyricsSources.QqCache, Track("Sample Song", "Nobody Real", 180_000));
            Assert.Equal("[00:01.00]你好示例\n[00:04.00]下一句示例\n", en?.Translation);

            File.WriteAllText(Path.Combine(dir, "虚构乐队 - 示例曲目 - 200 - 专辑_qm.qrc"), "[00:01.00]你好示例\n");
            File.WriteAllText(Path.Combine(dir, "虚构乐队 - 示例曲目 - 200 - 专辑_qmts.qrc"), "[00:01.00]hello sample\n");
            var zh = new PlayerCacheProvider(_ => [dir]).Find(LyricsSources.QqCache, Track("示例曲目", "虚构乐队", 200_000));
            Assert.True(string.IsNullOrEmpty(zh?.Translation));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Extracts_qrc_from_xml_wrapper()
    {
        var xml = """<?xml version="1.0"?><Qrc LyricContent="[0,100]one(0,100)" />""";
        Assert.Equal("[0,100]one(0,100)", PlayerCacheProvider.ExtractLyricContent(xml));
        Assert.Equal("[00:01.00]hello", PlayerCacheProvider.ExtractLyricContent("[00:01.00]hello"));
    }

    [Fact]
    public void Extracts_qrc_when_the_xml_attribute_has_newlines_and_raw_lt()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <QrcInfos>
            <QrcHeadInfo SaveTime="1" Version="100"/>
            <LyricInfo LyricCount="1">
            <Lyric_1 LyricType="1" LyricContent="[ti:sample]
            [0,100]one<(0,100)
            "/>
            </LyricInfo>
            </QrcInfos>
            """;
        var inner = PlayerCacheProvider.ExtractLyricContent(xml);
        Assert.Contains("[0,100]one<(0,100)", inner);
        Assert.DoesNotContain("QrcInfos", inner);
        Assert.DoesNotContain("LyricContent", inner);
    }

    [Fact]
    public void Leaves_plain_lyric_bytes_unchanged()
    {
        var plain = "[00:01.00]hello sample"u8.ToArray();
        Assert.Equal(plain, PlayerCacheProvider.UnwrapLocalQmc(plain));
    }

    [Fact]
    public void Finds_qq_file_by_unique_title_when_the_artist_differs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lyric-clone-qq-title-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "别人 - 示例曲目 - 200 - 专辑_qm.qrc"), HelperLyricsParserTests.Qrc);
            var found = new PlayerCacheProvider(_ => [dir]).Find(LyricsSources.QqCache, Track("示例曲目", "虚构乐队", 200_000));
            Assert.NotNull(found);
            Assert.Contains("one", found.Content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Finds_netease_lrc_by_artist_dash_title()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lyric-clone-netease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "虚构乐队 - 示例曲目.lrc"), "[00:01.00]hello sample\n[00:04.00]next sample\n");
            var found = new PlayerCacheProvider(_ => [dir]).Find(LyricsSources.NeteaseCache, Track("示例曲目", "虚构乐队", 200_000));
            Assert.NotNull(found);
            Assert.Equal(LyricsSources.NeteaseCache, found.Source);
            Assert.Equal(LyricsFormat.Lrc, found.Format);
            Assert.Contains("hello sample", found.Content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Finds_kugou_krc_by_artist_dash_title()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lyric-clone-kugou-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "虚构乐队 - 示例曲目.krc"), HelperLyricsParserTests.Krc);
            var found = new PlayerCacheProvider(_ => [dir]).Find(LyricsSources.KugouCache, Track("示例曲目", "虚构乐队", 200_000));
            Assert.NotNull(found);
            Assert.Equal(LyricsSources.KugouCache, found.Source);
            Assert.Equal(LyricsFormat.Krc, found.Format);
            Assert.Contains("one", found.Content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Extract_does_not_resolve_external_xml_entities()
    {
        var canary = Path.Combine(Path.GetTempPath(), "lyric-clone-xxe-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(canary, "CANARY_XXE_SHOULD_NOT_APPEAR");
        try
        {
            var href = new Uri(canary).AbsoluteUri;
            var xml = $"""
                <?xml version="1.0"?>
                <!DOCTYPE foo [<!ENTITY xxe SYSTEM "{href}">]>
                <Qrc LyricContent="[0,100]one(0,100)&xxe;" />
                """;
            var inner = PlayerCacheProvider.ExtractLyricContent(xml);
            Assert.DoesNotContain("CANARY_XXE_SHOULD_NOT_APPEAR", inner);
            Assert.Contains("one", inner, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(canary);
        }
    }

    [Fact]
    public void WithKnownDefaults_inserts_player_caches_ahead_of_older_orders()
    {
        var merged = LyricsSources.WithKnownDefaults([LyricsSources.LocalFile, LyricsSources.Lrclib]);
        Assert.Equal(
            [LyricsSources.QqCache, LyricsSources.NeteaseCache, LyricsSources.KugouCache, LyricsSources.LocalFile, LyricsSources.Embedded, LyricsSources.Lrclib],
            merged);
    }

    private static Track Track(string title, string artist, long duration) =>
        new("id", title, artist, "", duration, "k", null, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
}
