namespace LyricClone.Core.Views;

/// <summary>Keeps Stage backdrop colours dark enough that white lyric text stays AA (luminance ≤ 0.15).</summary>
public static class StageGradient
{
    public const double MaxLuminance = 0.15;

    public static (byte R, byte G, byte B) CapLuminance(byte r, byte g, byte b, double maxY = MaxLuminance)
    {
        double y = RelativeLuminance(r, g, b);
        if (y <= maxY || y <= 0)
        {
            return (r, g, b);
        }

        double scale = maxY / y;
        return (Mul(r, scale), Mul(g, scale), Mul(b, scale));
    }

    public static double RelativeLuminance(byte r, byte g, byte b) =>
        0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);

    private static double Channel(byte c)
    {
        double s = c / 255.0;
        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    private static byte Mul(byte c, double scale) => (byte)Math.Clamp(Math.Round(c * scale), 0, 255);
}
