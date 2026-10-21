namespace LyricClone.Core.Views;

public enum IslandDock
{
    None,
    Top,
    Bottom,
    Left,
    Right,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>Keeps the island flush to a monitor edge or corner when the user drops it nearby.</summary>
public static class IslandPlacement
{
    public const double SnapDistance = 36;

    public static bool IsTop(IslandDock dock) =>
        dock is IslandDock.Top or IslandDock.TopLeft or IslandDock.TopRight;

    public static bool IsBottom(IslandDock dock) =>
        dock is IslandDock.Bottom or IslandDock.BottomLeft or IslandDock.BottomRight;

    public static bool IsLeft(IslandDock dock) =>
        dock is IslandDock.Left or IslandDock.TopLeft or IslandDock.BottomLeft;

    public static bool IsRight(IslandDock dock) =>
        dock is IslandDock.Right or IslandDock.TopRight or IslandDock.BottomRight;

    public static (double Left, double Top, IslandDock Dock) Snap(double monitorLeft, double monitorTop, double monitorWidth, double monitorHeight, double left, double top, double width, double height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        double right = monitorLeft + monitorWidth;
        double bottom = monitorTop + monitorHeight;

        double gapLeft = left - monitorLeft;
        double gapTop = top - monitorTop;
        double gapRight = right - (left + width);
        double gapBottom = bottom - (top + height);

        bool nearLeft = gapLeft <= SnapDistance;
        bool nearTop = gapTop <= SnapDistance;
        bool nearRight = gapRight <= SnapDistance;
        bool nearBottom = gapBottom <= SnapDistance;

        var dock = (nearTop, nearBottom, nearLeft, nearRight) switch
        {
            (true, _, true, _) => IslandDock.TopLeft,
            (true, _, _, true) => IslandDock.TopRight,
            (_, true, true, _) => IslandDock.BottomLeft,
            (_, true, _, true) => IslandDock.BottomRight,
            _ => NearestEdge(gapLeft, gapTop, gapRight, gapBottom),
        };

        return dock switch
        {
            IslandDock.Top => (Clamp(left, monitorLeft, right - width), monitorTop, dock),
            IslandDock.Bottom => (Clamp(left, monitorLeft, right - width), bottom - height, dock),
            IslandDock.Left => (monitorLeft, Clamp(top, monitorTop, bottom - height), dock),
            IslandDock.Right => (right - width, Clamp(top, monitorTop, bottom - height), dock),
            IslandDock.TopLeft => (monitorLeft, monitorTop, dock),
            IslandDock.TopRight => (right - width, monitorTop, dock),
            IslandDock.BottomLeft => (monitorLeft, bottom - height, dock),
            IslandDock.BottomRight => (right - width, bottom - height, dock),
            _ => (Clamp(left, monitorLeft, right - width), Clamp(top, monitorTop, bottom - height), IslandDock.None),
        };
    }

    public static double CenteredLeft(double monitorLeft, double monitorWidth, double width) =>
        monitorLeft + (monitorWidth - Math.Max(1, width)) / 2;

    private static IslandDock NearestEdge(double gapLeft, double gapTop, double gapRight, double gapBottom)
    {
        var dock = IslandDock.None;
        double best = SnapDistance + 1;
        Consider(IslandDock.Top, gapTop);
        Consider(IslandDock.Bottom, gapBottom);
        Consider(IslandDock.Left, gapLeft);
        Consider(IslandDock.Right, gapRight);
        return dock;

        void Consider(IslandDock edge, double gap)
        {
            if (gap <= SnapDistance && gap < best)
            {
                best = gap;
                dock = edge;
            }
        }
    }

    private static double Clamp(double value, double min, double max) =>
        max < min ? min : Math.Min(max, Math.Max(min, value));
}
