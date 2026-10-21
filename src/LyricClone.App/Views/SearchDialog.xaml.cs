using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LyricClone.App.Services;
using LyricClone.App.ViewModels;
using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;
using LyricClone.Infrastructure.Lrclib;

namespace LyricClone.App.Views;

/// <summary>S11: search LRCLIB, preview a result, save the chosen one.</summary>
public partial class SearchDialog : Window
{
    private const long DurationToleranceMs = 2000;

    private readonly ManagerContext _ctx;
    private readonly Track _track;
    private CancellationTokenSource? _cts;

    public SearchDialog(ManagerContext ctx, Track track)
    {
        InitializeComponent();
        _ctx = ctx;
        _track = track;
        TrackText.Text = $"当前曲目：{track.Title} — {track.Artist}  {ManagerViewModel.FormatDuration(track.DurationMs)}";
        QueryBox.Text = $"{TrackNormalizer.CleanTitle(track.Title)} {TrackNormalizer.PrimaryArtist(track.Artist)}".Trim();
        ResultsEmpty.Text = "输入歌名和艺人后搜索。";
        Loaded += (_, _) =>
        {
            QueryBox.Focus();
            QueryBox.SelectAll();
            OnSearch(this, new RoutedEventArgs());
        };
        Closed += (_, _) => _cts?.Cancel();
    }

    public LyricsCandidate? Result { get; private set; }

    private sealed record Row(LyricsCandidate Candidate, string Title, string Line2, string Line3);

    private void OnQueryKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnSearch(sender, e);
        }
    }

    private async void OnSearch(object sender, RoutedEventArgs e)
    {
        var query = QueryBox.Text.Trim();
        if (query.Length == 0)
        {
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        SearchButton.IsEnabled = false;
        Results.ItemsSource = null;
        ResultsEmpty.Text = "正在搜索…";
        ResultsEmpty.Visibility = Visibility.Visible;
        try
        {
            var found = await _ctx.Lrclib.SearchAsync(query, ct);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            Results.ItemsSource = found.Select(c => new Row(
                c,
                c.Title,
                string.Join("  ·  ", new[] { c.Artist, c.Album }.Where(s => !string.IsNullOrEmpty(s))),
                string.Join("  ·  ", new[]
                {
                    ManagerViewModel.FormatDuration(c.DurationMs),
                    c.IsInstrumental ? "纯音乐" : c.HasWordSync ? "逐字" : c.HasSyncedContent ? "逐行" : "无时间轴",
                    $"ID {c.SourceSongId}",
                }.Where(s => s.Length > 0)))).ToList();
            ResultsEmpty.Text = found.Count == 0 ? "没有搜索结果。可以换个关键词，比如只搜歌名。" : "";
            ResultsEmpty.Visibility = found.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is LrclibException or HttpRequestException or TaskCanceledException)
        {
            if (!ct.IsCancellationRequested)
            {
                ResultsEmpty.Text = $"搜索失败：{ex.Message}";
            }
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private void OnSelect(object sender, SelectionChangedEventArgs e)
    {
        var row = Results.SelectedItem as Row;
        SaveButton.IsEnabled = row is not null;
        WarningText.Text = "";
        PreviewText.Text = "";
        if (row is null)
        {
            return;
        }

        var c = row.Candidate;
        if (_track.DurationMs is { } mine && c.DurationMs is { } theirs && Math.Abs(mine - theirs) > DurationToleranceMs)
        {
            WarningText.Text = $"时长相差 {Math.Abs(mine - theirs) / 1000.0:0.#} 秒，时间轴可能对不上。";
        }

        if (c.IsInstrumental)
        {
            PreviewText.Text = "纯音乐，没有歌词。";
            return;
        }

        var parsed = string.IsNullOrWhiteSpace(c.Content) ? ParsedLyrics.Empty : _ctx.Parser.Parse(c.Content, c.Format, c.Translation);
        var sb = new StringBuilder();
        foreach (var line in parsed.Lines)
        {
            if (parsed.IsSynced)
            {
                sb.Append(ManagerViewModel.FormatTime(line.StartMs)).Append("   ");
            }

            sb.AppendLine(line.Text);
        }

        PreviewText.Text = sb.Length == 0 ? "这个结果没有歌词内容。" : sb.ToString();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (Results.SelectedItem is Row row)
        {
            Result = row.Candidate;
            DialogResult = true;
        }
    }
}
