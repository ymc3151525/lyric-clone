namespace LyricClone.Core.Playback;

/// <summary>
/// SMTC length is noisy: the previous song's duration often sticks for a moment, and some players
/// then report remaining time that shrinks. Lyrics lookup should see a stable length for the current track.
/// </summary>
public static class MediaTimeline
{
    public const long LeftoverDurationMs = 500;

    /// <summary>
    /// Drop an unchanged leftover length on track change; afterwards keep the longest length seen for this track.
    /// </summary>
    public static MediaSnapshot StabilizeDuration(MediaSnapshot snapshot, MediaSnapshot? previous, ref long? maxDurationMs)
    {
        if (!snapshot.IsSameTrack(previous))
        {
            bool leftover = previous?.DurationMs is long prev
                && snapshot.DurationMs is long cur
                && Math.Abs(cur - prev) <= LeftoverDurationMs;
            maxDurationMs = leftover || snapshot.DurationMs is not > 0 ? null : snapshot.DurationMs;
            return leftover ? snapshot with { DurationMs = null } : snapshot;
        }

        if (snapshot.DurationMs is long d && d > 0)
        {
            maxDurationMs = maxDurationMs is long m ? Math.Max(m, d) : d;
        }

        return maxDurationMs is long md ? snapshot with { DurationMs = md } : snapshot;
    }

    /// <summary>True when a later update finally reports a length after the leftover one was dropped.</summary>
    public static bool DurationArrived(MediaSnapshot current, MediaSnapshot? previous) =>
        previous is not null
        && current.IsSameTrack(previous)
        && previous.DurationMs is not > 0
        && current.DurationMs is > 0;
}
