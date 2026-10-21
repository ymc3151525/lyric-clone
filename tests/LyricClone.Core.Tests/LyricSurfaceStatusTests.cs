using LyricClone.Core.Lyrics;

namespace LyricClone.Core.Tests;

public sealed class LyricSurfaceStatusTests
{
    [Fact]
    public void Empty_surface_uses_a_short_reason_and_never_the_raw_error()
    {
        Assert.Equal("纯音乐", LyricSurfaceStatus.Text(LyricEmptyKind.Instrumental));
        Assert.Equal("这首歌暂时没有歌词", LyricSurfaceStatus.Text(LyricEmptyKind.NotFound));
        Assert.Equal("获取歌词失败", LyricSurfaceStatus.Text(LyricEmptyKind.Error, "SQLite Error 19"));
        Assert.Equal("示例 — 艺人", LyricSurfaceStatus.Text(LyricEmptyKind.Resolving, "示例 — 艺人"));
        Assert.DoesNotContain("SQLite", LyricSurfaceStatus.Text(LyricEmptyKind.Error, "SQLite Error 19"));
    }
}
