namespace LyricClone.Core.Views;

public enum TaskbarEdge
{
    Bottom,
    Top,
    Left,
    Right,
}

public readonly record struct TaskbarPlacement(bool Visible, bool Unsupported, double Left, double Top, double Width, double Height);

/// <summary>
/// Where the taskbar lyric strip should sit. Vertical taskbars are unsupported (the strip would cover the Start
/// button). Auto-hide and a fullscreen app both hide it so the real taskbar / game stays clickable.
/// </summary>
public static class TaskbarLayout
{
    public static TaskbarPlacement Place(
        TaskbarEdge edge,
        double x,
        double y,
        double width,
        double height,
        bool autoHide,
        bool fullscreenApp,
        double startReserve = 72,
        double trayReserve = 160)
    {
        if (autoHide || fullscreenApp)
        {
            return new TaskbarPlacement(false, false, 0, 0, 0, 0);
        }

        if (edge is TaskbarEdge.Left or TaskbarEdge.Right)
        {
            return new TaskbarPlacement(false, true, 0, 0, 0, 0);
        }

        double left = x + startReserve;
        double w = Math.Max(180, width - startReserve - trayReserve);
        return new TaskbarPlacement(true, false, left, y, w, Math.Max(24, height));
    }
}
