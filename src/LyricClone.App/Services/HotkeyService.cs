using System.Runtime.InteropServices;
using System.Windows.Interop;
using LyricClone.Core.Settings;

namespace LyricClone.App.Services;

public enum HotkeyAction
{
    ToggleDesktop,
    ToggleLock,
    ToggleIsland,
    ToggleTaskbar,
    ToggleFullscreen,
    ToggleStage,
}

/// <summary>Global shortcuts via RegisterHotKey. Empty gestures are skipped. Ids 1..N are reused on each update.</summary>
public sealed partial class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001, ModCtrl = 0x0002, ModShift = 0x0004, ModWin = 0x0008, ModNoRepeat = 0x4000;

    private readonly HwndSource _source;
    private readonly Dictionary<int, HotkeyAction> _ids = [];

    public HotkeyService()
    {
        _source = new HwndSource(new HwndSourceParameters("lyric-clone-hotkeys")
        {
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3),
        });
        _source.AddHook(Hook);
    }

    public event Action<HotkeyAction>? Fired;

    public void Update(HotkeysSettings settings)
    {
        foreach (var id in _ids.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _ids.Clear();
        TryRegister(1, settings.ToggleDesktop, HotkeyAction.ToggleDesktop);
        TryRegister(2, settings.ToggleLock, HotkeyAction.ToggleLock);
        TryRegister(3, settings.ToggleIsland, HotkeyAction.ToggleIsland);
        TryRegister(4, settings.ToggleTaskbar, HotkeyAction.ToggleTaskbar);
        TryRegister(5, settings.ToggleFullscreen, HotkeyAction.ToggleFullscreen);
        TryRegister(6, settings.ToggleStage, HotkeyAction.ToggleStage);
    }

    public void Dispose()
    {
        foreach (var id in _ids.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _source.RemoveHook(Hook);
        _source.Dispose();
    }

    private void TryRegister(int id, string? gesture, HotkeyAction action)
    {
        if (!HotkeyChord.TryParse(gesture, out var chord) || chord.IsEmpty)
        {
            return;
        }

        uint mods = ModNoRepeat;
        if (chord.Ctrl)
        {
            mods |= ModCtrl;
        }

        if (chord.Alt)
        {
            mods |= ModAlt;
        }

        if (chord.Shift)
        {
            mods |= ModShift;
        }

        if (chord.Win)
        {
            mods |= ModWin;
        }

        if (RegisterHotKey(_source.Handle, id, mods, (uint)chord.VirtualKey))
        {
            _ids[id] = action;
        }
    }

    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmHotkey && _ids.TryGetValue(wParam.ToInt32(), out var action))
        {
            Fired?.Invoke(action);
            handled = true;
        }

        return 0;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hWnd, int id);
}
