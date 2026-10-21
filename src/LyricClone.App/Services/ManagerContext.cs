using LyricClone.Core.Lyrics;
using LyricClone.Infrastructure.Data;
using LyricClone.Infrastructure.Lrclib;
using LyricClone.Infrastructure.Lyrics;
using LyricClone.Infrastructure.Settings;

namespace LyricClone.App.Services;

/// <summary>Services the Tracks &amp; Lyrics windows and their dialogs share.</summary>
public sealed record ManagerContext(
    ILibraryRepository Repository,
    ILyricsResolver Resolver,
    ILyricsParser Parser,
    ILrclibClient Lrclib,
    ISettingsStore Settings,
    NowPlayingService NowPlaying);
