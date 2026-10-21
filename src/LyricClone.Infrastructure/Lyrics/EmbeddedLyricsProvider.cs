using System.Text;
using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;

namespace LyricClone.Infrastructure.Lyrics;

/// <summary>
/// Lyrics stored inside audio files. SMTC does not say which file is playing, so the music folder is indexed by tags
/// (title / artist / album / length of files that carry lyrics) and the playing track is matched against that index.
/// The index is kept in memory and refreshed at most every few minutes; unchanged files are not read again.
/// </summary>
public sealed class EmbeddedLyricsProvider
{
    private const double MinScore = 0.8;
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromMinutes(5);

    private readonly Func<string?> _folder;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Entry> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private string? _indexedFolder;
    private DateTime _indexedAt;
    private IReadOnlyList<Entry> _withLyrics = [];

    public EmbeddedLyricsProvider(Func<string?> folder)
    {
        _folder = folder;
    }

    public static string DefaultFolder => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

    public string? Folder => _folder() is { Length: > 0 } f ? f : DefaultFolder;

    /// <summary>Files with lyrics found by the last index run.</summary>
    public int IndexedCount => _withLyrics.Count;

    private sealed record Entry(string Path, long Size, DateTime Modified, string Title, string Artist, long? DurationMs, bool HasLyrics);

    public Task WarmUpAsync(CancellationToken ct) => EnsureIndexAsync(force: false, ct);

    /// <summary>Drops the refresh timer so the next lookup re-scans the folder (e.g. after the user picked a new one).</summary>
    public void Invalidate() => _indexedAt = DateTime.MinValue;

    public async Task<LyricsCandidate?> FindAsync(string title, string artist, long? durationMs, CancellationToken ct)
    {
        var entries = await EnsureIndexAsync(force: false, ct);
        var best = entries
            .Select(e => (e, score: TrackNormalizer.Score(title, artist, durationMs, e.Title, e.Artist, e.DurationMs)))
            .Where(x => x.score >= MinScore)
            .OrderByDescending(x => x.score)
            .Select(x => x.e)
            .FirstOrDefault();
        if (best is null)
        {
            return null;
        }

        var tags = AudioTagReader.Read(best.Path);
        return tags is { HasLyrics: true } ? ToCandidate(tags) : null;
    }

    public static LyricsCandidate ToCandidate(AudioTags tags)
    {
        LyricsFormat format;
        string content;
        bool wordSync = false;
        if (tags.Synced is { Count: > 0 } synced)
        {
            (content, wordSync) = SyncedToLrc(synced);
            format = LyricsFormat.Lrc;
        }
        else
        {
            content = tags.Lyrics!.Trim();
            format = LrcParser.LooksLikeLrc(content) ? LyricsFormat.Lrc
                : content.StartsWith('<') && content.Contains("<tt", StringComparison.OrdinalIgnoreCase) ? LyricsFormat.Ttml
                : LyricsFormat.Plain;
            wordSync = format == LyricsFormat.Lrc && content.Contains('<');
        }

        return new LyricsCandidate(LyricsSources.Embedded, null, tags.Title, tags.Artist, tags.Album, tags.DurationMs, format, content, null, false, wordSync);
    }

    /// <summary>
    /// SYLT entries are either one per line, or one per word with a leading line break marking a new line.
    /// Word entries become enhanced LRC ("[line]&lt;word&gt;text&lt;word&gt;text").
    /// </summary>
    public static (string Lrc, bool WordSync) SyncedToLrc(IReadOnlyList<SyncedText> entries)
    {
        bool words = entries.Skip(1).Any(e => e.Text.StartsWith('\n') || e.Text.StartsWith('\r'));
        var sb = new StringBuilder();
        if (!words)
        {
            foreach (var e in entries)
            {
                sb.Append('[').Append(Clock(e.TimeMs)).Append(']').Append(e.Text.Trim()).Append('\n');
            }

            return (sb.ToString().TrimEnd('\n'), false);
        }

        bool open = false;
        foreach (var e in entries)
        {
            bool newLine = !open || e.Text.StartsWith('\n') || e.Text.StartsWith('\r');
            var text = e.Text.TrimStart('\r', '\n');
            if (newLine)
            {
                if (open)
                {
                    sb.Append('\n');
                }

                sb.Append('[').Append(Clock(e.TimeMs)).Append(']');
                open = true;
            }

            sb.Append('<').Append(Clock(e.TimeMs)).Append('>').Append(text);
        }

        return (sb.ToString(), true);
    }

    private static string Clock(long ms) => $"{ms / 60000:00}:{ms / 1000 % 60:00}.{ms % 1000 / 10:00}";

    private async Task<IReadOnlyList<Entry>> EnsureIndexAsync(bool force, CancellationToken ct)
    {
        var folder = Folder;
        if (!force && folder == _indexedFolder && DateTime.UtcNow - _indexedAt < RefreshAfter)
        {
            return _withLyrics;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (!force && folder == _indexedFolder && DateTime.UtcNow - _indexedAt < RefreshAfter)
            {
                return _withLyrics;
            }

            await Task.Run(() => Rebuild(folder, ct), ct);
            return _withLyrics;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Rebuild(string? folder, CancellationToken ct)
    {
        if (!string.Equals(folder, _indexedFolder, StringComparison.OrdinalIgnoreCase))
        {
            _byPath.Clear();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
            foreach (var path in Directory.EnumerateFiles(folder, "*", options))
            {
                ct.ThrowIfCancellationRequested();
                if (!AudioTagReader.Extensions.Contains(Path.GetExtension(path)))
                {
                    continue;
                }

                seen.Add(path);
                FileInfo info;
                try
                {
                    info = new FileInfo(path);
                }
                catch (IOException)
                {
                    continue;
                }

                if (_byPath.TryGetValue(path, out var known) && known.Size == info.Length && known.Modified == info.LastWriteTimeUtc)
                {
                    continue;
                }

                var tags = AudioTagReader.Read(path);
                if (tags is null)
                {
                    _byPath[path] = new Entry(path, info.Length, info.LastWriteTimeUtc, "", "", null, false);
                    continue;
                }

                var (title, artist) = string.IsNullOrWhiteSpace(tags.Title)
                    ? TrackNormalizer.SplitCombined(Path.GetFileNameWithoutExtension(path), tags.Artist)
                    : (tags.Title, tags.Artist);
                _byPath[path] = new Entry(path, info.Length, info.LastWriteTimeUtc, title, artist, tags.DurationMs, tags.HasLyrics);
            }
        }

        foreach (var gone in _byPath.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _byPath.Remove(gone);
        }

        _withLyrics = _byPath.Values.Where(e => e.HasLyrics).ToList();
        _indexedFolder = folder;
        _indexedAt = DateTime.UtcNow;
    }
}
