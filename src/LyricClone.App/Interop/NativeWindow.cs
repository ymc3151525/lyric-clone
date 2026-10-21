using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LyricClone.App.Interop;

internal static partial class NativeWindow
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x00000020;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_LAYERED = 0x00080000;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out PointI point);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(nint hMonitor, ref MonitorInfoEx lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointI
    {
        public int X;
        public int Y;
    }

    /// <summary>Hides the window from Alt+Tab and keeps it from stealing focus.</summary>
    public static void MakeToolWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)(style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
    }

    /// <summary>Click-through: mouse input goes to whatever is under the window.</summary>
    public static void SetClickThrough(Window window, bool enabled)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        style = enabled ? style | WS_EX_TRANSPARENT | WS_EX_LAYERED : style & ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)style);
    }

    /// <summary>Cursor position in device-independent pixels, or null if it cannot be read.</summary>
    public static Point? CursorDip(Visual relativeTo)
    {
        if (!GetCursorPos(out var pt))
        {
            return null;
        }

        var source = PresentationSource.FromVisual(relativeTo);
        if (source?.CompositionTarget is null)
        {
            return new Point(pt.X, pt.Y);
        }

        return source.CompositionTarget.TransformFromDevice.Transform(new Point(pt.X, pt.Y));
    }

    /// <summary>The monitor that currently contains the window, in device-independent pixels.</summary>
    public static Rect MonitorDip(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var monitor = MonitorFromWindow(hwnd, 2);
        var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
        if (monitor == 0 || !GetMonitorInfoW(monitor, ref info))
        {
            return new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
        }

        var source = PresentationSource.FromVisual(window);
        var from = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = from.Transform(new Point(info.rcMonitor.Left, info.rcMonitor.Top));
        var bottomRight = from.Transform(new Point(info.rcMonitor.Right, info.rcMonitor.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }
}
