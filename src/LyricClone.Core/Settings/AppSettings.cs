using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;
using LyricClone.Core.Views;

namespace LyricClone.Core.Settings;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

public enum LyricAlignment
{
    Left,
    Center,
    Right,
}

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public UiLanguage Language { get; set; } = UiLanguage.System;

    public long DeviceDelayMs { get; set; }

    public bool TimelineSmoothing { get; set; } = true;

    public bool AutoUpdateSavedOnline { get; set; }

    /// <summary>When false, saved lyrics are still used but nothing new is fetched or written.</summary>
    public bool AutoManageLibrary { get; set; } = true;

    public VersionPickMode VersionPick { get; set; } = VersionPickMode.SourceFirst;

    public bool ShowChineseTranslation { get; set; } = true;

    public RomajiDisplay Romaji { get; set; } = RomajiDisplay.Off;

    public bool PreferSimplifiedChinese { get; set; } = true;

    public bool SplitLongLines { get; set; }

    public bool ShowExplicitLyrics { get; set; } = true;

    public List<string> SourceOrder { get; set; } = [.. LyricsSources.DefaultOrder];

    public string? LocalLyricsFolder { get; set; }

    /// <summary>Where to look for audio files with embedded lyrics; null = the user's Music folder.</summary>
    public string? MusicFolder { get; set; }

    public bool IncludeBrowserSessions { get; set; }

    public bool ShowNoPlayerHint { get; set; } = true;

    public Dictionary<string, AppProfile> AppProfiles
    {
        get;
        set => field = new Dictionary<string, AppProfile>(value, StringComparer.OrdinalIgnoreCase);
    } = new(StringComparer.OrdinalIgnoreCase);

    public DesktopLyricsSettings Desktop { get; set; } = new();

    public IslandSettings Island { get; set; } = new();

    public TaskbarLyricsSettings Taskbar { get; set; } = new();

    public FullscreenSettings Fullscreen { get; set; } = new();

    public StageSettings Stage { get; set; } = new();

    /// <summary>Optional JSON feed URL <c>{"version","url","notes"}</c>. Empty means this build has no update source.</summary>
    public string? UpdateFeedUrl { get; set; }

    public HotkeysSettings Hotkeys { get; set; } = new();

    public AppProfile ProfileFor(string appId)
    {
        if (!AppProfiles.TryGetValue(appId, out var profile))
        {
            profile = new AppProfile { AppId = appId, DisplayName = appId, Enabled = !LooksLikeBrowser(appId) || IncludeBrowserSessions };
            AppProfiles[appId] = profile;
        }

        return profile;
    }

    public IReadOnlyList<string> EffectiveSourceOrder(string appId)
    {
        if (AppProfiles.TryGetValue(appId, out var p) && p.SourceOrder is { Count: > 0 } order)
        {
            return LyricsSources.WithKnownDefaults(order);
        }

        return LyricsSources.WithKnownDefaults(SourceOrder.Count > 0 ? SourceOrder : LyricsSources.DefaultOrder);
    }

    /// <summary>Master switch plus the view's own toggle. Translation still only appears when a line has one.</summary>
    public bool ShowsTranslation(bool viewFlag) => ShowChineseTranslation && viewFlag;

    /// <summary>Drops settings that the current build no longer honours so old files keep loading.</summary>
    public void Normalize()
    {
        if (Island.SideContent == IslandSideContent.Cover)
        {
            Island.SideContent = IslandSideContent.Default;
        }
    }

    public static bool LooksLikeBrowser(string appId)
    {
        var id = appId.ToLowerInvariant();
        return id.Contains("chrome") || id.Contains("msedge") || id.Contains("firefox") || id.Contains("opera") || id.Contains("brave");
    }
}

public sealed class AppProfile
{
    public string AppId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public long SessionDelayMs { get; set; }

    public bool? TimelineSmoothing { get; set; }

    /// <summary>Null means use the global order.</summary>
    public List<string>? SourceOrder { get; set; }
}

public sealed class DesktopLyricsSettings
{
    public bool Enabled { get; set; } = true;

    public bool Locked { get; set; }

    public double FontSize { get; set; } = 32;

    public string? FontFamily { get; set; }

    public bool ShowTranslation { get; set; } = true;

    public bool TwoLines { get; set; } = true;

    public LyricAlignment Alignment { get; set; } = LyricAlignment.Center;

    public ViewColors Colors { get; set; } = new();

    public bool Outline { get; set; } = true;

    /// <summary>Window position in device-independent pixels; null = default position on the primary monitor.</summary>
    public double? Left { get; set; }

    public double? Top { get; set; }

    public double Width { get; set; } = 900;
}

public enum IslandSideContent
{
    Default,
    Hidden,
    Cover,
}

public sealed class IslandSettings
{
    public bool Enabled { get; set; }

    public string? FontFamily { get; set; }

    public bool AutoHideWhenIdle { get; set; } = true;

    public bool ShowTranslation { get; set; } = true;

    public bool ContinentMode { get; set; }

    public bool AlwaysTwoLines { get; set; }

    public bool TrackInfoOnly { get; set; }

    public bool Bold { get; set; }

    public bool Karaoke { get; set; } = true;

    public bool HideOnFullscreen { get; set; } = true;

    public bool HideOnLyricsFullscreen { get; set; } = true;

    public double Opacity { get; set; } = 1;

    public double BackgroundOpacity { get; set; } = 1;

    public double Scale { get; set; } = 1;

    public IslandSideContent SideContent { get; set; } = IslandSideContent.Default;

    /// <summary>Cover was never wired; treat it as the default transport chrome.</summary>
    public IslandSideContent EffectiveSideContent =>
        SideContent == IslandSideContent.Cover ? IslandSideContent.Default : SideContent;

    public ViewColors Colors { get; set; } = new();

    /// <summary>Null = centred on the docked edge (or the default top-centre seat).</summary>
    public double? Left { get; set; }

    public double? Top { get; set; }

    public IslandDock Dock { get; set; } = IslandDock.Top;
}

public sealed class TaskbarLyricsSettings
{
    public bool Enabled { get; set; }

    public string? FontFamily { get; set; }

    public double FontSize { get; set; } = 14;

    public bool ShowTranslation { get; set; } = true;

    public bool ShowCover { get; set; } = true;

    public ViewColors Colors { get; set; } = new();
}

public sealed class HotkeysSettings
{
    /// <summary>Gestures as "Ctrl+Alt+L". Empty means off.</summary>
    public string? ToggleDesktop { get; set; }

    public string? ToggleLock { get; set; }

    public string? ToggleIsland { get; set; }

    public string? ToggleTaskbar { get; set; }

    public string? ToggleFullscreen { get; set; }

    public string? ToggleStage { get; set; }
}

public sealed class FullscreenSettings
{
    public string? FontFamily { get; set; }

    public bool ShowTranslation { get; set; } = true;

    public bool BlurredCover { get; set; } = true;

    public ViewColors Colors { get; set; } = new();
}

public sealed class StageSettings
{
    public string? FontFamily { get; set; }

    public bool ShowTranslation { get; set; } = true;

    public ViewColors Colors { get; set; } = new();
}

/// <summary>Colours as "#RRGGBB" or "#AARRGGBB"; null means the theme's colour for that role.</summary>
public sealed class ViewColors
{
    public string? Text { get; set; }

    public string? Sung { get; set; }

    public string? Translation { get; set; }

    public string? Outline { get; set; }
}
