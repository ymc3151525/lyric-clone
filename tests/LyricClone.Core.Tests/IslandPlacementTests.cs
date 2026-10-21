using LyricClone.Core.Views;

namespace LyricClone.Core.Tests;

public class IslandPlacementTests
{
    [Fact]
    public void Snaps_flush_to_the_top_edge()
    {
        var (left, top, dock) = IslandPlacement.Snap(0, 0, 1920, 1080, 700, 8, 400, 48);

        Assert.Equal(IslandDock.Top, dock);
        Assert.Equal(0, top);
        Assert.Equal(700, left);
    }

    [Fact]
    public void Leaves_a_free_position_away_from_edges()
    {
        var (left, top, dock) = IslandPlacement.Snap(0, 0, 1920, 1080, 500, 400, 400, 48);

        Assert.Equal(IslandDock.None, dock);
        Assert.Equal(500, left);
        Assert.Equal(400, top);
    }

    [Fact]
    public void Snaps_flush_to_the_left_edge()
    {
        var (_, _, dock) = IslandPlacement.Snap(0, 0, 1920, 1080, 10, 300, 400, 48);

        Assert.Equal(IslandDock.Left, dock);
    }

    [Fact]
    public void Snaps_flush_to_the_top_right_corner()
    {
        var (left, top, dock) = IslandPlacement.Snap(0, 0, 1920, 1080, 1500, 10, 400, 48);

        Assert.Equal(IslandDock.TopRight, dock);
        Assert.Equal(0, top);
        Assert.Equal(1520, left);
    }

    [Fact]
    public void Docked_ears_are_concave_wraps_not_outward_squares()
    {
        var top = IslandChrome.For(IslandDock.Top, 18);
        Assert.True(top.NW.Show && top.NE.Show && !top.SW.Show && !top.SE.Show);
        Assert.Equal((0, 1), (top.NW.HoleX, top.NW.HoleY));
        Assert.Equal((1, 1), (top.NE.HoleX, top.NE.HoleY));
        Assert.Equal(0, top.Corners.TopLeft);
        Assert.Equal(18, top.Margin.Left);

        var topRight = IslandChrome.For(IslandDock.TopRight, 18);
        Assert.True(topRight.NW.Show && !topRight.NE.Show && topRight.SE.Show);
        Assert.Equal(0, topRight.Margin.Right);
        Assert.Equal(18, topRight.Margin.Left);
        Assert.Equal(18, topRight.Margin.Bottom);
        Assert.Equal(0, topRight.Corners.BottomRight);
        Assert.Equal((0, 1), (topRight.NW.HoleX, topRight.NW.HoleY));
        Assert.Equal((0, 1), (topRight.SE.HoleX, topRight.SE.HoleY));

        var left = IslandChrome.For(IslandDock.Left, 18);
        Assert.True(left.NW.Show && left.SW.Show);
        Assert.Equal((1, 0), (left.NW.HoleX, left.NW.HoleY));
        Assert.Equal((1, 1), (left.SW.HoleX, left.SW.HoleY));
    }

    [Fact]
    public void Top_right_stays_flush_when_the_island_grows()
    {
        var compact = IslandPlacement.Snap(0, 0, 1920, 1080, 1720, 0, 200, 48);
        Assert.Equal(IslandDock.TopRight, compact.Dock);
        Assert.Equal(1720, compact.Left);

        double grownLeft = 1920 - 400;
        Assert.Equal(1520, grownLeft);
        Assert.Equal(1920, grownLeft + 400);
        Assert.Equal(1920, compact.Left + 200);
    }

    [Fact]
    public void Width_follows_the_lyric_up_to_the_cap()
    {
        Assert.Equal(160, IslandFit.ClampWidth(80, 160, 560));
        Assert.Equal(240, IslandFit.ClampWidth(240, 160, 560));
        Assert.Equal(560, IslandFit.ClampWidth(900, 160, 560));
        Assert.Equal(180 + IslandFit.BreathPx, IslandFit.PillWidth(180, 0, 0, 160, 560));
        Assert.Equal(560, IslandFit.PillWidth(900, 36, 40, 180, 560));
    }

    [Fact]
    public void Long_line_marquees_hold_then_travel_then_return()
    {
        Assert.Equal(0, IslandFit.MarqueeOffset(100, 0));
        Assert.Equal(0, IslandFit.MarqueeOffset(100, IslandFit.MarqueeHoldMs));
        double travelMs = 100 / IslandFit.MarqueeSpeedPxPerSec * 1000;
        Assert.InRange(IslandFit.MarqueeOffset(100, IslandFit.MarqueeHoldMs + travelMs / 2), 40, 60);
        Assert.InRange(IslandFit.MarqueeOffset(100, IslandFit.MarqueeHoldMs + travelMs), 99, 100);
        Assert.Equal(100, IslandFit.MarqueeOffset(100, IslandFit.MarqueeHoldMs + travelMs + IslandFit.MarqueeHoldMs));
        Assert.InRange(IslandFit.MarqueeOffset(100, IslandFit.MarqueeHoldMs + travelMs + IslandFit.MarqueeHoldMs + travelMs), 0, 1);
        Assert.Equal(0, IslandFit.MarqueeOffset(overflow: 0, elapsedMs: 5_000));
    }
}
