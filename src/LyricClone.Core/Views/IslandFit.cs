namespace LyricClone.Core.Views;

/// <summary>
/// Island width follows the current lyric. Lines longer than the cap ping-pong inside the pill.
/// </summary>
public static class IslandFit
{
    public const double MarqueeSpeedPxPerSec = 56;
    public const double MarqueeHoldMs = 800;
    public const double HeaderMinWidth = 288;
    public const double BreathPx = 32;

    public static double PillWidth(double lyrics, double cover, double pad, double min, double max) =>
        ClampWidth(cover + lyrics + pad + BreathPx, min, max);

    public static double ClampWidth(double content, double min, double max)
    {
        if (max < min)
        {
            return min;
        }

        return Math.Clamp(content, min, max);
    }

    /// <summary>
    /// 0 = show the start of the line; <paramref name="overflow"/> = show the end.
    /// Holds at each end, then travels the other way.
    /// </summary>
    public static double MarqueeOffset(double overflow, double elapsedMs, double speedPxPerSec = MarqueeSpeedPxPerSec, double holdMs = MarqueeHoldMs)
    {
        if (overflow <= 0.5 || speedPxPerSec <= 0)
        {
            return 0;
        }

        double travelMs = overflow / speedPxPerSec * 1000;
        double cycle = holdMs + travelMs + holdMs + travelMs;
        double t = elapsedMs % cycle;
        if (t < 0)
        {
            t += cycle;
        }

        if (t <= holdMs)
        {
            return 0;
        }

        t -= holdMs;
        if (t <= travelMs)
        {
            return overflow * (t / travelMs);
        }

        t -= travelMs;
        if (t <= holdMs)
        {
            return overflow;
        }

        t -= holdMs;
        return overflow * (1 - t / travelMs);
    }
}
