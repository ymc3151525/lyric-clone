using LyricClone.Core.Playback;

namespace LyricClone.Core.Tests;

public class PlaybackClockTests
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private PlaybackClock NewClock() => new(() => _now) { SmoothingEnabled = false };

    private MediaSnapshot Snap(long pos, PlaybackStatus status = PlaybackStatus.Playing, bool timeline = true, string title = "t", DateTimeOffset? at = null) =>
        new("app", title, "a", "al", 200_000, pos, at ?? _now, status, 1.0, timeline);

    [Fact]
    public void Extrapolates_while_playing()
    {
        var clock = NewClock();
        clock.Update(Snap(10_000));

        _now = _now.AddMilliseconds(1500);

        Assert.Equal(11_500, clock.PositionMs);
    }

    [Fact]
    public void Accounts_for_report_age()
    {
        var clock = NewClock();
        clock.Update(Snap(10_000, at: _now.AddMilliseconds(-300)));

        Assert.Equal(10_300, clock.PositionMs);
    }

    [Fact]
    public void Holds_when_paused()
    {
        var clock = NewClock();
        clock.Update(Snap(10_000, PlaybackStatus.Paused));

        _now = _now.AddSeconds(5);

        Assert.Equal(10_000, clock.PositionMs);
    }

    [Fact]
    public void Positive_delays_show_lyrics_later()
    {
        var clock = NewClock();
        clock.SessionDelayMs = 300;
        clock.DeviceDelayMs = 200;
        clock.Update(Snap(10_000));

        Assert.Equal(9_500, clock.PositionMs);
    }

    [Fact]
    public void No_timeline_starts_internal_timer_at_track_change()
    {
        var clock = NewClock();
        clock.Update(Snap(0, timeline: false, title: "one"));
        _now = _now.AddSeconds(3);
        Assert.Equal(3_000, clock.PositionMs);

        clock.Update(Snap(0, timeline: false, title: "two"));
        _now = _now.AddSeconds(1);
        Assert.Equal(1_000, clock.PositionMs);
    }

    [Fact]
    public void No_timeline_pause_and_resume_keeps_position()
    {
        var clock = NewClock();
        clock.Update(Snap(0, timeline: false));
        _now = _now.AddSeconds(2);
        clock.Update(Snap(0, PlaybackStatus.Paused, timeline: false));
        _now = _now.AddSeconds(10);
        clock.Update(Snap(0, timeline: false));
        _now = _now.AddSeconds(1);

        Assert.Equal(3_000, clock.PositionMs);
    }

    [Fact]
    public void Smoothing_ignores_small_backward_jitter_but_follows_seeks()
    {
        var clock = new PlaybackClock(() => _now) { SmoothingEnabled = true };
        clock.Update(Snap(10_000));
        _now = _now.AddSeconds(1);

        clock.Update(Snap(10_400));
        Assert.Equal(11_000, clock.PositionMs);

        clock.Update(Snap(2_000));
        Assert.Equal(2_000, clock.PositionMs);
    }

    [Fact]
    public void Smoothing_ignores_tiny_forward_noise()
    {
        var clock = new PlaybackClock(() => _now) { SmoothingEnabled = true };
        clock.Update(Snap(10_000));
        _now = _now.AddSeconds(1);

        clock.Update(Snap(11_040));
        Assert.Equal(11_000, clock.PositionMs);
    }

    [Fact]
    public void Smoothing_eases_a_medium_forward_jump()
    {
        var clock = new PlaybackClock(() => _now) { SmoothingEnabled = true };
        clock.Update(Snap(10_000));
        _now = _now.AddSeconds(1);
        clock.Update(Snap(11_500));

        Assert.InRange(clock.PositionMs, 10_900, 11_200);

        _now = _now.AddMilliseconds(PlaybackClock.EaseDurationMs);
        Assert.Equal(12_200, clock.PositionMs);
    }

    [Fact]
    public void Never_negative()
    {
        var clock = NewClock();
        clock.SessionDelayMs = 5_000;
        clock.Update(Snap(1_000));

        Assert.Equal(0, clock.PositionMs);
    }
}

public class MediaTimelineTests
{
    private static MediaSnapshot Song(string title, long? duration) =>
        new("app", title, "artist", "album", duration, 0, DateTimeOffset.UnixEpoch, PlaybackStatus.Playing, 1, true);

    [Fact]
    public void Drops_previous_song_length_that_stuck_on_the_new_track()
    {
        long? max = 233_692;
        var next = MediaTimeline.StabilizeDuration(Song("new", 233_692), Song("old", 233_692), ref max);

        Assert.Null(next.DurationMs);
        Assert.Null(max);
    }

    [Fact]
    public void Keeps_the_longest_length_when_remaining_time_shrinks()
    {
        long? max = null;
        var first = MediaTimeline.StabilizeDuration(Song("t", 252_000), previous: null, ref max);
        var later = MediaTimeline.StabilizeDuration(Song("t", 240_000), first, ref max);

        Assert.Equal(252_000, first.DurationMs);
        Assert.Equal(252_000, later.DurationMs);
    }

    [Fact]
    public void DurationArrived_when_the_real_length_arrives()
    {
        var first = Song("t", null);
        var later = Song("t", 252_000);

        Assert.True(MediaTimeline.DurationArrived(later, first));
        Assert.False(MediaTimeline.DurationArrived(Song("t", 260_000), later));
    }
}
