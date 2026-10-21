using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using LyricClone.App.Lyrics;
using LyricClone.App.Services;
using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;
using LyricClone.Core.Update;
using LyricClone.Infrastructure.Lyrics;
using LyricClone.Core.Settings;
using ThemeMode = LyricClone.Core.Settings.ThemeMode;
using LyricClone.Infrastructure;
using LyricClone.Infrastructure.Settings;
using LyricClone.Infrastructure.Smtc;
using Microsoft.Win32;

namespace LyricClone.App.Views;

/// <summary>S07 settings and S08 per-app configuration.</summary>
public partial class SettingsWindow : Window
{
    private const long MaxDelayMs = 10_000;

    private readonly ISettingsStore _settings;
    private readonly ISessionWatcher _sessions;
    private readonly Action _openLibrary;
    private readonly Autostart? _autostart;
    private readonly EmbeddedLyricsProvider? _embedded;
    private readonly string _userAgent;
    private readonly List<(ColorField Field, Func<AppSettings, ViewColors> Of, Action<ViewColors, string?> Set, Func<ViewColors, string?> Get)> _colorFields = [];
    private bool _loading;
    private string? _selectedAppId;
    private string? _recordingHotkey;

    public SettingsWindow(ISettingsStore settings, ISessionWatcher sessions, Action openLibrary, string userAgent,
        Autostart? autostart = null, EmbeddedLyricsProvider? embedded = null)
    {
        InitializeComponent();
        _settings = settings;
        _sessions = sessions;
        _openLibrary = openLibrary;
        _autostart = autostart;
        _embedded = embedded;
        _userAgent = userAgent;
        NavColumn.Width = new GridLength((double)FindResource("Layout.Settings.NavWidth"));
        Title = Loc.T("设置 — lyric-clone", "Settings — lyric-clone");

        var fonts = Fonts.SystemFontFamilies.Select(f => f.Source).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        fonts.Insert(0, DefaultFontLabel);
        DesktopFontBox.ItemsSource = fonts;
        IslandFontBox.ItemsSource = fonts;
        FullscreenFontBox.ItemsSource = fonts;
        TaskbarFontBox.ItemsSource = fonts;
        StageFontBox.ItemsSource = fonts;

        AddColorRows(DesktopColorsPanel, s => s.Desktop.Colors, outline: true);
        AddColorRows(IslandColorsPanel, s => s.Island.Colors, outline: false);
        AddColorRows(FullscreenColorsPanel, s => s.Fullscreen.Colors, outline: false);
        AddColorRows(TaskbarColorsPanel, s => s.Taskbar.Colors, outline: false);
        AddColorRows(StageColorsPanel, s => s.Stage.Colors, outline: false);
        AutostartBox.IsEnabled = autostart is not null;
        ScanMusicButton.IsEnabled = embedded is not null;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"lyric-clone  {Loc.T("版本", "version")} {version?.ToString(3)}";
        UserAgentText.Text = userAgent;
        LicenseList.ItemsSource = new[]
        {
            new { Name = "CommunityToolkit.Mvvm", License = "MIT" },
            new { Name = "H.NotifyIcon", License = "MIT" },
            new { Name = "Dapper", License = "Apache-2.0" },
            new { Name = "Lyricify.Lyrics.Helper", License = "Apache-2.0" },
            new { Name = "Microsoft.Data.Sqlite", License = "MIT" },
            new { Name = "Microsoft.Extensions.*", License = "MIT" },
            new { Name = "SQLite", License = Loc.T("公有领域 (Public Domain)", "Public Domain") },
        };

        LoadAll(settings.Current);
        PreviewKeyDown += OnHotkeyKey;
        settings.Changed += OnSettingsChanged;
        Closed += (_, _) => settings.Changed -= OnSettingsChanged;
    }

    private const string DefaultFontLabel = "（默认字体）";

    private static readonly LyricLine PreviewCurrentLine = new(0, 4_000, "You and me", [], "你和我");
    private static readonly LyricLine PreviewNextLine = new(4_000, 8_000, "under the rain", [], "雨下");

    private sealed record SourceRow(string Id, string Label);

    private sealed record AppRow(string Id, string Name, string Detail);

    public void ShowPage(string tag)
    {
        foreach (ListBoxItem item in Nav.Items)
        {
            if ((string)item.Tag == tag)
            {
                Nav.SelectedItem = item;
            }
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings s) =>
        Dispatcher.InvokeAsync(() =>
        {
            if (!IsKeyboardFocusWithin || !(Keyboard.FocusedElement is TextBox or ComboBox))
            {
                LoadAll(s);
            }
        });

    private void LoadAll(AppSettings s)
    {
        _loading = true;
        try
        {
            ThemeBox.SelectedIndex = (int)s.Theme;
            LanguageBox.SelectedIndex = (int)s.Language;
            DeviceDelayBox.Text = s.DeviceDelayMs.ToString(CultureInfo.InvariantCulture);
            SmoothingBox.IsChecked = s.TimelineSmoothing;
            BrowsersBox.IsChecked = s.IncludeBrowserSessions;
            NoPlayerHintBox.IsChecked = s.ShowNoPlayerHint;
            AutostartBox.IsChecked = _autostart?.IsEnabled == true;

            AutoUpdateBox.IsChecked = s.AutoUpdateSavedOnline;
            AutoManageBox.IsChecked = s.AutoManageLibrary;
            VersionPickBox.SelectedIndex = (int)s.VersionPick;
            ChineseTranslationBox.IsChecked = s.ShowChineseTranslation;
            RomajiBox.SelectedIndex = (int)s.Romaji;
            SimplifiedBox.IsChecked = s.PreferSimplifiedChinese;
            SplitLinesBox.IsChecked = s.SplitLongLines;
            ExplicitBox.IsChecked = s.ShowExplicitLyrics;
            FillOrder(SourceOrderList, LyricsSources.WithKnownDefaults(s.SourceOrder.Count > 0 ? s.SourceOrder : LyricsSources.DefaultOrder));
            FolderBox.Text = s.LocalLyricsFolder ?? Loc.T("未设置", "Not set");
            MusicFolderBox.Text = s.MusicFolder ?? $"{EmbeddedLyricsProvider.DefaultFolder}{Loc.T("（默认）", " (default)")}";

            var d = s.Desktop;
            DesktopEnabledBox.IsChecked = d.Enabled;
            DesktopLockedBox.IsChecked = d.Locked;
            DesktopFontBox.Text = d.FontFamily ?? DefaultFontLabel;
            DesktopSizeSlider.Value = d.FontSize;
            DesktopSizeText.Text = $"{d.FontSize:0}";
            DesktopAlignBox.SelectedIndex = (int)d.Alignment;
            DesktopTwoLinesBox.IsChecked = d.TwoLines;
            DesktopTranslationBox.IsChecked = d.ShowTranslation;
            DesktopOutlineBox.IsChecked = d.Outline;

            var i = s.Island;
            IslandEnabledBox.IsChecked = i.Enabled;
            IslandFontBox.Text = i.FontFamily ?? DefaultFontLabel;
            IslandAutoHideBox.IsChecked = i.AutoHideWhenIdle;
            IslandTranslationBox.IsChecked = i.ShowTranslation;
            IslandContinentBox.IsChecked = i.ContinentMode;
            IslandTwoLinesBox.IsChecked = i.AlwaysTwoLines;
            IslandBoldBox.IsChecked = i.Bold;
            IslandKaraokeBox.IsChecked = i.Karaoke;
            IslandHideFullscreenBox.IsChecked = i.HideOnFullscreen;
            IslandHideLyricsFsBox.IsChecked = i.HideOnLyricsFullscreen;
            IslandOpacitySlider.Value = i.Opacity;
            IslandOpacityText.Text = $"{i.Opacity:0%}";
            IslandBgOpacitySlider.Value = i.BackgroundOpacity;
            IslandBgOpacityText.Text = $"{i.BackgroundOpacity:0%}";
            IslandScaleSlider.Value = i.Scale;
            IslandScaleText.Text = $"{i.Scale:0%}";

            var f = s.Fullscreen;
            FullscreenFontBox.Text = f.FontFamily ?? DefaultFontLabel;
            FullscreenTranslationBox.IsChecked = f.ShowTranslation;
            FullscreenBlurBox.IsChecked = f.BlurredCover;

            var st = s.Stage;
            StageFontBox.Text = st.FontFamily ?? DefaultFontLabel;
            StageTranslationBox.IsChecked = st.ShowTranslation;

            var tb = s.Taskbar;
            TaskbarEnabledBox.IsChecked = tb.Enabled;
            TaskbarFontBox.Text = tb.FontFamily ?? DefaultFontLabel;
            TaskbarCoverBox.IsChecked = tb.ShowCover;
            TaskbarTranslationBox.IsChecked = tb.ShowTranslation;

            HotkeyDesktopBox.Text = s.Hotkeys.ToggleDesktop ?? "";
            HotkeyLockBox.Text = s.Hotkeys.ToggleLock ?? "";
            HotkeyIslandBox.Text = s.Hotkeys.ToggleIsland ?? "";
            HotkeyTaskbarBox.Text = s.Hotkeys.ToggleTaskbar ?? "";
            HotkeyFullscreenBox.Text = s.Hotkeys.ToggleFullscreen ?? "";
            HotkeyStageBox.Text = s.Hotkeys.ToggleStage ?? "";
            UpdateFeedBox.Text = s.UpdateFeedUrl ?? "";

            foreach (var (field, of, _, get) in _colorFields)
            {
                field.Value = get(of(s));
            }

            ApplyTranslationSwitchState(s);
            UpdateViewPreview(s);
            LoadApps(s);
        }
        finally
        {
            _loading = false;
        }
    }

    private static string SourceLabel(string id) => id switch
    {
        LyricsSources.QqCache => "QQ音乐缓存",
        LyricsSources.NeteaseCache => "网易云音乐缓存",
        LyricsSources.KugouCache => "酷狗音乐缓存",
        LyricsSources.LocalFile => "本地歌词文件夹",
        LyricsSources.Embedded => "音频文件内嵌",
        LyricsSources.Lrclib => "LRCLIB（在线）",
        _ => id,
    };

    private static void FillOrder(ListBox list, IEnumerable<string> order)
    {
        var selected = (list.SelectedItem as SourceRow)?.Id;
        var rows = order.Select(id => new SourceRow(id, SourceLabel(id))).ToList();
        list.ItemsSource = rows;
        list.SelectedItem = rows.FirstOrDefault(r => r.Id == selected);
    }

    private static List<string> MoveSelected(ListBox list, int delta)
    {
        var rows = ((IEnumerable<SourceRow>)list.ItemsSource).ToList();
        if (list.SelectedItem is SourceRow row)
        {
            int i = rows.IndexOf(row);
            int j = Math.Clamp(i + delta, 0, rows.Count - 1);
            rows.RemoveAt(i);
            rows.Insert(j, row);
        }

        return rows.Select(r => r.Id).ToList();
    }

    private void OnNav(object sender, SelectionChangedEventArgs e)
    {
        if (GeneralPage is null)
        {
            return;
        }

        var tag = (Nav.SelectedItem as ListBoxItem)?.Tag as string;
        GeneralPage.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
        LyricsPage.Visibility = tag == "Lyrics" ? Visibility.Visible : Visibility.Collapsed;
        ViewsPage.Visibility = tag == "Views" ? Visibility.Visible : Visibility.Collapsed;
        AppsPage.Visibility = tag == "Apps" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;
        if (HotkeysPage is not null)
        {
            HotkeysPage.Visibility = tag == "Hotkeys" ? Visibility.Visible : Visibility.Collapsed;
        }

        if (SupportPage is not null)
        {
            SupportPage.Visibility = tag == "Support" ? Visibility.Visible : Visibility.Collapsed;
        }
        Scroller.ScrollToTop();
        if (tag == "Apps")
        {
            _loading = true;
            LoadApps(_settings.Current);
            _loading = false;
        }
    }

    private Task Update(Action<AppSettings> edit) => _loading ? Task.CompletedTask : _settings.UpdateAsync(edit);

    private static bool TryParseDelay(string text, out long value)
    {
        var ok = long.TryParse(text.Trim().Replace('−', '-'), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        value = Math.Clamp(value, -MaxDelayMs, MaxDelayMs);
        return ok;
    }

    private void OnNumberKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }
    }

    // 常规

    private async void OnThemeChanged(object sender, SelectionChangedEventArgs e) =>
        await Update(s => s.Theme = (ThemeMode)Math.Max(0, ThemeBox.SelectedIndex));

    private async void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        var lang = (UiLanguage)Math.Max(0, LanguageBox.SelectedIndex);
        Loc.Apply(lang);
        await Update(s => s.Language = lang);
        Title = Loc.T("设置 — lyric-clone", "Settings — lyric-clone");
    }

    private async void OnAutoUpdateToggle(object sender, RoutedEventArgs e) =>
        await Update(s => s.AutoUpdateSavedOnline = AutoUpdateBox.IsChecked == true);

    private async void OnLyricsContentToggle(object sender, RoutedEventArgs e) =>
        await Update(s =>
        {
            s.AutoManageLibrary = AutoManageBox.IsChecked == true;
            s.ShowChineseTranslation = ChineseTranslationBox.IsChecked == true;
            s.PreferSimplifiedChinese = SimplifiedBox.IsChecked == true;
            s.SplitLongLines = SplitLinesBox.IsChecked == true;
            s.ShowExplicitLyrics = ExplicitBox.IsChecked == true;
        });

    private async void OnVersionPickChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        await Update(s => s.VersionPick = (VersionPickMode)Math.Max(0, VersionPickBox.SelectedIndex));
    }

    private async void OnRomajiChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        await Update(s => s.Romaji = (RomajiDisplay)Math.Max(0, RomajiBox.SelectedIndex));
    }

    private async void OnIslandSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || IslandScaleText is null)
        {
            return;
        }

        IslandOpacityText.Text = $"{IslandOpacitySlider.Value:0%}";
        IslandBgOpacityText.Text = $"{IslandBgOpacitySlider.Value:0%}";
        IslandScaleText.Text = $"{IslandScaleSlider.Value:0%}";
        await Update(s =>
        {
            s.Island.Opacity = IslandOpacitySlider.Value;
            s.Island.BackgroundOpacity = IslandBgOpacitySlider.Value;
            s.Island.Scale = IslandScaleSlider.Value;
        });
    }

    private async void OnDeviceDelayCommit(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (TryParseDelay(DeviceDelayBox.Text, out var v))
        {
            await Update(s => s.DeviceDelayMs = v);
        }

        DeviceDelayBox.Text = _settings.Current.DeviceDelayMs.ToString(CultureInfo.InvariantCulture);
    }

    private async void OnGeneralToggle(object sender, RoutedEventArgs e) =>
        await Update(s =>
        {
            s.TimelineSmoothing = SmoothingBox.IsChecked == true;
            s.IncludeBrowserSessions = BrowsersBox.IsChecked == true;
            s.ShowNoPlayerHint = NoPlayerHintBox.IsChecked == true;
        });

    private void OnAutostartToggle(object sender, RoutedEventArgs e)
    {
        if (_autostart is null)
        {
            return;
        }

        try
        {
            _autostart.SetEnabled(AutostartBox.IsChecked == true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Dialogs.Info(this, "无法修改开机启动", ex.Message);
        }

        AutostartBox.IsChecked = _autostart.IsEnabled;
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "导出设置", FileName = "lyric-clone-settings.json", Filter = "JSON (*.json)|*.json" };
        if (dialog.ShowDialog(this) == true)
        {
            await _settings.ExportAsync(dialog.FileName);
        }
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "导入设置", Filter = "JSON (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await _settings.ImportAsync(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            Dialogs.Info(this, "无法导入", $"这个文件不是有效的设置文件：{ex.Message}");
        }
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(DataPaths.Root);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DataPaths.Root}\"") { UseShellExecute = true });
    }

    // 歌词

    private async void OnSourceUp(object sender, RoutedEventArgs e) => await SaveGlobalOrder(MoveSelected(SourceOrderList, -1));

    private async void OnSourceDown(object sender, RoutedEventArgs e) => await SaveGlobalOrder(MoveSelected(SourceOrderList, 1));

    private async Task SaveGlobalOrder(List<string> order)
    {
        FillOrder(SourceOrderList, order);
        await Update(s => s.SourceOrder = order);
    }

    private async void OnPickFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择本地歌词文件夹" };
        if (_settings.Current.LocalLyricsFolder is { } current && Directory.Exists(current))
        {
            dialog.InitialDirectory = current;
        }

        if (dialog.ShowDialog(this) == true)
        {
            await Update(s => s.LocalLyricsFolder = dialog.FolderName);
        }
    }

    private async void OnClearFolder(object sender, RoutedEventArgs e) => await Update(s => s.LocalLyricsFolder = null);

    private async void OnPickMusicFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择音乐文件夹" };
        var current = _settings.Current.MusicFolder ?? EmbeddedLyricsProvider.DefaultFolder;
        if (Directory.Exists(current))
        {
            dialog.InitialDirectory = current;
        }

        if (dialog.ShowDialog(this) == true)
        {
            await Update(s => s.MusicFolder = dialog.FolderName);
            await ScanMusicAsync();
        }
    }

    private async void OnClearMusicFolder(object sender, RoutedEventArgs e)
    {
        await Update(s => s.MusicFolder = null);
        await ScanMusicAsync();
    }

    private async void OnScanMusic(object sender, RoutedEventArgs e) => await ScanMusicAsync();

    private async Task ScanMusicAsync()
    {
        if (_embedded is null)
        {
            return;
        }

        ScanMusicButton.IsEnabled = false;
        MusicScanText.Text = "正在扫描…";
        try
        {
            _embedded.Invalidate();
            await _embedded.WarmUpAsync(CancellationToken.None);
            MusicScanText.Text = _embedded.IndexedCount == 0 ? "没有找到带歌词的音频文件" : $"找到 {_embedded.IndexedCount} 个带歌词的音频文件";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MusicScanText.Text = $"扫描失败：{ex.Message}";
        }
        finally
        {
            ScanMusicButton.IsEnabled = true;
        }
    }

    private void OnOpenLibrary(object sender, RoutedEventArgs e) => _openLibrary();

    private void ApplyTranslationSwitchState(AppSettings s)
    {
        bool on = s.ShowChineseTranslation;
        DesktopTranslationBox.IsEnabled = on;
        IslandTranslationBox.IsEnabled = on;
        FullscreenTranslationBox.IsEnabled = on;
        StageTranslationBox.IsEnabled = on;
        TaskbarTranslationBox.IsEnabled = on;
        if (ViewTranslationHint is not null)
        {
            ViewTranslationHint.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void UpdateViewPreview(AppSettings s)
    {
        if (ViewPreviewCurrent is null)
        {
            return;
        }

        var d = s.Desktop;
        var family = string.IsNullOrWhiteSpace(d.FontFamily) ? (FontFamily)FindResource("Font.Sans") : new FontFamily(d.FontFamily);
        bool showTr = s.ShowsTranslation(d.ShowTranslation);
        double outline = d.Outline ? (double)FindResource("Lyric.OutlineThickness") : 0;
        var align = d.Alignment switch
        {
            LyricAlignment.Left => TextAlignment.Left,
            LyricAlignment.Right => TextAlignment.Right,
            _ => TextAlignment.Center,
        };

        ViewPreviewCurrent.FontFamily = family;
        ViewPreviewNext.FontFamily = family;
        ViewPreviewCurrent.FontSize = d.FontSize;
        ViewPreviewNext.FontSize = d.FontSize * 0.75;
        ViewPreviewCurrent.ShowTranslation = showTr;
        ViewPreviewNext.ShowTranslation = showTr;
        ViewPreviewCurrent.OutlineThickness = outline;
        ViewPreviewNext.OutlineThickness = outline;
        ViewPreviewCurrent.TextAlignment = align;
        ViewPreviewNext.TextAlignment = align;
        ViewPreviewCurrent.Line = PreviewCurrentLine;
        ViewPreviewNext.Line = PreviewNextLine;
        ViewPreviewNext.Visibility = d.TwoLines ? Visibility.Visible : Visibility.Collapsed;
        LyricColors.Apply(ViewPreviewCurrent, d.Colors);
        LyricColors.Apply(ViewPreviewNext, d.Colors);
    }

    // 视图

    private async void OnViewsToggle(object sender, RoutedEventArgs e) =>
        await Update(s =>
        {
            s.Desktop.Enabled = DesktopEnabledBox.IsChecked == true;
            s.Desktop.Locked = DesktopLockedBox.IsChecked == true;
            s.Desktop.TwoLines = DesktopTwoLinesBox.IsChecked == true;
            s.Desktop.ShowTranslation = DesktopTranslationBox.IsChecked == true;
            s.Desktop.Outline = DesktopOutlineBox.IsChecked == true;
            s.Island.Enabled = IslandEnabledBox.IsChecked == true;
            s.Island.AutoHideWhenIdle = IslandAutoHideBox.IsChecked == true;
            s.Island.ShowTranslation = IslandTranslationBox.IsChecked == true;
            s.Island.ContinentMode = IslandContinentBox.IsChecked == true;
            s.Island.AlwaysTwoLines = IslandTwoLinesBox.IsChecked == true;
            s.Island.Bold = IslandBoldBox.IsChecked == true;
            s.Island.Karaoke = IslandKaraokeBox.IsChecked == true;
            s.Island.HideOnFullscreen = IslandHideFullscreenBox.IsChecked == true;
            s.Island.HideOnLyricsFullscreen = IslandHideLyricsFsBox.IsChecked == true;
            s.Fullscreen.ShowTranslation = FullscreenTranslationBox.IsChecked == true;
            s.Fullscreen.BlurredCover = FullscreenBlurBox.IsChecked == true;
            s.Taskbar.Enabled = TaskbarEnabledBox.IsChecked == true;
            s.Taskbar.ShowCover = TaskbarCoverBox.IsChecked == true;
            s.Taskbar.ShowTranslation = TaskbarTranslationBox.IsChecked == true;
            s.Stage.ShowTranslation = StageTranslationBox.IsChecked == true;
        });

    private void AddColorRows(Panel panel, Func<AppSettings, ViewColors> of, bool outline)
    {
        var roles = new List<(string Label, string Token, Action<ViewColors, string?> Set, Func<ViewColors, string?> Get)>
        {
            ("文字颜色", "Brush.Lyric.Text", (c, v) => c.Text = v, c => c.Text),
            ("已唱部分颜色", "Brush.Lyric.Sung", (c, v) => c.Sung = v, c => c.Sung),
            ("翻译颜色", "Brush.Lyric.Translation", (c, v) => c.Translation = v, c => c.Translation),
        };
        if (outline)
        {
            roles.Add(("描边颜色", "Brush.Lyric.Stroke", (c, v) => c.Outline = v, c => c.Outline));
        }

        foreach (var (label, token, set, get) in roles)
        {
            var field = new ColorField(label) { DefaultBrush = (Brush)FindResource(token) };
            field.Committed += async value => await Update(s => set(of(s), value));
            var row = new Grid { Style = (Style)FindResource("Row") };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("Text.Body"), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(field, 1);
            row.Children.Add(field);
            panel.Children.Add(row);
            _colorFields.Add((field, of, set, get));
        }
    }

    private async void OnSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DesktopSizeText is null)
        {
            return;
        }

        DesktopSizeText.Text = $"{e.NewValue:0}";
        await Update(s => s.Desktop.FontSize = e.NewValue);
    }

    private async void OnAlignChanged(object sender, SelectionChangedEventArgs e) =>
        await Update(s => s.Desktop.Alignment = (LyricAlignment)Math.Max(0, DesktopAlignBox.SelectedIndex));

    private async void OnFontCommit(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not ComboBox box)
        {
            return;
        }

        var text = (e is SelectionChangedEventArgs ? box.SelectedItem as string : box.Text)?.Trim();
        string? family = string.IsNullOrEmpty(text) || text == DefaultFontLabel ? null : text;
        if (family is not null && !Fonts.SystemFontFamilies.Any(f => string.Equals(f.Source, family, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (box == DesktopFontBox && family != _settings.Current.Desktop.FontFamily)
        {
            await Update(s => s.Desktop.FontFamily = family);
        }
        else if (box == IslandFontBox && family != _settings.Current.Island.FontFamily)
        {
            await Update(s => s.Island.FontFamily = family);
        }
        else if (box == FullscreenFontBox && family != _settings.Current.Fullscreen.FontFamily)
        {
            await Update(s => s.Fullscreen.FontFamily = family);
        }
        else if (box == TaskbarFontBox && family != _settings.Current.Taskbar.FontFamily)
        {
            await Update(s => s.Taskbar.FontFamily = family);
        }
        else if (box == StageFontBox && family != _settings.Current.Stage.FontFamily)
        {
            await Update(s => s.Stage.FontFamily = family);
        }
    }

    private async void OnResetDesktopPosition(object sender, RoutedEventArgs e) =>
        await Update(s =>
        {
            s.Desktop.Left = null;
            s.Desktop.Top = null;
        });

    // 按应用

    private void LoadApps(AppSettings s)
    {
        var ids = s.AppProfiles.Keys.Concat(_sessions.SessionAppIds).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var live = new HashSet<string>(_sessions.SessionAppIds, StringComparer.OrdinalIgnoreCase);
        var rows = ids
            .Select(id =>
            {
                s.AppProfiles.TryGetValue(id, out var p);
                bool enabled = p?.Enabled ?? (!AppSettings.LooksLikeBrowser(id) || s.IncludeBrowserSessions);
                var detail = (enabled ? "已启用" : "已忽略") + (live.Contains(id) ? " · 正在运行" : "");
                return new AppRow(id, p?.DisplayName is { Length: > 0 } n ? n : FriendlyName(id), detail);
            })
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        AppList.ItemsSource = rows;
        AppListEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AppList.SelectedItem = rows.FirstOrDefault(r => string.Equals(r.Id, _selectedAppId, StringComparison.OrdinalIgnoreCase)) ?? rows.FirstOrDefault();
        _selectedAppId = (AppList.SelectedItem as AppRow)?.Id;
        LoadAppDetail(s);
    }

    public static string FriendlyName(string appId)
    {
        var name = appId;
        var bang = name.IndexOf('!');
        if (bang > 0)
        {
            name = name[..bang];
        }

        var underscore = name.IndexOf('_');
        if (underscore > 0)
        {
            name = name[..underscore];
        }

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        var dot = name.LastIndexOf('.');
        return dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..] : name;
    }

    private void OnAppSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _selectedAppId = (AppList.SelectedItem as AppRow)?.Id;
        _loading = true;
        LoadAppDetail(_settings.Current);
        _loading = false;
    }

    private void LoadAppDetail(AppSettings s)
    {
        var row = AppList.SelectedItem as AppRow;
        AppDetail.IsEnabled = row is not null;
        AppDetail.Visibility = row is null ? Visibility.Hidden : Visibility.Visible;
        if (row is null)
        {
            return;
        }

        s.AppProfiles.TryGetValue(row.Id, out var p);
        AppNameText.Text = row.Name;
        AppIdText.Text = row.Id;
        AppEnabledBox.IsChecked = p?.Enabled ?? (!AppSettings.LooksLikeBrowser(row.Id) || s.IncludeBrowserSessions);
        AppDelayBox.Text = (p?.SessionDelayMs ?? 0).ToString(CultureInfo.InvariantCulture);
        AppSmoothingBox.SelectedIndex = p?.TimelineSmoothing switch { true => 1, false => 2, null => 0 };
        bool custom = p?.SourceOrder is { Count: > 0 };
        AppCustomOrderBox.IsChecked = custom;
        AppOrderPanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        FillOrder(AppOrderList, LyricsSources.WithKnownDefaults(custom ? p!.SourceOrder! : s.SourceOrder.Count > 0 ? s.SourceOrder : LyricsSources.DefaultOrder));
    }

    private Task UpdateApp(Action<AppProfile> edit)
    {
        if (_selectedAppId is not { } id)
        {
            return Task.CompletedTask;
        }

        return Update(s =>
        {
            var p = s.ProfileFor(id);
            if (string.IsNullOrEmpty(p.DisplayName) || p.DisplayName == id)
            {
                p.DisplayName = FriendlyName(id);
            }

            edit(p);
        });
    }

    private async void OnAppEdit(object sender, RoutedEventArgs e)
    {
        if (_loading || AppDetail is null)
        {
            return;
        }

        bool custom = AppCustomOrderBox.IsChecked == true;
        var order = ((IEnumerable<SourceRow>)AppOrderList.ItemsSource).Select(r => r.Id).ToList();
        AppOrderPanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        await UpdateApp(p =>
        {
            p.Enabled = AppEnabledBox.IsChecked == true;
            p.TimelineSmoothing = AppSmoothingBox.SelectedIndex switch { 1 => true, 2 => false, _ => null };
            p.SourceOrder = custom ? order : null;
        });
    }

    private async void OnAppDelayCommit(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (TryParseDelay(AppDelayBox.Text, out var v))
        {
            await UpdateApp(p => p.SessionDelayMs = v);
        }
    }

    private async void OnAppSourceUp(object sender, RoutedEventArgs e) => await SaveAppOrder(MoveSelected(AppOrderList, -1));

    private async void OnAppSourceDown(object sender, RoutedEventArgs e) => await SaveAppOrder(MoveSelected(AppOrderList, 1));

    private async Task SaveAppOrder(List<string> order)
    {
        FillOrder(AppOrderList, order);
        await UpdateApp(p => p.SourceOrder = order);
    }

    private async void OnAppReset(object sender, RoutedEventArgs e)
    {
        if (_selectedAppId is { } id)
        {
            await Update(s => s.AppProfiles.Remove(id));
        }
    }

    private async void OnRecordHotkey(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            _recordingHotkey = tag;
            ((Button)sender).Content = Loc.T("按下组合键…", "Press keys…");
        }

        await Task.CompletedTask;
    }

    private async void OnHotkeyKey(object sender, KeyEventArgs e)
    {
        if (_recordingHotkey is null)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
        {
            return;
        }

        e.Handled = true;
        if (key == Key.Escape)
        {
            _recordingHotkey = null;
            LoadAll(_settings.Current);
            return;
        }

        var chord = new HotkeyChord(
            (Keyboard.Modifiers & ModifierKeys.Control) != 0,
            (Keyboard.Modifiers & ModifierKeys.Alt) != 0,
            (Keyboard.Modifiers & ModifierKeys.Shift) != 0,
            (Keyboard.Modifiers & ModifierKeys.Windows) != 0,
            KeyInterop.VirtualKeyFromKey(key));
        if (!(chord.Ctrl || chord.Alt || chord.Win))
        {
            return;
        }

        var text = chord.ToString();
        var which = _recordingHotkey;
        _recordingHotkey = null;
        await Update(s => SetHotkey(s.Hotkeys, which, text));
    }

    private async void OnClearHotkeys(object sender, RoutedEventArgs e) =>
        await Update(s => s.Hotkeys = new HotkeysSettings());

    private static void SetHotkey(HotkeysSettings h, string which, string? value)
    {
        switch (which)
        {
            case "ToggleDesktop": h.ToggleDesktop = value; break;
            case "ToggleLock": h.ToggleLock = value; break;
            case "ToggleIsland": h.ToggleIsland = value; break;
            case "ToggleTaskbar": h.ToggleTaskbar = value; break;
            case "ToggleFullscreen": h.ToggleFullscreen = value; break;
            case "ToggleStage": h.ToggleStage = value; break;
        }
    }

    // 关于

    private async void OnUpdateFeedCommit(object sender, KeyboardFocusChangedEventArgs e)
    {
        var url = UpdateFeedBox.Text.Trim();
        await Update(s => s.UpdateFeedUrl = string.IsNullOrEmpty(url) ? null : url);
    }

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        var feed = (_settings.Current.UpdateFeedUrl ?? UpdateFeedBox.Text).Trim();
        if (string.IsNullOrEmpty(feed))
        {
            UpdateStatusText.Text = Loc.T(
                "这个个人构建没有发布更新源。需要的话可以在上面填写一个版本信息地址。",
                "This personal build has no update feed. You can paste a version JSON URL above.");
            return;
        }

        if (!Uri.TryCreate(feed, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            UpdateStatusText.Text = Loc.T("更新源不是有效的 http(s) 地址。", "The update feed is not a valid http(s) URL.");
            return;
        }

        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = Loc.T("正在检查…", "Checking…");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
            var json = await http.GetStringAsync(uri);
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);
            var result = UpdateFeed.Parse(json, current);
            UpdateStatusText.Text = result.Status switch
            {
                UpdateCheckStatus.Available => Loc.T(
                    $"有新版本 {result.Latest}。{(string.IsNullOrEmpty(result.Notes) ? "" : " " + result.Notes)}",
                    $"Version {result.Latest} is available.{(string.IsNullOrEmpty(result.Notes) ? "" : " " + result.Notes)}"),
                UpdateCheckStatus.UpToDate => Loc.T("已经是最新版本。", "You're on the latest version."),
                _ => Loc.T("更新源返回的内容无法识别。", "The update feed could not be read."),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            UpdateStatusText.Text = Loc.T($"检查失败：{ex.Message}", $"Check failed: {ex.Message}");
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void OnLink(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
