using System.Windows;
using LyricClone.Core.Settings;
using ThemeMode = LyricClone.Core.Settings.ThemeMode;
using Microsoft.Win32;

namespace LyricClone.App.Services;

/// <summary>Swaps the Colors.Light / Colors.Dark dictionary; "System" follows the Windows app theme.</summary>
public static class ThemeService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static ThemeMode _mode = ThemeMode.System;
    private static bool _listening;

    public static bool IsDark { get; private set; }

    public static void Apply(ThemeMode mode)
    {
        _mode = mode;
        if (!_listening)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _listening = true;
        }

        bool dark = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => SystemPrefersDark(),
        };
        if (dark == IsDark && FindColors() is not null)
        {
            return;
        }

        IsDark = dark;
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var replacement = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/LyricClone;component/Themes/Colors.{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute),
        };
        var existing = FindColors();
        if (existing is null)
        {
            dictionaries.Insert(0, replacement);
        }
        else
        {
            dictionaries[dictionaries.IndexOf(existing)] = replacement;
        }
    }

    public static bool SystemPrefersDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    private static ResourceDictionary? FindColors() =>
        Application.Current.Resources.MergedDictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString.Contains("Themes/Colors.", StringComparison.OrdinalIgnoreCase) == true);

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && _mode == ThemeMode.System)
        {
            Application.Current.Dispatcher.InvokeAsync(() => Apply(ThemeMode.System));
        }
    }
}
