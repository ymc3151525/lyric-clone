using System.Windows;
using System.Windows.Controls;
using LyricClone.App.ViewModels;
using LyricClone.App.Views;
using LyricClone.Core.Library;
using LyricClone.Infrastructure.Lyrics;
using static LyricClone.Flows.Tests.UiDriver;

namespace LyricClone.Flows.Tests;

/// <summary>F02-F06 and F08 driven through the real ManagerWindow and its dialogs.</summary>
public sealed class ManagerFlowTests
{
    private static async Task<(ManagerWindow Window, ManagerViewModel Vm)> OpenAsync(FlowEnv env, bool allTracks)
    {
        var w = new ManagerWindow(env.Context(), allTracks) { ShowActivated = false, Left = -10_000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual };
        w.Show();
        var vm = (ManagerViewModel)w.DataContext;
        await Sta.Until(() => vm.SelectedTrack is null || vm.Sources.Count > 0, "manager loaded");
        return (w, vm);
    }

    private static async Task<NowPlayingStarted> PlayingAsync(FlowEnv env, string title = "示例曲目")
    {
        var np = await env.StartAsync();
        env.Sessions.Play("demo.player", title, "虚构乐队");
        await Sta.Until(() => np.Resolved.State is ResolveState.Found or ResolveState.NotFound && !np.IsResolving, "resolved");
        return new NowPlayingStarted(np);
    }

    private sealed record NowPlayingStarted(LyricClone.App.Services.NowPlayingService Np);

    private static SourceItem Source(ManagerViewModel vm, string title) =>
        vm.Sources.First(s => s.Title.StartsWith(title, StringComparison.Ordinal));

    [Fact]
    public void F02_H1_search_pick_another_version_then_pin_it()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        env.Lrclib.Add(2, "示例曲目 (现场版)", "虚构乐队", Samples.LrcB, durationMs: 260_000);
        Sta.Run(async () =>
        {
            var np = (await PlayingAsync(env)).Np;
            var (w, vm) = await OpenAsync(env, allTracks: false);
            Assert.Equal("示例曲目 — 虚构乐队", vm.TrackHeader);
            Assert.True(Source(vm, "LRCLIB").IsInUse);

            var dialog = WhenDialog<SearchDialog>(async d =>
            {
                var results = Sta.Find<ListBox>(d, "Results");
                await Sta.Until(() => results.Items.Count == 2, "two results");
                results.SelectedIndex = 1;
                var warning = Sta.Find<TextBlock>(d, "WarningText");
                Assert.Contains("时长相差 60 秒", warning.Text);
                Press(Sta.Find<Button>(d, "SaveButton"));
            });
            Menu(Sta.Find<Button>(w, "ChangeButton"), "搜索并选择版本…");
            await dialog;

            await Sta.Until(() => np.Lyrics.Lines.FirstOrDefault()?.Text == "Made-up B line one", "playing lyrics replaced");
            await Sta.Until(() => Source(vm, "LRCLIB").Subtitle.Contains("ID 2"), "source row shows ID 2");

            vm.SelectedSource = Source(vm, "LRCLIB");
            Assert.Equal("固定", Sta.Find<Button>(w, "PrimaryButton").Content);
            Sta.Click(w, "PrimaryButton");
            await Sta.Until(() => np.Resolved.Reason == SelectionReason.Pinned, "pinned version in use");
            await Sta.Until(() => (string)Sta.Find<Button>(w, "PrimaryButton").Content == "取消固定", "button flips to unpin");
            w.Close();
        });
    }

    [Fact]
    public void F02_E1_no_search_results_shows_hint_and_save_stays_disabled()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        Sta.Run(async () =>
        {
            await PlayingAsync(env);
            var (w, _) = await OpenAsync(env, allTracks: false);
            var dialog = WhenDialog<SearchDialog>(async d =>
            {
                var query = Sta.Find<TextBox>(d, "QueryBox");
                query.Text = "完全不存在的关键词";
                Press(Sta.Find<Button>(d, "SearchButton"));
                var empty = Sta.Find<TextBlock>(d, "ResultsEmpty");
                await Sta.Until(() => empty.Text.StartsWith("没有搜索结果"), "empty hint");
                Assert.False(Sta.Find<Button>(d, "SaveButton").IsEnabled);
                d.Close();
            });
            Menu(Sta.Find<Button>(w, "ChangeButton"), "搜索并选择版本…");
            await dialog;
            w.Close();
        });
    }

    [Fact]
    public void F02_E2_window_follows_track_changes()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        env.Lrclib.Add(2, "另一首示例", "虚构乐队", Samples.LrcB);
        Sta.Run(async () =>
        {
            await PlayingAsync(env);
            var (w, vm) = await OpenAsync(env, allTracks: false);
            env.Sessions.Play("demo.player", "另一首示例", "虚构乐队");
            await Sta.Until(() => vm.TrackHeader == "另一首示例 — 虚构乐队", "header follows");
            await Sta.Until(() => vm.Preview.FirstOrDefault()?.Text == "Made-up B line one", "preview follows");

            env.Sessions.Stop();
            await Sta.Until(() => vm.SelectedTrack is null, "cleared when playback stops");
            Assert.Equal(Visibility.Collapsed, Sta.Find<FrameworkElement>(w, "SourcesPane").Visibility);
            w.Close();
        });
    }

    [Fact]
    public void F03_H1_specify_version_by_lrclib_link()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        env.Lrclib.Add(4242, "示例曲目", "虚构乐队", Samples.LrcB);
        Sta.Run(async () =>
        {
            var np = (await PlayingAsync(env)).Np;
            var (w, vm) = await OpenAsync(env, allTracks: false);
            var dialog = WhenDialog<ByIdDialog>(async d =>
            {
                Descendants<TextBox>(d).First().Text = "https://lrclib.net/api/get/4242";
                Press(ButtonWithText(d, "获取"));
                await Sta.Until(() => Descendants<Button>(d).Any(b => b.Content as string == "保存此版本"), "fetched");
                Press(ButtonWithText(d, "保存此版本"));
            });
            Menu(Sta.Find<Button>(w, "AddButton"), "通过 LRCLIB ID 或链接指定版本…");
            await dialog;
            await Sta.Until(() => np.Lyrics.Lines.FirstOrDefault()?.Text == "Made-up B line one", "id version playing");
            Assert.Contains("id 4242", env.Lrclib.Calls);
            w.Close();
        });
    }

    [Fact]
    public void F03_N1_unknown_id_and_garbage_input_show_messages()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        Sta.Run(async () =>
        {
            await PlayingAsync(env);
            var (w, _) = await OpenAsync(env, allTracks: false);
            var dialog = WhenDialog<ByIdDialog>(async d =>
            {
                var input = Descendants<TextBox>(d).First();
                var status = Descendants<TextBlock>(d).First(t => t.MaxWidth == 420);
                input.Text = "not an id";
                Press(ButtonWithText(d, "获取"));
                await Sta.Until(() => status.Text.StartsWith("无法识别"), "garbage rejected");
                Assert.DoesNotContain(env.Lrclib.Calls, c => c.StartsWith("id "));

                input.Text = "999999";
                Press(ButtonWithText(d, "获取"));
                await Sta.Until(() => status.Text.Contains("没有 ID 为 999999"), "unknown id");
                d.Close();
            });
            Menu(Sta.Find<Button>(w, "AddButton"), "通过 LRCLIB ID 或链接指定版本…");
            await dialog;
            w.Close();
        });
    }

    [Fact]
    public void F05_H1_add_from_text_is_not_used_until_use_version_then_restore_auto()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        Sta.Run(async () =>
        {
            var np = (await PlayingAsync(env)).Np;
            var (w, vm) = await OpenAsync(env, allTracks: false);

            var dialog = WhenDialog<LyricsEditorDialog>(async d =>
            {
                Sta.Find<TextBox>(d, "ContentBox").Text = "[00:00.50]my own line one\n[00:04.50]my own line two";
                Sta.Find<TextBox>(d, "TranslationBox").Text = "[00:00.50]我自己写的 第一行";
                await Sta.Until(() => Sta.Find<TextBlock>(d, "StatusText").Text.Contains("1 行带翻译"), "parse status");
                Press(Sta.Find<Button>(d, "SaveButton"));
            });
            Menu(Sta.Find<Button>(w, "AddButton"), "从文本添加歌词…");
            await dialog;

            await Sta.Until(() => vm.Sources.Any(s => s.Source == LyricsSources.User), "user version listed");
            var mine = vm.Sources.First(s => s.Source == LyricsSources.User);
            Assert.False(mine.IsInUse);
            Assert.Equal(LyricsSources.Lrclib, np.Resolved.Version?.Source);
            vm.SelectedSource = mine;
            Assert.Equal(Visibility.Visible, Sta.Find<FrameworkElement>(w, "InfoBar").Visibility);
            Assert.Equal("my own line one", vm.Preview[0].Text);
            Assert.Equal("我自己写的 第一行", vm.Preview[0].Translation);

            Assert.Equal("使用此版本", Sta.Find<Button>(w, "PrimaryButton").Content);
            Sta.Click(w, "PrimaryButton");
            await Sta.Until(() => np.Resolved.Version?.Source == LyricsSources.User, "user version playing");

            vm.SelectedSource = vm.Sources.First(s => s.Source == LyricsSources.User);
            Assert.Equal("取消固定", Sta.Find<Button>(w, "PrimaryButton").Content);
            Sta.Click(w, "PrimaryButton");
            await Sta.Until(() => np.Resolved.Version?.Source == LyricsSources.Lrclib, "back to auto");
            w.Close();
        });
    }

    [Fact]
    public void F04_E1_editor_rejects_broken_ttml_and_accepts_plain_text()
    {
        Sta.Run(async () =>
        {
            var d = new LyricsEditorDialog(new LyricClone.Core.Lyrics.LyricsParser(), "测试", "", null) { ShowActivated = false, Left = -10_000 };
            d.Show();
            var content = Sta.Find<TextBox>(d, "ContentBox");
            var save = Sta.Find<Button>(d, "SaveButton");
            var status = Sta.Find<TextBlock>(d, "StatusText");
            Assert.False(save.IsEnabled, $"empty: {status.Text}");

            content.Text = "<tt><body><p begin=\"0.5s\">未闭合";
            Assert.False(save.IsEnabled, $"broken ttml: {status.Text}");
            Assert.StartsWith("无法解析", status.Text);

            content.Text = "只有文字的一行\n第二行，没有时间";
            Assert.True(save.IsEnabled);
            Assert.Contains("无时间轴", status.Text);

            content.Text = new string('长', 5000) + "\n[00:01.00]😀 émoji 行";
            Assert.True(save.IsEnabled);

            Sta.Find<TextBox>(d, "NameBox").Text = "   ";
            Assert.False(save.IsEnabled);
            d.Close();
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void F06_H1_offset_saved_from_preview_shifts_playing_lyrics()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        Sta.Run(async () =>
        {
            var np = (await PlayingAsync(env)).Np;
            var (w, vm) = await OpenAsync(env, allTracks: false);
            vm.SelectedSource = Source(vm, "LRCLIB");

            var dialog = WhenDialog<OffsetDialog>(async d =>
            {
                Assert.True(Sta.Find<RadioButton>(d, "FollowRadio").IsChecked);
                Sta.Find<TextBox>(d, "OffsetBox").Text = "500";
                Press(ButtonWithText(d, "+50"));
                await Sta.Until(() => d.OffsetMs == 550, "step applied");
                Press(ButtonWithText(d, "保存"));
            });
            Sta.Click(w, "PreviewButton");
            await dialog;

            await Sta.Until(() => np.Lyrics.Lines.FirstOrDefault()?.StartMs == 1550, "offset applied to playing lyrics");
            await Sta.Until(() => vm.Sources.Any(s => s.Labels.Any(l => l.Text == "偏移 +550 ms")), "offset label");
            w.Close();
        });
    }

    [Fact]
    public void F06_E1_not_playing_track_uses_simulated_playback()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        Sta.Run(async () =>
        {
            var np = (await PlayingAsync(env)).Np;
            env.Sessions.Stop();
            await Sta.Until(() => np.Resolved.Track is null, "stopped");
            var (w, vm) = await OpenAsync(env, allTracks: true);
            await Sta.Until(() => vm.Sources.Any(s => s.Version is not null), "sources");
            vm.SelectedSource = Source(vm, "LRCLIB");

            var dialog = WhenDialog<OffsetDialog>(async d =>
            {
                Assert.False(Sta.Find<RadioButton>(d, "FollowRadio").IsEnabled,
                    $"playing track: {env.NowPlaying!.Resolved.Track?.Id ?? "none"}, snapshot: {env.NowPlaying.Snapshot?.Title ?? "none"}, selected: {vm.SelectedTrack?.Id}, " +
                    $"tooltip: {Sta.Find<RadioButton>(d, "FollowRadio").ToolTip ?? "null"}, follow checked: {Sta.Find<RadioButton>(d, "FollowRadio").IsChecked}, " +
                    $"dialogs: {Application.Current.Windows.OfType<OffsetDialog>().Count()}");
                Assert.True(Sta.Find<RadioButton>(d, "SimRadio").IsChecked);
                Press(ButtonWithText(d, "取消"));
                await Task.CompletedTask;
            });
            Sta.Click(w, "PreviewButton");
            await dialog;
            w.Close();
        });
    }

    [Fact]
    public void F06_E2_playback_stops_while_preview_is_open_switches_to_simulation()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        Sta.Run(async () =>
        {
            await PlayingAsync(env);
            var (w, vm) = await OpenAsync(env, allTracks: false);
            vm.SelectedSource = Source(vm, "LRCLIB");

            var dialog = WhenDialog<OffsetDialog>(async d =>
            {
                var follow = Sta.Find<RadioButton>(d, "FollowRadio");
                Assert.True(follow.IsChecked);
                env.Sessions.Stop();
                await Sta.Until(() => !follow.IsEnabled && Sta.Find<RadioButton>(d, "SimRadio").IsChecked == true, "falls back to simulation");
                Press(ButtonWithText(d, "取消"));
            });
            Sta.Click(w, "PreviewButton");
            await dialog;
            w.Close();
        });
    }

    [Fact]
    public void F08_H1_all_tracks_search_filter_and_delete()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        env.Lrclib.Add(2, "Placeholder Song", "Nobody Real", Samples.LrcB);
        Sta.Run(async () =>
        {
            var np = (await PlayingAsync(env)).Np;
            env.Sessions.Play("demo.player", "Placeholder Song", "Nobody Real");
            await Sta.Until(() => np.Resolved.Track?.Title == "Placeholder Song" && np.Resolved.State == ResolveState.Found, "second track");
            env.Sessions.Play("demo.player", "还没有歌词", "测试艺人");
            await Sta.Until(() => np.Resolved.Track?.Title == "还没有歌词" && !np.IsResolving, "third track");

            var (w, vm) = await OpenAsync(env, allTracks: true);
            await Sta.Until(() => vm.Tracks.Count == 3, "three tracks");

            vm.Search = "placeholder";
            await Sta.Until(() => vm.Tracks.Count == 1, "search narrows");
            vm.Search = "";
            await Sta.Until(() => vm.Tracks.Count == 3, "search cleared");

            vm.FilterIndex = 4;
            await Sta.Until(() => vm.Tracks.Count == 2, "only tracks with LRCLIB saved");
            vm.FilterIndex = 1;
            await Sta.Until(() => vm.Tracks.Count == 0 && vm.EmptyMessage == "没有符合条件的曲目。", "empty filter message");
            vm.FilterIndex = 0;
            await Sta.Until(() => vm.Tracks.Count == 3, "filter cleared");

            vm.SortIndex = 2;
            await Sta.Until(() => vm.Tracks[0].Title == "Placeholder Song", "sorted by title");

            vm.SelectedTrack = vm.Tracks.First(t => t.Title == "Placeholder Song");
            await Sta.Until(() => Source(vm, "LRCLIB").Version is not null, "sources for selected");
            vm.SelectedSource = Source(vm, "LRCLIB");
            var confirm = WhenWindowTitled("删除此版本？", async d =>
            {
                Press(ButtonWithText(d, "删除"));
                await Task.CompletedTask;
            });
            Menu(Sta.Find<Button>(w, "MoreButton"), "删除此版本");
            await confirm;
            await Sta.Until(() => Source(vm, "LRCLIB").Version is null, "version deleted");
            Assert.Equal("获取", Sta.Find<Button>(w, "PrimaryButton").Content);
            w.Close();
        });
    }

    [Fact]
    public void F08_E1_new_track_appears_after_refresh()
    {
        using var env = new FlowEnv();
        env.Lrclib.Add(1, "示例曲目", "虚构乐队", Samples.LrcA);
        env.Lrclib.Add(2, "后来播放的", "虚构乐队", Samples.LrcB);
        Sta.Run(async () =>
        {
            var np = (await PlayingAsync(env)).Np;
            var (w, vm) = await OpenAsync(env, allTracks: true);
            await Sta.Until(() => vm.Tracks.Count == 1, "one track");

            env.Sessions.Play("demo.player", "后来播放的", "虚构乐队");
            await Sta.Until(() => np.Resolved.Track?.Title == "后来播放的", "new track resolved");
            Press(ButtonWithText(w, "刷新曲目库"));
            await Sta.Until(() => vm.Tracks.Count == 2, "refresh shows new track");
            w.Close();
        });
    }

    [Fact]
    public void F08_E2_fetch_for_track_with_no_lyrics_shows_message()
    {
        using var env = new FlowEnv();
        Sta.Run(async () =>
        {
            var np = await env.StartAsync();
            env.Sessions.Play("demo.player", "还没有歌词", "测试艺人");
            await Sta.Until(() => np.Resolved.State == ResolveState.NotFound && !np.IsResolving, "not found");
            var (w, vm) = await OpenAsync(env, allTracks: false);
            await Sta.Until(() => vm.Sources.Count == 4, "player cache + lrclib rows");
            Assert.Equal("获取", Sta.Find<Button>(w, "PrimaryButton").Content);

            var info = WhenWindowTitled("没有找到歌词", async d =>
            {
                Press(ButtonWithText(d, "知道了"));
                await Task.CompletedTask;
            });
            Sta.Click(w, "PrimaryButton");
            await info;
            w.Close();
        });
    }
}
