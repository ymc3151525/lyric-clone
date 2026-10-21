using System.Runtime.InteropServices;
using System.Windows;
using LyricClone.Core.Views;

namespace LyricClone.App.Interop;

public readonly record struct TaskbarSnapshot(
    TaskbarEdge Edge,
    double Left,
    double Top,
    double Width,
    double Height,
    bool AutoHide,
    bool FullscreenApp,
    bool QueryFailed);

public interface ITaskbarQuery
{
    TaskbarSnapshot Read();
}

/// <summary>Reads the shell taskbar through ABM_GETTASKBARPOS and whether a fullscreen window is covering the monitor.</summary>
public sealed partial class TaskbarQuery : ITaskbarQuery
{
    private const uint AbmGetState = 4;
    private const uint AbmGetTaskbarPos = 5;
    private const int AbsAutoHide = 1;
    private const int AbeLeft = 0, AbeTop = 1, AbeRight = 2, AbeBottom = 3;
    private const int MonitorDefaultToNearest = 2;

    public TaskbarSnapshot Read()
    {
        try
        {
            var data = new AppBarData { cbSize = Marshal.SizeOf<AppBarData>() };
            if (SHAppBarMessage(AbmGetTaskbarPos, ref data) == 0)
            {
                return new TaskbarSnapshot(TaskbarEdge.Bottom, 0, 0, 0, 0, false, false, true);
            }

            var state = new AppBarData { cbSize = Marshal.SizeOf<AppBarData>() };
            bool autoHide = (SHAppBarMessage(AbmGetState, ref state) & AbsAutoHide) != 0;
            var edge = data.uEdge switch
            {
                AbeLeft => TaskbarEdge.Left,
                AbeTop => TaskbarEdge.Top,
                AbeRight => TaskbarEdge.Right,
                _ => TaskbarEdge.Bottom,
            };

            uint dpi = GetDpiForWindow(data.hWnd is 0 ? GetDesktopWindow() : data.hWnd);
            double scale = dpi == 0 ? 1 : dpi / 96.0;
            double l = data.rc.Left / scale, t = data.rc.Top / scale;
            double w = (data.rc.Right - data.rc.Left) / scale, h = (data.rc.Bottom - data.rc.Top) / scale;
            return new TaskbarSnapshot(edge, l, t, w, h, autoHide, ForegroundIsFullscreen(), false);
        }
        catch
        {
            return new TaskbarSnapshot(TaskbarEdge.Bottom, 0, 0, 0, 0, false, false, true);
        }
    }

    private static bool ForegroundIsFullscreen()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0)
        {
            return false;
        }

        GetClassName(hwnd, out var className);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
        {
            return false;
        }

        if (!GetWindowRect(hwnd, out var wr) || !GetMonitorInfo(MonitorFromWindow(hwnd, MonitorDefaultToNearest), out var mi))
        {
            return false;
        }

        return wr.Left <= mi.Left + 2 && wr.Top <= mi.Top + 2 && wr.Right >= mi.Right - 2 && wr.Bottom >= mi.Bottom - 2
            && (wr.Right - wr.Left) >= (mi.Right - mi.Left) - 4;
    }

    [LibraryImport("shell32.dll")]
    private static partial nuint SHAppBarMessage(uint dwMessage, ref AppBarData pData);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial nint GetDesktopWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hWnd, out NativeRect lpRect);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint hWnd, char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfoW(nint hMonitor, ref MonitorInfoEx lpmi);

    private static void GetClassName(nint hwnd, out string name)
    {
        var buf = new char[256];
        int n = GetClassNameW(hwnd, buf, buf.Length);
        name = n <= 0 ? "" : new string(buf, 0, n);
    }

    private static bool GetMonitorInfo(nint monitor, out NativeRect monitorRect)
    {
        var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
        if (!GetMonitorInfoW(monitor, ref info))
        {
            monitorRect = default;
            return false;
        }

        monitorRect = info.rcMonitor;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public NativeRect rc;
        public int lParam;
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
