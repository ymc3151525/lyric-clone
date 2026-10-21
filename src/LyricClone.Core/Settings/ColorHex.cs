using System.Globalization;

namespace LyricClone.Core.Settings;

/// <summary>Parses and formats "#RGB", "#RRGGBB" and "#AARRGGBB" (the leading # is optional).</summary>
public static class ColorHex
{
    public static bool TryParse(string? text, out uint argb)
    {
        argb = 0;
        var s = text?.Trim().TrimStart('#');
        if (string.IsNullOrEmpty(s) || !uint.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v))
        {
            return false;
        }

        switch (s.Length)
        {
            case 3:
                uint r = (v >> 8) & 0xF, g = (v >> 4) & 0xF, b = v & 0xF;
                argb = 0xFF000000 | (r * 0x11) << 16 | (g * 0x11) << 8 | b * 0x11;
                return true;
            case 6:
                argb = 0xFF000000 | v;
                return true;
            case 8:
                argb = v;
                return true;
            default:
                return false;
        }
    }

    /// <summary>"#RRGGBB" when opaque, else "#AARRGGBB".</summary>
    public static string Format(uint argb) =>
        (argb >> 24) == 0xFF ? $"#{argb & 0xFFFFFF:X6}" : $"#{argb:X8}";

    /// <summary>Canonical form of a user-entered colour, or null when it is not a colour.</summary>
    public static string? Normalize(string? text) => TryParse(text, out var v) ? Format(v) : null;
}
