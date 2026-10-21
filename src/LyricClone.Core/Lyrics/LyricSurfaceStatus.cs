using LyricClone.Core.Settings;

namespace LyricClone.Core.Lyrics;

public enum LyricEmptyKind
{
    Title,
    Unavailable,
    NoTrack,
    Resolving,
    Instrumental,
    NotFound,
    Error,
}

/// <summary>Short copy the lyric surfaces share so a switch that does nothing still explains itself.</summary>
public static class LyricSurfaceStatus
{
    public static string Text(LyricEmptyKind kind, string? titleArtist = null) => kind switch
    {
        LyricEmptyKind.Unavailable => Loc.T(
            "这台电脑上的 Windows 媒体控制不可用，无法读取播放信息。",
            "Windows media control is not available on this PC, so playback cannot be read."),
        LyricEmptyKind.NoTrack => Loc.T("没有正在播放的内容", "Nothing is playing"),
        LyricEmptyKind.Resolving => titleArtist ?? "",
        LyricEmptyKind.Instrumental => Loc.T("纯音乐", "Instrumental"),
        LyricEmptyKind.NotFound => Loc.T("这首歌暂时没有歌词", "No lyrics for this track yet"),
        LyricEmptyKind.Error => Loc.T("获取歌词失败", "Couldn't fetch lyrics"),
        _ => titleArtist ?? "",
    };
}
