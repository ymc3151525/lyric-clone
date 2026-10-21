using Microsoft.Win32;

namespace LyricClone.App.Services;

/// <summary>
/// "Start with Windows" through the current user's Run key. The registry is the only source of truth, so turning it
/// off in Windows' Startup apps page is reflected here too.
/// </summary>
public sealed class Autostart
{
    public const string Argument = "--autostart";
    public const string DefaultKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "lyric-clone";

    private readonly string _keyPath;
    private readonly string _exePath;

    public Autostart(string keyPath = DefaultKeyPath, string? exePath = null)
    {
        _keyPath = keyPath;
        _exePath = exePath ?? Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path");
    }

    public string Command => $"\"{_exePath}\" {Argument}";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
            return key?.GetValue(ValueName) is string { Length: > 0 };
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
        if (enabled)
        {
            key.SetValue(ValueName, Command, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Points an existing entry at this executable again, e.g. after the app folder was moved.</summary>
    public bool RepairIfMoved()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true);
        if (key?.GetValue(ValueName) is not string current || current == Command)
        {
            return false;
        }

        key.SetValue(ValueName, Command, RegistryValueKind.String);
        return true;
    }
}
