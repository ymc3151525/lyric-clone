using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;

namespace LyricClone.Core.Tests;

public class TrackNormalizerTests
{
    [Theory]
    [InlineData("Song (feat. Someone)", "Song")]
    [InlineData("Song feat. Someone", "Song")]
    [InlineData("Song - 2011 Remaster", "Song")]
    [InlineData("Song (Remastered 2009)", "Song")]
    [InlineData("Song.flac", "Song")]
    [InlineData("歌名（feat. 某人）", "歌名")]
    [InlineData("晴天 - QQ音乐", "晴天")]
    public void CleanTitle_removes_credits_and_tags(string input, string expected) =>
        Assert.Equal(expected, TrackNormalizer.CleanTitle(input));

    [Theory]
    [InlineData("梦田 (Live)", "梦田")]
    [InlineData("晴天（臻品音质2.0）", "晴天")]
    [InlineData("笑红尘 (Single Version)", "笑红尘")]
    [InlineData("示例曲（单曲版）", "示例曲")]
    [InlineData("Empty", "Empty")]
    public void SearchTitle_strips_live_and_quality_tags(string input, string expected) =>
        Assert.Equal(expected, TrackNormalizer.SearchTitle(input));

    [Theory]
    [InlineData("A / B", "A")]
    [InlineData("A、B", "A")]
    [InlineData("A feat. B", "A")]
    [InlineData("A x B", "A")]
    public void PrimaryArtist_takes_first_credit(string input, string expected) =>
        Assert.Equal(expected, TrackNormalizer.PrimaryArtist(input));

    [Fact]
    public void SplitCombined_handles_artist_dash_title_in_title_field()
    {
        Assert.Equal(("Title", "Artist"), TrackNormalizer.SplitCombined("Artist - Title.mp3", ""));
        Assert.Equal(("A - B", "Real"), TrackNormalizer.SplitCombined("A - B", "Real"));
        Assert.Equal(("晴天", "周杰伦"), TrackNormalizer.SplitCombined("周杰伦 - 晴天", "周杰伦"));
    }

    [Fact]
    public void SameSong_ignores_live_tags()
    {
        Assert.True(TrackNormalizer.SameSong("梦田 (Live)", "齐豫/潘越云", "梦田", "齐豫"));
        Assert.True(TrackNormalizer.SameSong("笑红尘 (Single Version)", "陈淑桦", "笑红尘", "陈淑桦"));
        Assert.False(TrackNormalizer.SameSong("梦田", "齐豫", "你的眼神", "蔡琴"));
    }

    [Fact]
    public void MatchKey_is_stable_across_case_width_and_punctuation()
    {
        Assert.Equal(
            TrackNormalizer.MatchKey("Ｈｅｌｌｏ, World!", "Some Band", "Album"),
            TrackNormalizer.MatchKey("hello world", "SOME BAND feat. X", "album"));
    }

    [Fact]
    public void Score_prefers_matching_duration()
    {
        double close = TrackNormalizer.Score("Song", "Band", 200_000, "Song", "Band", 201_000);
        double far = TrackNormalizer.Score("Song", "Band", 200_000, "Song", "Band", 260_000);

        Assert.True(close > far);
        Assert.True(close > 0.95);
    }
}

public class VersionSelectorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static LyricsVersion V(string id, string source, VersionStatus status = VersionStatus.Cached, VersionKind? kind = null, bool word = false, int age = 0) =>
        new(id, "t", kind ?? (LyricsSources.IsOnline(source) ? VersionKind.Online : VersionKind.Local), source, LyricsSources.IsOnline(source) ? "1" : null,
            id, LyricsFormat.Lrc, "[00:01.00]x", null, true, word, false, false, false, status, 0, T0, T0, T0.AddMinutes(age));

    private static readonly string[] Order = [LyricsSources.LocalFile, LyricsSources.Lrclib];

    [Fact]
    public void Pinned_wins()
    {
        var sel = VersionSelector.Select([V("a", LyricsSources.Lrclib, VersionStatus.Confirmed), V("b", LyricsSources.User, VersionStatus.Pinned)], Order);

        Assert.Equal(("b", SelectionReason.Pinned), (sel.Version!.Id, sel.Reason));
    }

    [Fact]
    public void Confirmed_beats_source_order()
    {
        var sel = VersionSelector.Select([V("local", LyricsSources.LocalFile), V("online", LyricsSources.Lrclib, VersionStatus.Confirmed)], Order);

        Assert.Equal(("online", SelectionReason.Confirmed), (sel.Version!.Id, sel.Reason));
    }

    [Fact]
    public void PreferQuality_picks_word_sync_over_source_order()
    {
        var local = V("local", LyricsSources.LocalFile);
        var online = V("online", LyricsSources.Lrclib, word: true);

        Assert.Equal("local", VersionSelector.Select([local, online], Order).Version!.Id);
        Assert.Equal("online", VersionSelector.Select([local, online], Order, VersionPickMode.PreferQuality).Version!.Id);
    }

    [Fact]
    public void Otherwise_source_order()
    {
        var sel = VersionSelector.Select([V("online", LyricsSources.Lrclib), V("local", LyricsSources.LocalFile)], Order);

        Assert.Equal(("local", SelectionReason.SourceOrder), (sel.Version!.Id, sel.Reason));
    }

    [Fact]
    public void User_versions_need_pinning()
    {
        var sel = VersionSelector.Select([V("mine", LyricsSources.User)], [LyricsSources.User, LyricsSources.Lrclib]);

        Assert.Null(sel.Version);
    }

    [Fact]
    public void SourcesToFetch_skips_saved_and_recent_misses()
    {
        var fetch = VersionSelector.SourcesToFetch([V("a", LyricsSources.LocalFile)], [LyricsSources.LocalFile, LyricsSources.Embedded, LyricsSources.Lrclib], [LyricsSources.Embedded]);

        Assert.Equal([LyricsSources.Lrclib], fetch);
    }

    [Fact]
    public void OfflineSourcesAhead_lists_unsaved_local_sources_ranked_above_a_cached_online_pick()
    {
        var versions = new[] { V("a", LyricsSources.Lrclib) };
        var order = new[] { LyricsSources.LocalFile, LyricsSources.Embedded, LyricsSources.Lrclib };

        var ahead = VersionSelector.OfflineSourcesAhead(versions, order, VersionSelector.Select(versions, order));

        Assert.Equal([LyricsSources.LocalFile, LyricsSources.Embedded], ahead);
    }

    [Fact]
    public void OfflineSourcesAhead_is_empty_for_pinned_or_confirmed_picks_and_when_online_ranks_first()
    {
        var order = new[] { LyricsSources.LocalFile, LyricsSources.Lrclib };
        var confirmed = new[] { V("a", LyricsSources.Lrclib, VersionStatus.Confirmed) };
        var pinned = new[] { V("a", LyricsSources.Lrclib, VersionStatus.Pinned) };
        var cached = new[] { V("a", LyricsSources.Lrclib) };
        string[] onlineFirst = [LyricsSources.Lrclib, LyricsSources.LocalFile];

        Assert.Empty(VersionSelector.OfflineSourcesAhead(confirmed, order, VersionSelector.Select(confirmed, order)));
        Assert.Empty(VersionSelector.OfflineSourcesAhead(pinned, order, VersionSelector.Select(pinned, order)));
        Assert.Empty(VersionSelector.OfflineSourcesAhead(cached, onlineFirst, VersionSelector.Select(cached, onlineFirst)));
    }

    [Fact]
    public void Stale_online_cache_is_older_than_a_week_and_not_pinned()
    {
        var stale = V("a", LyricsSources.Lrclib) with { FetchedAt = T0.AddDays(-8) };
        var fresh = V("b", LyricsSources.Lrclib) with { FetchedAt = T0.AddDays(-1) };
        var pinned = V("c", LyricsSources.Lrclib, VersionStatus.Pinned) with { FetchedAt = T0.AddDays(-30) };
        var local = V("d", LyricsSources.LocalFile) with { FetchedAt = T0.AddDays(-30) };

        Assert.True(VersionSelector.IsStaleOnlineCache(stale, T0));
        Assert.False(VersionSelector.IsStaleOnlineCache(fresh, T0));
        Assert.False(VersionSelector.IsStaleOnlineCache(pinned, T0));
        Assert.False(VersionSelector.IsStaleOnlineCache(local, T0));
    }
}
