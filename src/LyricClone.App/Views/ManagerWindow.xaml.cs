using System.Net.Http;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LyricClone.App.Services;
using LyricClone.App.ViewModels;
using LyricClone.Core.Library;
using LyricClone.Core.Settings;
using LyricClone.Infrastructure.Data;
using LyricClone.Infrastructure.Lrclib;
using Microsoft.Win32;

namespace LyricClone.App.Views;

/// <summary>S09 "Current Playback" (follows the playing track) and S10 "All Tracks" (library list).</summary>
public partial class ManagerWindow : Window
{
    private readonly ManagerContext _ctx;
    private readonly ManagerViewModel _vm;
    private bool _busy;

    public ManagerWindow(ManagerContext ctx, bool allTracks)
    {
        InitializeComponent();
        _ctx = ctx;
        _vm = new ManagerViewModel(ctx.Repository, ctx.Resolver, ctx.Settings, ctx.NowPlaying, allTracks);
        DataContext = _vm;
        Title = allTracks
            ? Loc.T("全部曲目 — lyric-clone", "All tracks — lyric-clone")
            : Loc.T("正在播放 — lyric-clone", "Now playing — lyric-clone");

        SourcesColumn.Width = new GridLength((double)FindResource("Layout.Manager.SourcesWidth"));
        if (allTracks)
        {
            TrackColumn.Width = new GridLength((double)FindResource("Layout.Manager.TrackListWidth"));
        }
        else
        {
            TrackColumn.Width = new GridLength(0);
            TrackPane.Visibility = Visibility.Collapsed;
            Width = 840;
            MinWidth = 680;
            ctx.NowPlaying.PropertyChanged += OnNowPlayingChanged;
            Closed += (_, _) => ctx.NowPlaying.PropertyChanged -= OnNowPlayingChanged;
        }

        _vm.PropertyChanged += OnVmChanged;
        _vm.Sources.CollectionChanged += (_, _) => UpdateActions();
        Loaded += async (_, _) => await RunAsync(_vm.LoadAsync);
        UpdateActions();
    }

    /// <summary>Opens "All Tracks" with a track preselected (from the Current Playback "open in library" path).</summary>
    public async Task SelectTrackAsync(string trackId)
    {
        await _vm.RefreshTracksAsync();
        _vm.SelectedTrack = _vm.Tracks.FirstOrDefault(t => t.Id == trackId) ?? await _ctx.Repository.GetTrackAsync(trackId);
    }

    private void OnNowPlayingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NowPlayingService.Resolved) or nameof(NowPlayingService.IsResolving))
        {
            Dispatcher.InvokeAsync(async () => await RunAsync(_vm.FollowNowPlayingAsync));
        }
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ManagerViewModel.Search):
            case nameof(ManagerViewModel.FilterIndex):
            case nameof(ManagerViewModel.SortIndex):
                if (_vm.IsAllTracks)
                {
                    _ = RunAsync(_vm.RefreshTracksAsync);
                }

                break;
            case nameof(ManagerViewModel.SelectedSource):
            case nameof(ManagerViewModel.SelectedTrack):
            case nameof(ManagerViewModel.EmptyMessage):
                UpdateActions();
                break;
        }
    }

    private void OnFind(object sender, ExecutedRoutedEventArgs e)
    {
        if (_vm.IsAllTracks)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
    }

    private void UpdateActions()
    {
        var track = _vm.SelectedTrack;
        var item = _vm.SelectedSource;
        var v = item?.Version;

        bool hasTrack = track is not null;
        AddButton.IsEnabled = hasTrack && !_busy;
        SourcesPane.Visibility = hasTrack || _vm.IsAllTracks ? Visibility.Visible : Visibility.Collapsed;
        EmptyPanel.Visibility = v is null ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = !hasTrack
            ? _vm.EmptyMessage
            : item is null ? "" : _busy ? "正在处理…" : "这个来源还没有保存歌词。";

        PrimaryButton.Visibility = hasTrack && item is not null ? Visibility.Visible : Visibility.Collapsed;
        SecondaryButton.Visibility = Visibility.Collapsed;
        ChangeButton.Visibility = item?.IsOnlineSource == true && v is not null ? Visibility.Visible : Visibility.Collapsed;
        PreviewButton.Visibility = v is not null && !v.IsInstrumental ? Visibility.Visible : Visibility.Collapsed;
        MoreButton.Visibility = v is not null ? Visibility.Visible : Visibility.Collapsed;
        InfoBar.Visibility = Visibility.Collapsed;

        foreach (var b in new[] { PrimaryButton, SecondaryButton, ChangeButton, PreviewButton, MoreButton })
        {
            b.IsEnabled = !_busy;
        }

        if (item is null)
        {
            return;
        }

        ChangeButton.Content = "更换版本 ▾";
        bool anyPinned = _vm.Sources.Any(s => s.Version?.Status == VersionStatus.Pinned);

        if (v is null)
        {
            PrimaryButton.Content = LyricsSources.CanFetch(item.Source) ? "获取" : "查找本地文件";
            if (item.IsOnlineSource)
            {
                SecondaryButton.Content = "搜索并选择版本…";
                SecondaryButton.Visibility = Visibility.Visible;
            }

            return;
        }

        if (v.Status == VersionStatus.Pinned)
        {
            PrimaryButton.Content = "取消固定";
        }
        else if (item.IsOnlineSource)
        {
            PrimaryButton.Content = "固定";
            SecondaryButton.Content = v.Status == VersionStatus.Confirmed ? "取消标记" : "标记为正确";
            SecondaryButton.Visibility = Visibility.Visible;
        }
        else
        {
            PrimaryButton.Content = "使用此版本";
            if (anyPinned)
            {
                SecondaryButton.Content = "恢复自动";
                SecondaryButton.Visibility = Visibility.Visible;
            }
        }

        bool isUser = v.Source == LyricsSources.User;
        RefetchItem.Visibility = item.IsOnlineSource ? Visibility.Visible : Visibility.Collapsed;
        EditItem.Visibility = isUser ? Visibility.Visible : Visibility.Collapsed;
        RenameItem.Visibility = isUser ? Visibility.Visible : Visibility.Collapsed;
        OffsetItem.IsEnabled = !v.IsInstrumental;

        var inUse = _vm.Sources.FirstOrDefault(s => s.IsInUse);
        if (!item.IsInUse)
        {
            InfoText.Text = isUser && !anyPinned
                ? "手动添加的歌词不会自动使用。点「使用此版本」后才会生效。"
                : inUse is null ? "这个版本已保存，但当前没有在使用。" : $"这个版本已保存，当前使用的是「{inUse.Title}」。";
            InfoBar.Visibility = Visibility.Visible;
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Dialogs.Info(this, "操作失败", ex is LrclibException or HttpRequestException or TaskCanceledException
                ? $"无法连接 LRCLIB：{ex.Message}"
                : ex.Message);
        }
    }

    private async Task BusyAsync(Func<Task> action)
    {
        _busy = true;
        UpdateActions();
        try
        {
            await RunAsync(action);
        }
        finally
        {
            _busy = false;
            UpdateActions();
        }
    }

    private async void OnPrimary(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTrack is not { } track || _vm.SelectedSource is not { } item)
        {
            return;
        }

        var v = item.Version;
        await BusyAsync(async () =>
        {
            if (v is null)
            {
                var saved = await _ctx.Resolver.FetchAndSaveAsync(track.Id, item.Source, _ctx.Settings.Current.LocalLyricsFolder, CancellationToken.None);
                if (saved is null)
                {
                    Dialogs.Info(this, "没有找到歌词", item.IsOnlineSource
                        ? "LRCLIB 上没有找到匹配的歌词。可以试试「搜索并选择版本」，或者导入本地歌词。"
                        : LyricsSources.IsPlayerCache(item.Source)
                            ? "本机对应播放器的歌词缓存里没有找到这首歌。请先在该播放器里打开过歌词，再点获取。"
                            : "本地歌词文件夹里没有同名的歌词文件。可以在设置里更换文件夹。");
                }

                await _vm.AfterChangeAsync(saved?.Id);
                return;
            }

            if (v.Status == VersionStatus.Pinned)
            {
                await _ctx.Repository.UnpinAsync(v.Id);
            }
            else
            {
                await _ctx.Repository.PinAsync(v.Id);
            }

            await _vm.AfterChangeAsync(v.Id);
        });
    }

    private async void OnSecondary(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSource is not { } item)
        {
            return;
        }

        var v = item.Version;
        if (v is null)
        {
            OnSearch(sender, e);
            return;
        }

        await BusyAsync(async () =>
        {
            if (item.IsOnlineSource)
            {
                if (v.Status == VersionStatus.Confirmed)
                {
                    await _ctx.Repository.UnconfirmAsync(v.Id);
                }
                else
                {
                    await _ctx.Repository.ConfirmAsync(v.Id);
                }
            }
            else if (_vm.Sources.FirstOrDefault(s => s.Version?.Status == VersionStatus.Pinned)?.Version is { } pinned)
            {
                await _ctx.Repository.UnpinAsync(pinned.Id);
            }

            await _vm.AfterChangeAsync(v.Id);
        });
    }

    private void OnAddMenu(object sender, RoutedEventArgs e) => OpenMenu(AddButton);

    private void OnChangeMenu(object sender, RoutedEventArgs e) => OpenMenu(ChangeButton);

    private void OnMoreMenu(object sender, RoutedEventArgs e) => OpenMenu(MoreButton);

    private static void OpenMenu(Button button)
    {
        if (button.ContextMenu is { } menu)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OnRules(object sender, RoutedEventArgs e) =>
        Dialogs.Info(this, "歌词选择规则", _vm.SelectedTrack is null ? "请先选择一首曲目。" : _vm.RulesText);

    private async void OnSearch(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTrack is not { } track)
        {
            return;
        }

        var dialog = new SearchDialog(_ctx, track) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } candidate)
        {
            await BusyAsync(async () =>
            {
                var saved = await _ctx.Resolver.SaveCandidateAsync(track.Id, candidate, CancellationToken.None);
                await _vm.AfterChangeAsync(saved.Id);
            });
        }
    }

    private async void OnById(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTrack is not { } track)
        {
            return;
        }

        var dialog = new ByIdDialog(_ctx, track) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } candidate)
        {
            await BusyAsync(async () =>
            {
                var saved = await _ctx.Resolver.SaveCandidateAsync(track.Id, candidate, CancellationToken.None);
                await _vm.AfterChangeAsync(saved.Id);
            });
        }
    }

    private async void OnAutoReplace(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTrack is not { } track)
        {
            return;
        }

        await BusyAsync(async () =>
        {
            var saved = await _ctx.Resolver.FetchAndSaveAsync(track.Id, LyricsSources.Lrclib, null, CancellationToken.None);
            if (saved is null)
            {
                Dialogs.Info(this, "没有找到歌词", "LRCLIB 上没有找到更合适的版本，原来的版本保持不变。");
            }

            await _vm.AfterChangeAsync(saved?.Id);
        });
    }

    private async void OnRefetch(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTrack is not { } track || _vm.SelectedSource?.Version is not { SourceSongId: { } songId } v
            || !long.TryParse(songId, out var id))
        {
            return;
        }

        await BusyAsync(async () =>
        {
            var candidate = await _ctx.Lrclib.GetByIdAsync(id, CancellationToken.None);
            if (candidate is null)
            {
                Dialogs.Info(this, "无法重新获取", "LRCLIB 上已经找不到这个版本了。");
                return;
            }

            var saved = await _ctx.Resolver.SaveCandidateAsync(track.Id, candidate, CancellationToken.None);
            await _vm.AfterChangeAsync(saved.Id);
        });
    }

    private async void OnImportFile(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTrack is not { } track)
        {
            return;
        }

        var pick = new OpenFileDialog
        {
            Title = "导入歌词文件",
            Filter = "歌词文件 (*.lrc;*.ttml;*.xml;*.txt)|*.lrc;*.ttml;*.xml;*.txt|所有文件 (*.*)|*.*",
        };
        if (pick.ShowDialog(this) != true)
        {
            return;
        }

        string text;
        try
        {
            text = Infrastructure.Lyrics.LocalFileProvider.ReadText(pick.FileName);
        }
        catch (IOException ex)
        {
            Dialogs.Info(this, "无法读取文件", ex.Message);
            return;
        }

        await OpenEditorAsync(track, null, Path.GetFileNameWithoutExtension(pick.FileName), text);
    }

    private async void OnAddFromText(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTrack is { } track)
        {
            await OpenEditorAsync(track, null, "我的歌词", "");
        }
    }

    private async void OnEdit(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTrack is { } track && _vm.SelectedSource?.Version is { } v)
        {
            await OpenEditorAsync(track, v, v.Name, v.Content);
        }
    }

    private async Task OpenEditorAsync(Track track, LyricsVersion? existing, string name, string content)
    {
        var dialog = new LyricsEditorDialog(_ctx.Parser, name, content, existing) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } r)
        {
            return;
        }

        await BusyAsync(async () =>
        {
            var flags = LyricsFlags.From(r.Parsed);
            string id;
            if (existing is null)
            {
                id = (await _ctx.Repository.AddUserVersionAsync(track.Id, r.Name, r.Format, r.Content, r.Translation, flags)).Id;
            }
            else
            {
                await _ctx.Repository.UpdateUserVersionAsync(existing.Id, r.Format, r.Content, r.Translation, flags);
                if (r.Name != existing.Name)
                {
                    await _ctx.Repository.RenameVersionAsync(existing.Id, r.Name);
                }

                id = existing.Id;
            }

            await _vm.AfterChangeAsync(id);
        });
    }

    private async void OnRename(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSource?.Version is not { } v)
        {
            return;
        }

        var name = Dialogs.Prompt(this, "重命名", "版本名称", v.Name);
        if (!string.IsNullOrWhiteSpace(name) && name != v.Name)
        {
            await BusyAsync(async () =>
            {
                await _ctx.Repository.RenameVersionAsync(v.Id, name);
                await _vm.AfterChangeAsync(v.Id);
            });
        }
    }

    private async void OnPreview(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTrack is not { } track || _vm.SelectedSource?.Version is not { } v)
        {
            return;
        }

        var dialog = new OffsetDialog(_ctx, track, v) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await BusyAsync(async () =>
            {
                await _ctx.Repository.SetOffsetAsync(v.Id, dialog.OffsetMs);
                await _vm.AfterChangeAsync(v.Id);
            });
        }
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSource?.Version is not { } v)
        {
            return;
        }

        var message = v.Kind == VersionKind.Online
            ? "删除后，下次播放时可能会重新自动获取。"
            : "删除后无法恢复。";
        if (Dialogs.Confirm(this, "删除此版本？", message, "删除", danger: true))
        {
            await BusyAsync(async () =>
            {
                await _ctx.Repository.DeleteVersionAsync(v.Id);
                await _vm.AfterChangeAsync();
            });
        }
    }

    private async void OnRefreshLibrary(object sender, RoutedEventArgs e) => await RunAsync(_vm.RefreshTracksAsync);
}
