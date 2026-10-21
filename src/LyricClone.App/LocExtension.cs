using System.Windows.Markup;
using LyricClone.Core.Settings;

namespace LyricClone.App;

/// <summary>Picks zh or en at load. Reopen the window after changing the language setting.</summary>
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string zh, string en)
    {
        Zh = zh;
        En = en;
    }

    public string Zh { get; set; } = "";

    public string En { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Zh, En);
}
