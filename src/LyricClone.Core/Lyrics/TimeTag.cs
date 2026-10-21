using System.Globalization;

namespace LyricClone.Core.Lyrics;

internal static class TimeTag
{
    /// <summary>Parses "mm:ss", "mm:ss.f", "mm:ss.ff", "mm:ss.fff", "mm:ss:ff" and "hh:mm:ss.fff".</summary>
    public static bool TryParseClock(ReadOnlySpan<char> s, out long ms)
    {
        ms = 0;
        s = s.Trim();
        if (s.IsEmpty)
        {
            return false;
        }

        Span<Range> parts = stackalloc Range[4];
        int count = s.Split(parts, ':');
        if (count < 2 || count > 3)
        {
            return false;
        }

        long hours = 0;
        ReadOnlySpan<char> minPart, secPart;
        ReadOnlySpan<char> fracFromColon = default;

        if (count == 3)
        {
            var a = s[parts[0]];
            var b = s[parts[1]];
            var c = s[parts[2]];
            if (c.Contains('.'))
            {
                if (!long.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out hours))
                {
                    return false;
                }

                minPart = b;
                secPart = c;
            }
            else
            {
                minPart = a;
                secPart = b;
                fracFromColon = c;
            }
        }
        else
        {
            minPart = s[parts[0]];
            secPart = s[parts[1]];
        }

        if (!long.TryParse(minPart, NumberStyles.None, CultureInfo.InvariantCulture, out long minutes))
        {
            return false;
        }

        ReadOnlySpan<char> wholeSec = secPart;
        ReadOnlySpan<char> frac = fracFromColon;
        int dot = secPart.IndexOf('.');
        if (dot >= 0)
        {
            wholeSec = secPart[..dot];
            frac = secPart[(dot + 1)..];
        }

        if (!long.TryParse(wholeSec, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds))
        {
            return false;
        }

        if (!TryParseFraction(frac, out long fracMs))
        {
            return false;
        }

        ms = ((hours * 60 + minutes) * 60 + seconds) * 1000 + fracMs;
        return true;
    }

    /// <summary>Parses TTML clock values: clock form, "12.5s", "1500ms", or bare seconds.</summary>
    public static bool TryParseTtml(string? value, out long ms)
    {
        ms = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var v = value.Trim();
        if (v.Contains(':'))
        {
            return TryParseClock(v, out ms);
        }

        if (v.EndsWith("ms", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out double msVal))
        {
            ms = (long)Math.Round(msVal);
            return true;
        }

        if (v.EndsWith('s')
            && double.TryParse(v[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double sVal))
        {
            ms = (long)Math.Round(sVal * 1000);
            return true;
        }

        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double bare))
        {
            ms = (long)Math.Round(bare * 1000);
            return true;
        }

        return false;
    }

    private static bool TryParseFraction(ReadOnlySpan<char> frac, out long ms)
    {
        ms = 0;
        if (frac.IsEmpty)
        {
            return true;
        }

        if (frac.Length > 3)
        {
            frac = frac[..3];
        }

        if (!long.TryParse(frac, NumberStyles.None, CultureInfo.InvariantCulture, out long value))
        {
            return false;
        }

        ms = frac.Length switch
        {
            1 => value * 100,
            2 => value * 10,
            _ => value,
        };
        return true;
    }
}
