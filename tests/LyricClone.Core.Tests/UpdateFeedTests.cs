using LyricClone.Core.Update;
using LyricClone.Core.Views;

namespace LyricClone.Core.Tests;

public sealed class UpdateFeedTests
{
    [Fact]
    public void Empty_or_junk_is_invalid()
    {
        var current = new Version(0, 1, 0);
        Assert.Equal(UpdateCheckStatus.Invalid, UpdateFeed.Parse(null, current).Status);
        Assert.Equal(UpdateCheckStatus.Invalid, UpdateFeed.Parse("{", current).Status);
        Assert.Equal(UpdateCheckStatus.Invalid, UpdateFeed.Parse("{\"version\":\"nope\"}", current).Status);
    }

    [Fact]
    public void Newer_version_is_available()
    {
        var result = UpdateFeed.Parse("""{"version":"0.2.0","url":"https://example.invalid/app","notes":"fix"}""", new Version(0, 1, 0));
        Assert.Equal(UpdateCheckStatus.Available, result.Status);
        Assert.Equal(new Version(0, 2, 0), result.Latest);
        Assert.Equal("https://example.invalid/app", result.Link);
        Assert.Equal("fix", result.Notes);
    }

    [Fact]
    public void Same_or_older_is_up_to_date()
    {
        Assert.Equal(UpdateCheckStatus.UpToDate, UpdateFeed.Parse("""{"version":"0.1.0"}""", new Version(0, 1, 0)).Status);
        Assert.Equal(UpdateCheckStatus.UpToDate, UpdateFeed.Parse("""{"version":"0.0.9"}""", new Version(0, 1, 0)).Status);
    }

    [Fact]
    public void Stage_gradient_caps_bright_colours()
    {
        var (r, g, b) = StageGradient.CapLuminance(255, 255, 255);
        Assert.True(StageGradient.RelativeLuminance(r, g, b) <= StageGradient.MaxLuminance + 0.001);
        Assert.Equal((10, 10, 10), StageGradient.CapLuminance(10, 10, 10));
    }
}
