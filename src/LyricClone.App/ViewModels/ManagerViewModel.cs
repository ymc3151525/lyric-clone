using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LyricClone.App.Services;
using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;
using LyricClone.Infrastructure.Data;
using LyricClone.Infrastructure.Lyrics;
using LyricClone.Infrastructure.Settings;

namespace LyricClone.App.ViewModels;

public sealed record LabelChip(string Text, bool IsAccent = false);

/// <summary>One row of the "Lyric sources" list: an online source (maybe not fetched yet) or a saved local version.</summary>
public sealed class SourceItem
{
    public required string Source { get; init; }

    public LyricsVersion? Version { get; init; }

    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public required string StatusText { get; init; }

    public bool IsInUse { get; init; }

    public IReadOnlyList<LabelChip> Labels { get; init; } = [];

    public bool IsOnlineSource => LyricsSources.IsOnline(Source);
}

public sealed record PreviewLine(string Time, string Text, string? Translation);

public sealed partial class ManagerViewModel : ObservableObject
{
    private readonly ILibraryRepository _repo;
    private readonly ILyricsResolver _resolver;
    private readonly ISettingsStore _settings;
    private readonly NowPlayingService _nowPlaying;

    [ObservableProperty]
    private bool _isAllTracks;

    [ObservableProperty]
    private string _search = "";

    [ObservableProperty]
    private int _filterIndex;

    [ObservableProperty]
    private int _sortIndex;

    [ObservableProperty]
    private Track? _selectedTrack;

    [ObservableProperty]
    private SourceItem? _selectedSource;

    [ObservableProperty]
    private string _emptyMessage = "";

    [ObservableProperty]
    private string _rulesText = "";

    public ManagerViewModel(ILibraryRepository repo, ILyricsResolver resolver, ISettingsStore settings, NowPlayingService nowPlaying, bool allTracks)
    {
        _repo = repo;
        _resolver = resolver;
        _settings = settings;
        _nowPlaying = nowPlaying;
        _isAllTracks = allTracks;
    }

    public ObservableCollection<Track> Tracks { get; } = [];

    public ObservableCollection<SourceItem> Sources { get; } = [];

    public ObservableCollection<PreviewLine> Preview { get; } = [];

    public ObservableCollection<LabelChip> SelectedLabels { get; } = [];

    public ILibraryRepository Repository => _repo;

    public NowPlayingService NowPlaying => _nowPlaying;

    public string TrackHeader => SelectedTrack is null ? "" : $"{SelectedTrack.Title} — {SelectedTrack.Artist}";

    public string TrackDetails => SelectedTrack is null
        ? ""
        : string.Join("  ·  ", new[] { SelectedTrack.Album, FormatDuration(SelectedTrack.DurationMs) }.Where(s => !string.IsNullOrEmpty(s)));

    public string AppIdForRules => _nowPlaying.Snapshot?.AppId ?? SelectedTrack?.FirstSeenAppId ?? "";

    public IReadOnlyList<string> SourceOrder => _settings.Current.EffectiveSourceOrder(AppIdForRules);

    public async Task LoadAsync()
    {
        if (IsAllTracks)
        {
            await RefreshTracksAsync();
        }
        else
        {
            await FollowNowPlayingAsync();
        }
    }

    public async Task FollowNowPlayingAsync()
    {
        var trackId = _nowPlaying.Resolved.Track?.Id;
        if (trackId is null)
        {
            SelectedTrack = null;
            EmptyMessage = _nowPlaying.IsResolving ? "正在识别当前曲目…" : "请先在任意音乐软件里播放一首歌。";
            Sources.Clear();
            ClearPreview();
            return;
        }

        if (SelectedTrack?.Id == trackId)
        {
            await RefreshSourcesAsync();
            return;
        }

        SelectedTrack = await _repo.GetTrackAsync(trackId);
    }

    public async Task RefreshTracksAsync()
    {
        var query = new TrackQuery(
            Search: string.IsNullOrWhiteSpace(Search) ? null : Search,
            HasPinned: FilterIndex == 1 ? true : FilterIndex == 2 ? false : null,
            HasLocal: FilterIndex == 3 ? true : null,
            Source: FilterIndex == 4 ? LyricsSources.Lrclib : null,
            Sort: SortIndex switch { 1 => TrackSort.RecentlyManaged, 2 => TrackSort.Title, _ => TrackSort.RecentlyPlayed });
        var keep = SelectedTrack?.Id;
        var tracks = await _repo.QueryTracksAsync(query);
        Tracks.Clear();
        foreach (var t in tracks)
        {
            Tracks.Add(t);
        }

        EmptyMessage = tracks.Count == 0
            ? (string.IsNullOrWhiteSpace(Search) && FilterIndex == 0 ? "还没有保存任何曲目。播放歌曲后会出现在这里。" : "没有符合条件的曲目。")
            : "";
        SelectedTrack = tracks.FirstOrDefault(t => t.Id == keep) ?? tracks.FirstOrDefault();
    }

    public async Task RefreshSourcesAsync(string? selectVersionId = null)
    {
        var track = SelectedTrack;
        Sources.Clear();
        ClearPreview();
        if (track is null)
        {
            return;
        }

        var versions = await _repo.GetVersionsAsync(track.Id);
        var order = SourceOrder;
        var inUse = VersionSelector.Select(versions, order);
        RulesText = BuildRules(order, inUse);

        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in new[] { LyricsSources.QqCache, LyricsSources.NeteaseCache, LyricsSources.KugouCache, LyricsSources.Lrclib })
        {
            Sources.Add(MakeItem(source, versions.FirstOrDefault(v => v.Source == source), inUse.Version?.Id));
            listed.Add(source);
        }

        foreach (var v in versions.Where(v => v.Kind == VersionKind.Local && !listed.Contains(v.Source)).OrderBy(v => v.CreatedAt))
        {
            Sources.Add(MakeItem(v.Source, v, inUse.Version?.Id));
        }

        var wanted = selectVersionId ?? SelectedSource?.Version?.Id ?? inUse.Version?.Id;
        SelectedSource = Sources.FirstOrDefault(s => s.Version?.Id == wanted && wanted is not null) ?? Sources.FirstOrDefault();
    }

    /// <summary>Called after any change that may alter which version plays now.</summary>
    public async Task AfterChangeAsync(string? selectVersionId = null)
    {
        await RefreshSourcesAsync(selectVersionId);
        if (SelectedTrack is not null && _nowPlaying.Resolved.Track?.Id == SelectedTrack.Id)
        {
            await _nowPlaying.ReloadCurrentAsync();
        }
    }

    public ParsedLyrics ParseSelected() =>
        SelectedSource?.Version is { } v ? _resolver.Parse(v) : ParsedLyrics.Empty;

    partial void OnSelectedTrackChanged(Track? value)
    {
        OnPropertyChanged(nameof(TrackHeader));
        OnPropertyChanged(nameof(TrackDetails));
        _ = RefreshSourcesAsync();
    }

    partial void OnSelectedSourceChanged(SourceItem? value)
    {
        ClearPreview();
        if (value?.Version is not { } v)
        {
            return;
        }

        foreach (var l in value.Labels)
        {
            SelectedLabels.Add(l);
        }

        if (v.IsInstrumental)
        {
            Preview.Add(new PreviewLine("", "纯音乐，没有歌词", null));
            return;
        }

        var parsed = _resolver.Parse(v);
        if (parsed.IsEmpty)
        {
            Preview.Add(new PreviewLine("", "这个版本无法解析，请检查格式。", null));
            return;
        }

        foreach (var line in parsed.Lines)
        {
            Preview.Add(new PreviewLine(parsed.IsSynced ? FormatTime(line.StartMs) : "", line.Text, line.Translation));
        }
    }

    private void ClearPreview()
    {
        Preview.Clear();
        SelectedLabels.Clear();
    }

    private static SourceItem MakeItem(string source, LyricsVersion? v, string? inUseId)
    {
        bool inUse = v is not null && v.Id == inUseId;
        var labels = new List<LabelChip>();
        if (v is not null)
        {
            if (v.IsInstrumental)
            {
                labels.Add(new LabelChip("纯音乐"));
            }
            else
            {
                labels.Add(new LabelChip(v.HasWordSync ? "逐字" : v.HasLineSync ? "逐行" : "无时间轴"));
            }

            if (v.HasTranslation)
            {
                labels.Add(new LabelChip("翻译"));
            }

            if (v.OffsetMs != 0)
            {
                labels.Add(new LabelChip($"偏移 {v.OffsetMs:+#;-#;0} ms"));
            }

            if (inUse)
            {
                labels.Add(new LabelChip("使用中", IsAccent: true));
            }
        }

        string status = v is null
            ? (LyricsSources.IsPlayerCache(source) ? "尚未存取" : "未获取")
            : v.Status switch
            {
                VersionStatus.Pinned => "已固定",
                VersionStatus.Confirmed => "已标记为正确",
                _ => "已保存",
            };

        return new SourceItem
        {
            Source = source,
            Version = v,
            Title = source switch
            {
                LyricsSources.Lrclib => "LRCLIB",
                LyricsSources.QqCache => "QQ音乐",
                LyricsSources.NeteaseCache => "网易云音乐",
                LyricsSources.KugouCache => "酷狗音乐",
                LyricsSources.LocalFile => v?.Name is { Length: > 0 } n ? $"本地文件夹 · {n}" : "本地文件夹",
                LyricsSources.Embedded => "音频文件内嵌",
                _ => v?.Name ?? "本地歌词",
            },
            Subtitle = v is null
                ? LyricsSources.IsPlayerCache(source) ? "尚未存取" : "点击「获取歌词」搜索此来源"
                : source == LyricsSources.Lrclib ? $"{v.Name}  ·  ID {v.SourceSongId}"
                : LyricsSources.IsPlayerCache(source) ? v.Name.Replace(" — ", "-", StringComparison.Ordinal)
                : FormatDate(v.UpdatedAt),
            StatusText = status,
            IsInUse = inUse,
            Labels = labels,
        };
    }

    private static string BuildRules(IReadOnlyList<string> order, VersionSelection selection)
    {
        var names = order.Select(s => s switch
        {
            LyricsSources.Lrclib => "LRCLIB",
            LyricsSources.QqCache => "QQ音乐缓存",
            LyricsSources.NeteaseCache => "网易云缓存",
            LyricsSources.KugouCache => "酷狗缓存",
            LyricsSources.LocalFile => "本地文件夹",
            LyricsSources.Embedded => "音频文件内嵌",
            _ => s,
        });
        var reason = selection.Reason switch
        {
            SelectionReason.Pinned => "当前使用的是你固定的版本。",
            SelectionReason.Confirmed => "当前使用的是按来源顺序找到的第一个「已标记为正确」的版本。",
            SelectionReason.SourceOrder => "当前使用的是按来源顺序找到的第一个已保存版本。",
            _ => "目前没有可用的版本。",
        };
        return "选择规则：\n"
            + "1. 有固定的版本时，始终使用固定的版本。\n"
            + "2. 否则按来源顺序，优先使用标记为正确的在线版本。\n"
            + "3. 再按来源顺序使用已保存的版本；都没有时依次在线查找。\n"
            + "4. 手动添加的本地歌词需要点「使用此版本」才会生效。\n\n"
            + $"来源顺序：{string.Join(" → ", names)}\n\n{reason}";
    }

    public static string FormatDuration(long? ms) =>
        ms is > 0 ? TimeSpan.FromMilliseconds(ms.Value).ToString(ms.Value >= 3_600_000 ? @"h\:mm\:ss" : @"m\:ss") : "";

    public static string FormatTime(long ms) =>
        ms < 0 ? "" : TimeSpan.FromMilliseconds(ms).ToString(@"mm\:ss\.ff");

    private static string FormatDate(DateTimeOffset t) => t.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
