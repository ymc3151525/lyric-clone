using System.Windows;
using System.Windows.Media;
using LyricClone.App.Views;
using LyricClone.Core.Settings;

namespace LyricClone.App.Lyrics;

/// <summary>Turns a view's colour settings into brushes, falling back to the lyric tokens for unset roles.</summary>
public static class LyricColors
{
    public static Brush Text(ViewColors c) => Pick(c.Text, "Brush.Lyric.Text");

    public static Brush Sung(ViewColors c) => Pick(c.Sung, "Brush.Lyric.Sung");

    public static Brush Translation(ViewColors c) => Pick(c.Translation, "Brush.Lyric.Translation");

    public static Brush Outline(ViewColors c) => Pick(c.Outline, "Brush.Lyric.Stroke");

    /// <summary>Dim rows (next line, past lines) follow the text colour at reduced opacity when it is customised.</summary>
    public static Brush Dim(ViewColors c)
    {
        if (c.Text is null)
        {
            return (Brush)Application.Current.FindResource("Brush.Lyric.Dim");
        }

        var b = new SolidColorBrush(ColorField.ToColor(c.Text)) { Opacity = 0.55 };
        b.Freeze();
        return b;
    }

    public static void Apply(LyricLineView view, ViewColors c)
    {
        view.TextBrush = Text(c);
        view.SungBrush = Sung(c);
        view.TranslationBrush = Translation(c);
        view.StrokeBrush = Outline(c);
        view.DimBrush = Dim(c);
    }

    private static Brush Pick(string? hex, string token)
    {
        if (hex is null || !ColorHex.TryParse(hex, out _))
        {
            return (Brush)Application.Current.FindResource(token);
        }

        var b = new SolidColorBrush(ColorField.ToColor(hex));
        b.Freeze();
        return b;
    }
}
