using LyricClone.Core.Library;
using LyricClone.Infrastructure.Lrclib;
using LyricClone.Infrastructure.Lyrics;

namespace LyricClone.Flows.Tests;

/// <summary>F01 (see lyrics for whatever is playing) and F07 (per-app tuning) through the real NowPlayingService.</summary>
public sealed class PlaybackFlowTests
{
    [Fact]
    public void F01_H1_playing_track_resolves_from_lrclib_and_is_cached()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            env.Sessions.Play("demo.player", "示例曲目", "虚构乐队");
            await Sta.Until(() => np.Resolved.State == ResolveState.Found, "lyrics found");

            Assert.Equal(3, np.Lyrics.Lines.Count);
            Assert.Equal("示例 A 的第一行", np.Lyrics.Lines[0].Text);
            Assert.Equal("示例曲目 — 虚构乐队", np.StatusText);

            // Same track again (e.g. after pause / resume): no new network call, served from SQLite.
            var callsBefore = env.Lrclib.Calls.Count;
            env.Sessions.Stop();
            await Sta.Until(() => np.Resolved.State == ResolveState.NoTrack, "cleared");
            env.Sessions.Play("demo.player", "示例曲目", "虚构乐队");
            await Sta.Until(() => np.Resolved.State == ResolveState.Found, "found again");
            Assert.Equal(callsBefore, env.Lrclib.Calls.Count);
        });
    }

    [Fact]
    public void F01_E1_no_lyrics_found_shows_not_found_and_remembers_the_miss()
    {
        using var env = new FlowEnv();
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            env.Sessions.Play("demo.player", "没有歌词的曲目", "测试艺人");
            await Sta.Until(() => !np.IsResolving && np.Resolved.State == ResolveState.NotFound, "not found");

            var calls = env.Lrclib.Calls.Count;
            env.Sessions.Stop();
            env.Sessions.Play("demo.player", "没有歌词的曲目", "测试艺人");
            await Sta.Until(() => !np.IsResolving && np.Resolved.State == ResolveState.NotFound, "not found again");
            Assert.Equal(calls, env.Lrclib.Calls.Count);
        });
    }

    [Fact]
    public void F01_E2_network_error_is_reported_not_thrown()
    {
        using var env = new FlowEnv();
        env.Lrclib.FailWith = new LrclibException("Service Unavailable", System.Net.HttpStatusCode.ServiceUnavailable);
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            env.Sessions.Play("demo.player", "示例曲目", "虚构乐队");
            await Sta.Until(() => !np.IsResolving && np.Resolved.State == ResolveState.Error, "error state");
            Assert.NotNull(np.Resolved.Error);
        });
    }

    [Fact]
    public void F01_E3_instrumental_record_is_shown_as_instrumental()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(7, "示例纯音乐", "虚构乐队", "", instrumental: true);
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            env.Sessions.Play("demo.player", "示例纯音乐", "虚构乐队");
            await Sta.Until(() => np.Resolved.State == ResolveState.Instrumental, "instrumental");
            Assert.Equal("纯音乐", np.StatusText);
        });
    }

    [Fact]
    public void F01_E4_emoji_and_accents_in_metadata_still_match()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(8, "Café Ünïcode 🌙", "Ñame Fake", Samples.LrcB);
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            env.Sessions.Play("demo.player", "Café Ünïcode 🌙", "Ñame Fake");
            await Sta.Until(() => np.Resolved.State == ResolveState.Found, "found");
        });
    }

    [Fact]
    public void F01_E5_track_change_mid_resolve_shows_the_new_track()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "第一首", "虚构乐队", Samples.LrcA);
        env.Lrclib.Add(2, "第二首", "虚构乐队", Samples.LrcB);
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            env.Sessions.Play("demo.player", "第一首", "虚构乐队");
            env.Sessions.Play("demo.player", "第二首", "虚构乐队");
            await Sta.Until(() => np.Resolved.State == ResolveState.Found && np.Resolved.Track?.Title == "第二首", "second track");
            await Task.Delay(300);
            Assert.Equal("第二首", np.Resolved.Track?.Title);
        });
    }

    [Fact]
    public void F07_H1_per_app_source_order_prefers_local_folder()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        var folder = env.LocalFolder(("虚构乐队 - 示例曲目.lrc", Samples.LrcMine));
        Sta.Run(async () =>
        {
            await env.Settings.UpdateAsync(s =>
            {
                s.LocalLyricsFolder = folder;
                s.SourceOrder = [LyricsSources.Lrclib, LyricsSources.LocalFile];
                s.ProfileFor("local.first").SourceOrder = [LyricsSources.LocalFile, LyricsSources.Lrclib];
            });
            var np = await env.StartAsync();

            env.Sessions.Play("online.first", "示例曲目", "虚构乐队");
            await Sta.Until(() => np.Resolved.State == ResolveState.Found, "online app found");
            Assert.Equal(LyricsSources.Lrclib, np.Resolved.Version?.Source);

            env.Sessions.Play("local.first", "示例曲目", "虚构乐队");
            await Sta.Until(() => np.Resolved.Version?.Source == LyricsSources.LocalFile, "local app uses local file");
            Assert.Equal("我自己写的 第一行", np.Lyrics.Lines[0].Text);
        });
    }

    [Fact]
    public void F07_H2_session_delay_and_device_delay_shift_the_clock()
    {
        using var env = new FlowEnv();
        Sta.Run(async () =>
        {
            await env.Settings.UpdateAsync(s =>
            {
                s.DeviceDelayMs = 100;
                s.ProfileFor("slow.player").SessionDelayMs = 250;
            });
            var np = await env.StartAsync();
            env.Sessions.Play("slow.player", "示例曲目", "虚构乐队", positionMs: 10_000);
            await Sta.Until(() => np.Snapshot is not null, "snapshot");
            Assert.Equal(100, np.Clock.DeviceDelayMs);
            Assert.Equal(250, np.Clock.SessionDelayMs);

            await env.Settings.UpdateAsync(s => s.ProfileFor("slow.player").SessionDelayMs = 0);
            await Sta.Until(() => np.Clock.SessionDelayMs == 0, "delay updated live");
        });
    }

    [Fact]
    public void F07_E1_browsers_are_ignored_by_default_and_disabled_apps_are_skipped()
    {
        using var env = new FlowEnv();
        Sta.Run(async () =>
        {
            await env.Settings.UpdateAsync(s => s.ProfileFor("muted.player").Enabled = false);
            var np = await env.StartAsync();

            env.Sessions.Play("MSEdge", "网页视频", "某网站");
            await Task.Delay(100);
            Assert.Null(np.Snapshot);

            env.Sessions.Play("Muted.Player", "示例曲目", "虚构乐队");
            await Task.Delay(100);
            Assert.Null(np.Snapshot);

            await env.Settings.UpdateAsync(s => s.IncludeBrowserSessions = true);
            env.Sessions.Play("MSEdge", "网页视频", "某网站");
            await Sta.Until(() => np.Snapshot is not null, "browser allowed after opt-in");
        });
    }

    [Fact]
    public void F01_live_suffix_still_matches_the_studio_record()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            env.Sessions.Play("QQMusic.exe", "示例曲目 (Live)", "虚构乐队/另一人", album: "某演唱会");
            await Sta.Until(() => np.Resolved.State == ResolveState.Found, "live tag still found");
            Assert.Equal("示例 A 的第一行", np.Lyrics.Lines[0].Text);
        });
    }

    [Fact]
    public void F01_exact_lookup_error_falls_back_to_search()
    {
        using var env = new FlowEnv();
        env.Lrclib.FailGetWith = new LrclibException("timeout");
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            env.Sessions.Play("QQMusic.exe", "示例曲目", "虚构乐队");
            await Sta.Until(() => np.Resolved.State == ResolveState.Found, "search after get failed");
        });
    }
}
