using System.Globalization;

namespace LyricClone.Core.Settings;

public enum UiLanguage
{
    System,
    Chinese,
    English,
}

/// <summary>Picks zh-Hans or en from the setting / OS. Call <see cref="Apply"/> at start-up and when the user changes it.</summary>
public static class Loc
{
    public static bool English { get; private set; }

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("zh-Hans");

    public static void Apply(UiLanguage language)
    {
        Culture = language switch
        {
            UiLanguage.English => CultureInfo.GetCultureInfo("en"),
            UiLanguage.Chinese => CultureInfo.GetCultureInfo("zh-Hans"),
            _ => CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "zh"
                ? CultureInfo.GetCultureInfo("zh-Hans")
                : CultureInfo.GetCultureInfo("en"),
        };
        English = Culture.TwoLetterISOLanguageName != "zh";
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = Culture;
    }

    public static string T(string zh, string en) => English ? en : zh;
}
