using LyricClone.Core.Lyrics;

namespace LyricClone.Core.Library;

public enum SelectionReason
{
    None,
    Pinned,
    Confirmed,
    SourceOrder,
}

public sealed record VersionSelection(LyricsVersion? Version, SelectionReason Reason);

/// <summary>
/// Pinned first. Otherwise confirmed online versions, then any saved version, each walked in source order.
/// Instrumental markers count as a result so the app stops searching.
/// Versions the user added by hand (source "user") are only used once pinned.
/// </summary>
public static class VersionSelector
{
    public static VersionSelection Select(IReadOnlyCollection<LyricsVersion> versions, IReadOnlyList<string> sourceOrder, VersionPickMode pick = VersionPickMode.SourceFirst)
    {
        if (versions.Count == 0)
        {
            return new VersionSelection(null, SelectionReason.None);
        }

        var pinned = versions.FirstOrDefault(v => v.Status == VersionStatus.Pinned);
        if (pinned is not null)
        {
            return new VersionSelection(pinned, SelectionReason.Pinned);
        }

        var order = EffectiveOrder(sourceOrder);

        foreach (var source in order)
        {
            var confirmed = versions
                .Where(v => v.Source == source && v.Status == VersionStatus.Confirmed)
                .OrderByDescending(v => v.UpdatedAt)
                .FirstOrDefault();
            if (confirmed is not null)
            {
                return new VersionSelection(confirmed, SelectionReason.Confirmed);
            }
        }

        if (pick == VersionPickMode.PreferQuality)
        {
            var best = versions
                .Where(v => v.Source != LyricsSources.User)
                .OrderByDescending(Quality)
                .ThenBy(v => IndexOf(order, v.Source))
                .ThenByDescending(v => v.UpdatedAt)
                .FirstOrDefault();
            if (best is not null)
            {
                return new VersionSelection(best, SelectionReason.SourceOrder);
            }
        }

        foreach (var source in order)
        {
            var cached = versions
                .Where(v => v.Source == source && v.Source != LyricsSources.User)
                .OrderByDescending(v => v.HasWordSync)
                .ThenByDescending(v => v.HasLineSync)
                .ThenByDescending(v => v.UpdatedAt)
                .FirstOrDefault();
            if (cached is not null)
            {
                return new VersionSelection(cached, SelectionReason.SourceOrder);
            }
        }

        return new VersionSelection(null, SelectionReason.None);
    }

    private static int Quality(LyricsVersion v) =>
        v.HasSyllable ? 3 : v.HasWordSync ? 2 : v.HasLineSync ? 1 : 0;

    private static int IndexOf(IReadOnlyList<string> order, string source)
    {
        int i = 0;
        foreach (var s in order)
        {
            if (s == source)
            {
                return i;
            }

            i++;
        }

        return int.MaxValue;
    }

    public static readonly TimeSpan StaleOnlineAge = TimeSpan.FromDays(7);

    /// <summary>Cached (not pinned / confirmed) online versions older than a week, used by "auto-update saved lyrics".</summary>
    public static bool IsStaleOnlineCache(LyricsVersion version, DateTimeOffset now) =>
        version.Status == VersionStatus.Cached
        && LyricsSources.IsOnline(version.Source)
        && (version.FetchedAt is null || now - version.FetchedAt > StaleOnlineAge);


    /// <summary>Sources still worth asking, in order, when nothing saved was selected.</summary>
    public static IReadOnlyList<string> SourcesToFetch(
        IReadOnlyCollection<LyricsVersion> versions,
        IReadOnlyList<string> sourceOrder,
        IReadOnlyCollection<string> recentMisses)
    {
        var have = versions.Select(v => v.Source).ToHashSet(StringComparer.Ordinal);
        return EffectiveOrder(sourceOrder)
            .Where(s => !have.Contains(s) && !recentMisses.Contains(s))
            .ToArray();
    }

    /// <summary>
    /// Offline sources ranked above a version picked only by source order and not saved yet.
    /// They are cheap to check, so a file dropped into the lyrics folder wins over an older online cache.
    /// </summary>
    public static IReadOnlyList<string> OfflineSourcesAhead(
        IReadOnlyCollection<LyricsVersion> versions,
        IReadOnlyList<string> sourceOrder,
        VersionSelection selection)
    {
        if (selection.Reason != SelectionReason.SourceOrder || selection.Version is null)
        {
            return [];
        }

        var have = versions.Select(v => v.Source).ToHashSet(StringComparer.Ordinal);
        return EffectiveOrder(sourceOrder)
            .TakeWhile(s => s != selection.Version.Source)
            .Where(s => !LyricsSources.IsOnline(s) && !have.Contains(s))
            .ToArray();
    }

    private static IReadOnlyList<string> EffectiveOrder(IReadOnlyList<string> sourceOrder) =>
        sourceOrder.Count > 0 ? sourceOrder : LyricsSources.DefaultOrder;
}
