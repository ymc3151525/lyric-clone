namespace LyricClone.Core.Playback;

public enum PlayerHintKind
{
    None,

    /// <summary>Windows' media session API could not be opened at all.</summary>
    SmtcUnavailable,

    /// <summary>No app has a media session.</summary>
    NoPlayer,

    /// <summary>Apps have sessions, but every one is a browser or turned off in per-app settings.</summary>
    AllIgnored,
}

public static class PlayerHint
{
    /// <summary>
    /// What to tell the user a little while after start-up. SMTC being unavailable is always shown, because
    /// nothing can work; the other two respect the "don't show again" setting.
    /// </summary>
    public static PlayerHintKind Decide(bool smtcUnavailable, int sessionCount, bool hasActiveTrack, bool hintsEnabled)
    {
        if (smtcUnavailable)
        {
            return PlayerHintKind.SmtcUnavailable;
        }

        if (hasActiveTrack || !hintsEnabled)
        {
            return PlayerHintKind.None;
        }

        return sessionCount == 0 ? PlayerHintKind.NoPlayer : PlayerHintKind.AllIgnored;
    }
}
