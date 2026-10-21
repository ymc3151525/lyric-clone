using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using LyricClone.App.Interop;
using LyricClone.App.Services;
using LyricClone.App.Views;
using LyricClone.Core.Settings;
using LyricClone.Core.Lyrics;
using System.IO;
using LyricClone.Infrastructure.Data;
using LyricClone.Infrastructure.Lrclib;
using LyricClone.Infrastructure.Lyrics;
using LyricClone.Infrastructure.Settings;
using LyricClone.Infrastructure.Smtc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WpfApplication = System.Windows.Application;

namespace LyricClone.App;

public partial class App : WpfApplication
{
    public const string UserAgent = "lyric-clone/0.1.0 (personal build; contact: set in settings)";

    private IHost? _host;
    private TaskbarIcon? _tray;
    private NowPlayingService? _nowPlaying;
    private ISettingsStore? _settings;
    private DesktopLyricsWindow? _desktop;
    private IslandWindow? _island;
    private TaskbarLyricsWindow? _taskbarLyrics;
    private HotkeyService? _hotkeys;
    private readonly ITaskbarQuery _taskbarQuery = new TaskbarQuery();
    private readonly Dictionary<string, Window> _openWindows = [];
    private MenuItem? _desktopItem;
    private MenuItem? _lockItem;
    private MenuItem? _islandItem;
    private MenuItem? _taskbarItem;
    private MenuItem? _fullscreenItem;
    private MenuItem? _stageItem;
    private MenuItem? _manageItem;
    private MenuItem? _nowItem;
    private MenuItem? _allItem;
    private MenuItem? _settingsItem;
    private MenuItem? _exitItem;
    private Mutex? _singleInstance;
    private Autostart? _autostart;
    private string? _indexedMusicFolder;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var screenshotArg = Array.IndexOf(e.Args, "--gallery-screenshot");
        if (screenshotArg >= 0 && screenshotArg + 1 < e.Args.Length)
        {
            var gallery = new DesignGalleryWindow { ShowActivated = false, Left = -10_000 };
            gallery.Show();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            gallery.SaveScreenshot(e.Args[screenshotArg + 1]);
            Shutdown();
            return;
        }

        _singleInstance = new Mutex(true, "lyric-clone.single-instance", out bool first);
        if (!first)
        {
            MessageBox.Show("lyric-clone 已经在运行，可以在任务栏通知区域找到它。", "lyric-clone", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _host = Host.CreateDefaultBuilder(e.Args)
            .ConfigureLogging(l => l.ClearProviders().AddDebug().SetMinimumLevel(LogLevel.Information))
            .ConfigureServices(ConfigureServices)
            .Build();
        await _host.StartAsync();

        _settings = _host.Services.GetRequiredService<ISettingsStore>();
        _nowPlaying = _host.Services.GetRequiredService<NowPlayingService>();
        Loc.Apply(_settings.Current.Language);
        _hotkeys = new HotkeyService();
        _hotkeys.Fired += OnHotkey;
        _hotkeys.Update(_settings.Current.Hotkeys);

#if DEBUG
        var windowShot = Array.IndexOf(e.Args, "--window-screenshot");
        if (windowShot >= 0 && windowShot + 2 < e.Args.Length)
        {
            await SaveWindowScreenshotAsync(e.Args[windowShot + 1], e.Args[windowShot + 2]);
            Shutdown();
            return;
        }
#endif

        try
        {
            _autostart = new Autostart();
            _autostart.RepairIfMoved();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException)
        {
            _autostart = null;
        }

        CreateTray();
        _settings.Changed += (_, _) => Dispatcher.InvokeAsync(SyncViewsWithSettings);
        SyncViewsWithSettings();

        await _nowPlaying.StartAsync(CancellationToken.None);
        if (_nowPlaying.Availability == SmtcAvailability.Unavailable)
        {
            _tray?.ShowNotification("lyric-clone", "这台电脑上的 Windows 媒体控制不可用，无法读取播放信息。");
        }

        bool startedWithWindows = e.Args.Contains(Autostart.Argument);
        var hints = new PlayerHintService(_nowPlaying, _settings, () => OpenSettingsPage("Apps"));
        if (!startedWithWindows || _nowPlaying.Availability == SmtcAvailability.Unavailable)
        {
            _ = hints.CheckAfterAsync(PlayerHintService.DefaultDelay);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _tray?.Dispose();
        if (_nowPlaying is not null)
        {
            await _nowPlaying.DisposeAsync();
        }

        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(2));
            _host.Dispose();
        }

        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(JsonSettingsStore.DefaultPath, sp.GetRequiredService<ILogger<JsonSettingsStore>>()));
        services.AddSingleton(_ => new LibraryDatabase(LibraryDatabase.DefaultPath));
        services.AddSingleton<ILibraryRepository, LibraryRepository>(sp => new LibraryRepository(sp.GetRequiredService<LibraryDatabase>()));
        services.AddHttpClient<ILrclibClient, LrclibClient>(http =>
        {
            http.BaseAddress = new Uri(LrclibClient.BaseAddress);
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        });
        services.AddSingleton<LocalFileProvider>();
        services.AddSingleton<PlayerCacheProvider>();
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsStore>();
            return new EmbeddedLyricsProvider(() => settings.Current.MusicFolder);
        });
        services.AddSingleton<ILyricsParser, HelperLyricsParser>();
        services.AddSingleton<ILyricsResolver, LyricsResolver>();
        services.AddSingleton<ISessionWatcher, SmtcSessionWatcher>();
        services.AddSingleton<ITaskbarQuery, TaskbarQuery>();
        services.AddSingleton<NowPlayingService>();
    }

    private void CreateTray()
    {
        var menu = new ContextMenu();

        _desktopItem = Toggle("", async on => await _settings!.UpdateAsync(s => s.Desktop.Enabled = on));
        _lockItem = Toggle("", async on => await _settings!.UpdateAsync(s => s.Desktop.Locked = on));
        menu.Items.Add(_desktopItem);
        menu.Items.Add(_lockItem);
        _islandItem = Toggle("", async on => await _settings!.UpdateAsync(s => s.Island.Enabled = on));
        menu.Items.Add(_islandItem);
        _taskbarItem = Toggle("", async on => await _settings!.UpdateAsync(s => s.Taskbar.Enabled = on));
        menu.Items.Add(_taskbarItem);
        menu.Items.Add(new Separator());
        _fullscreenItem = Item("", () => ShowWindow("fullscreen", () => new FullscreenLyricsWindow(_nowPlaying!, _settings!)));
        _stageItem = Item("", ToggleStage);
        menu.Items.Add(_fullscreenItem);
        menu.Items.Add(_stageItem);
        menu.Items.Add(new Separator());

        _manageItem = new MenuItem();
        _nowItem = Item("", () => ShowWindow("now", () => new ManagerWindow(CreateManagerContext(), allTracks: false)));
        _allItem = Item("", OpenLibrary);
        _manageItem.Items.Add(_nowItem);
        _manageItem.Items.Add(_allItem);
        menu.Items.Add(_manageItem);
        _settingsItem = Item("", OpenSettings);
        menu.Items.Add(_settingsItem);
#if DEBUG
        menu.Items.Add(Item("设计预览", () => ShowWindow("gallery", () => new DesignGalleryWindow())));
#endif
        menu.Items.Add(new Separator());
        _exitItem = Item("", Shutdown);
        menu.Items.Add(_exitItem);

        _tray = new TaskbarIcon
        {
            ToolTipText = "lyric-clone",
            Icon = CreateTrayIcon(),
            ContextMenu = menu,
        };
        _tray.ForceCreate(enablesEfficiencyMode: false);

        _nowPlaying!.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NowPlayingService.StatusText) && _tray is not null)
            {
                var text = _nowPlaying.StatusText;
                _tray.ToolTipText = text.Length > 120 ? text[..120] : text;
            }
        };
    }

    private void SyncViewsWithSettings()
    {
        var s = _settings!.Current;
        ThemeService.Apply(s.Theme);
        ApplyTrayCopy();
        WarmUpMusicIndex(s.MusicFolder);
        if (_desktopItem is not null)
        {
            _desktopItem.IsChecked = s.Desktop.Enabled;
        }

        if (_lockItem is not null)
        {
            _lockItem.IsChecked = s.Desktop.Locked;
            _lockItem.IsEnabled = s.Desktop.Enabled;
        }

        if (s.Desktop.Enabled && _desktop is null)
        {
            _desktop = new DesktopLyricsWindow(_nowPlaying!, _settings, OpenSettings);
            _desktop.Closed += (_, _) => _desktop = null;
            _desktop.Show();
        }
        else if (!s.Desktop.Enabled && _desktop is not null)
        {
            _desktop.Close();
        }

        if (_islandItem is not null)
        {
            _islandItem.IsChecked = s.Island.Enabled;
        }

        if (s.Island.Enabled && _island is null)
        {
            _island = new IslandWindow(_nowPlaying!, _settings);
            _island.Closed += (_, _) => _island = null;
            _island.Show();
        }
        else if (!s.Island.Enabled && _island is not null)
        {
            _island.Close();
        }

        if (_taskbarItem is not null)
        {
            _taskbarItem.IsChecked = s.Taskbar.Enabled;
        }

        if (s.Taskbar.Enabled && _taskbarLyrics is null)
        {
            _taskbarLyrics = new TaskbarLyricsWindow(_nowPlaying!, _settings, _taskbarQuery);
            _taskbarLyrics.Closed += (_, _) => _taskbarLyrics = null;
            _taskbarLyrics.Show();
        }
        else if (!s.Taskbar.Enabled && _taskbarLyrics is not null)
        {
            _taskbarLyrics.Close();
        }

        _hotkeys?.Update(s.Hotkeys);
    }

    private void OnHotkey(HotkeyAction action)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            switch (action)
            {
                case HotkeyAction.ToggleDesktop:
                    await _settings!.UpdateAsync(s => s.Desktop.Enabled = !s.Desktop.Enabled);
                    break;
                case HotkeyAction.ToggleLock:
                    await _settings!.UpdateAsync(s => s.Desktop.Locked = !s.Desktop.Locked);
                    break;
                case HotkeyAction.ToggleIsland:
                    await _settings!.UpdateAsync(s => s.Island.Enabled = !s.Island.Enabled);
                    break;
                case HotkeyAction.ToggleTaskbar:
                    await _settings!.UpdateAsync(s => s.Taskbar.Enabled = !s.Taskbar.Enabled);
                    break;
                case HotkeyAction.ToggleFullscreen:
                    ShowWindow("fullscreen", () => new FullscreenLyricsWindow(_nowPlaying!, _settings!));
                    break;
                case HotkeyAction.ToggleStage:
                    ToggleStage();
                    break;
            }
        });
    }

#if DEBUG
    /// <summary>Renders one window against a seeded demo library. Run with LYRIC_CLONE_DATA_DIR pointing at a temp folder.</summary>
    private async Task SaveWindowScreenshotAsync(string key, string path)
    {
        var ctx = CreateManagerContext();
        await Screenshots.SeedDemoAsync(ctx.Repository, ctx.Parser);
        var tracks = await ctx.Repository.QueryTracksAsync(new TrackQuery());
        var first = tracks.First(t => t.Title == "示例曲目一");
        var versions = await ctx.Repository.GetVersionsAsync(first.Id);
        if (key == "settings-apps")
        {
            await _settings!.UpdateAsync(s =>
            {
                var p = s.ProfileFor("demo.player");
                p.DisplayName = "示例播放器";
                p.SessionDelayMs = 120;
                p.SourceOrder = [Core.Library.LyricsSources.Lrclib, Core.Library.LyricsSources.LocalFile];
            });
        }

        Window window = key switch
        {
            "all" => new ManagerWindow(ctx, allTracks: true),
            "now" => new ManagerWindow(ctx, allTracks: false),
            "search" => new SearchDialog(ctx, first),
            "editor" => new LyricsEditorDialog(ctx.Parser, "我的版本", versions.First(v => v.Kind == Core.Library.VersionKind.Local).Content, null),
            "offset" => new OffsetDialog(ctx, first, versions.First(v => v.Kind == Core.Library.VersionKind.Online)),
            "byid" => new ByIdDialog(ctx, first),
            "island" => new IslandWindow(_nowPlaying!, _settings!),
            "taskbar" => new TaskbarLyricsWindow(_nowPlaying!, _settings!, _taskbarQuery),
            "fullscreen" => new FullscreenLyricsWindow(_nowPlaying!, _settings!) { WindowState = WindowState.Normal, Width = 1440, Height = 900 },
            "stage" => new StageWindow(_nowPlaying!, _settings!) { WindowState = WindowState.Normal, Width = 1440, Height = 900 },
            "hint" => new PlayerHintDialog(Core.Playback.PlayerHintKind.AllIgnored, ["Chrome", "示例播放器"]),
            "settings" or "settings-views" or "settings-apps" or "settings-about" or "settings-dark" or "settings-hotkeys" or "settings-support" => OpenSettingsFor(key),
            _ => throw new ArgumentException($"Unknown window '{key}'"),
        };
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10_000;
        window.Top = 0;
        window.Show();
        await Task.Delay(key == "search" ? 4000 : 1500);
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Screenshots.Save(window, path, key == "island" ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x50, 0x55, 0x60)) : null);
        window.Close();
    }

    private SettingsWindow OpenSettingsFor(string key)
    {
        if (key == "settings-dark")
        {
            ThemeService.Apply(Core.Settings.ThemeMode.Dark);
        }

        var w = CreateSettingsWindow();
        w.ShowPage(key switch
        {
            "settings-views" => "Views",
            "settings-apps" => "Apps",
            "settings-about" => "About",
            "settings-hotkeys" => "Hotkeys",
            "settings-support" => "Support",
            _ => "General",
        });
        return w;
    }
#endif

    private ManagerContext CreateManagerContext()
    {
        var sp = _host!.Services;
        return new ManagerContext(
            sp.GetRequiredService<ILibraryRepository>(),
            sp.GetRequiredService<ILyricsResolver>(),
            sp.GetRequiredService<ILyricsParser>(),
            sp.GetRequiredService<ILrclibClient>(),
            _settings!,
            _nowPlaying!);
    }

    /// <summary>Builds the embedded-lyrics index in the background at start-up and whenever the music folder changes.</summary>
    private void WarmUpMusicIndex(string? folder)
    {
        var embedded = _host?.Services.GetRequiredService<EmbeddedLyricsProvider>();
        var effective = folder ?? EmbeddedLyricsProvider.DefaultFolder;
        if (embedded is null || effective == _indexedMusicFolder)
        {
            return;
        }

        _indexedMusicFolder = effective;
        embedded.Invalidate();
        _ = Task.Run(async () =>
        {
            try
            {
                await embedded.WarmUpAsync(CancellationToken.None);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        });
    }

    private SettingsWindow CreateSettingsWindow() =>
        new(_settings!, _host!.Services.GetRequiredService<ISessionWatcher>(), OpenLibrary, UserAgent,
            _autostart, _host.Services.GetRequiredService<EmbeddedLyricsProvider>());

    private void OpenSettings() => ShowWindow("settings", CreateSettingsWindow);

    private void OpenSettingsPage(string tag)
    {
        OpenSettings();
        if (_openWindows.TryGetValue("settings", out var w) && w is SettingsWindow settings)
        {
            settings.ShowPage(tag);
        }
    }

    private void OpenLibrary() => ShowWindow("all", () => new ManagerWindow(CreateManagerContext(), allTracks: true));

    private void ToggleStage()
    {
        if (_openWindows.TryGetValue("stage", out var existing))
        {
            existing.Close();
            return;
        }

        ShowWindow("stage", () => new StageWindow(_nowPlaying!, _settings!));
    }

    private void ApplyTrayCopy()
    {
        if (_desktopItem is null)
        {
            return;
        }

        _desktopItem.Header = Loc.T("桌面歌词", "Desktop lyrics");
        _lockItem!.Header = Loc.T("锁定桌面歌词", "Lock desktop lyrics");
        _islandItem!.Header = Loc.T("灵动词岛", "Lyrics island");
        _taskbarItem!.Header = Loc.T("任务栏歌词", "Taskbar lyrics");
        if (_fullscreenItem is not null)
        {
            _fullscreenItem.Header = Loc.T("全屏歌词", "Fullscreen lyrics");
        }

        if (_stageItem is not null)
        {
            _stageItem.Header = Loc.T("歌词舞台", "Lyrics stage");
        }

        if (_manageItem is not null)
        {
            _manageItem.Header = Loc.T("曲目与歌词", "Tracks & lyrics");
        }

        if (_nowItem is not null)
        {
            _nowItem.Header = Loc.T("正在播放", "Now playing");
        }

        if (_allItem is not null)
        {
            _allItem.Header = Loc.T("全部曲目", "All tracks");
        }

        if (_settingsItem is not null)
        {
            _settingsItem.Header = Loc.T("设置", "Settings");
        }

        if (_exitItem is not null)
        {
            _exitItem.Header = Loc.T("退出", "Exit");
        }
    }

    private void ShowWindow(string key, Func<Window> create)
    {
        if (_openWindows.TryGetValue(key, out var existing))
        {
            existing.Activate();
            return;
        }

        var w = create();
        _openWindows[key] = w;
        w.Closed += (_, _) => _openWindows.Remove(key);
        w.Show();
        w.Activate();
    }

    private static MenuItem Item(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static MenuItem Disabled(string header) => new() { Header = header, IsEnabled = false };

    private static MenuItem Toggle(string header, Action<bool> onChanged)
    {
        var item = new MenuItem { Header = header, IsCheckable = true };
        item.Click += (_, _) => onChanged(item.IsChecked);
        return item;
    }

    /// <summary>Placeholder tray glyph drawn at runtime: a rounded square with two bars. Replaced in /replica-brand.</summary>
    private static Icon CreateTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            using var path = new GraphicsPath();
            const int r = 8;
            path.AddArc(1, 1, r * 2, r * 2, 180, 90);
            path.AddArc(31 - r * 2, 1, r * 2, r * 2, 270, 90);
            path.AddArc(31 - r * 2, 31 - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(1, 31 - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            using var bg = new SolidBrush(System.Drawing.Color.FromArgb(0x3B, 0x5B, 0xDB));
            g.FillPath(bg, path);
            using var fg = new SolidBrush(System.Drawing.Color.White);
            g.FillRectangle(fg, 7, 11, 18, 4);
            g.FillRectangle(fg, 7, 18, 12, 4);
        }

        var handle = bmp.GetHicon();
        return (Icon)Icon.FromHandle(handle).Clone();
    }
}
