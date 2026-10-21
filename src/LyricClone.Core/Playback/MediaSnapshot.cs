namespace LyricClone.Core.Playback;

public enum PlaybackStatus
{
    Closed,
    Stopped,
    Paused,
    Playing,
}

/// <summary>What one media session reports at one moment. Immutable; a new snapshot arrives on every change.</summary>
public sealed record MediaSnapshot(
    string AppId,
    string Title,
    string Artist,
    string Album,
    long? DurationMs,
    long PositionMs,
    DateTimeOffset PositionUpdatedAt,
    PlaybackStatus Status,
    double PlaybackRate,
    bool HasTimeline)
{
    public bool HasTrack => !string.IsNullOrWhiteSpace(Title);

    /// <summary>Same track as <paramref name="other"/>, ignoring position and status.</summary>
    public bool IsSameTrack(MediaSnapshot? other) =>
        other is not null
        && string.Equals(AppId, other.AppId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Title, other.Title, StringComparison.Ordinal)
        && string.Equals(Artist, other.Artist, StringComparison.Ordinal)
        && string.Equals(Album, other.Album, StringComparison.Ordinal);
}
