namespace LyricClone.Core.Views;

/// <summary>
/// Docked-island wrap: a square minus a circle, so the ear is a concave quarter-circle beside the pill
/// instead of a square with an outward corner.
/// Hole coordinates are 0 or 1 in unit space (left/top vs right/bottom of the ear).
/// </summary>
public readonly record struct IslandEarLayout(
    double EarSize,
    ThicknessDip Margin,
    CornerDip Corners,
    IslandEarSpec NW,
    IslandEarSpec NE,
    IslandEarSpec SW,
    IslandEarSpec SE);

public readonly record struct IslandEarSpec(bool Show, double HoleX, double HoleY);

public readonly record struct ThicknessDip(double Left, double Top, double Right, double Bottom);

public readonly record struct CornerDip(double TopLeft, double TopRight, double BottomRight, double BottomLeft);

public static class IslandChrome
{
    public static IslandEarLayout For(IslandDock dock, double radius)
    {
        double r = Math.Clamp(radius, 12, 28);
        double e = r;
        double outer = 8;
        var bl = new IslandEarSpec(true, 0, 1);
        var br = new IslandEarSpec(true, 1, 1);
        var tl = new IslandEarSpec(true, 0, 0);
        var tr = new IslandEarSpec(true, 1, 0);
        var off = new IslandEarSpec(false, 0, 1);

        return dock switch
        {
            IslandDock.Top => new(e, new(e, 0, e, outer), new(0, 0, r, r), bl, br, off, off),
            IslandDock.Bottom => new(e, new(e, outer, e, 0), new(r, r, 0, 0), off, off, tl, tr),
            IslandDock.Left => new(e, new(0, e, outer, e), new(0, r, r, 0), tr, off, br, off),
            IslandDock.Right => new(e, new(outer, e, 0, e), new(r, 0, 0, r), off, tl, off, bl),
            // Corner: flush to both edges. The wrap that would sit in the gap moves to the inner free corner.
            IslandDock.TopRight => new(e, new(e, 0, 0, e), new(0, 0, 0, r), bl, off, off, bl),
            IslandDock.TopLeft => new(e, new(0, 0, e, e), new(0, 0, r, 0), off, br, br, off),
            IslandDock.BottomRight => new(e, new(e, e, 0, 0), new(r, 0, 0, 0), off, tl, tl, off),
            IslandDock.BottomLeft => new(e, new(0, e, e, 0), new(0, r, 0, 0), tr, off, off, tr),
            _ => new(e, new(outer, outer, outer, outer), new(r, r, r, r), off, off, off, off),
        };
    }
}
