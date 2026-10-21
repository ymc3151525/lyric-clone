using System.Windows.Media;
using System.Windows.Media.Imaging;
using LyricClone.Core.Views;

namespace LyricClone.App.Lyrics;

/// <summary>Picks two colours from a cover image and darkens them for the Stage backdrop.</summary>
public static class CoverSampler
{
    public static readonly Color FallbackA = Color.FromRgb(0x14, 0x18, 0x22);
    public static readonly Color FallbackB = Color.FromRgb(0x1C, 0x14, 0x22);

    public static (Color A, Color B) Sample(BitmapSource? image)
    {
        if (image is null)
        {
            return (FallbackA, FallbackB);
        }

        try
        {
            var small = new TransformedBitmap(image, new ScaleTransform(32.0 / Math.Max(1, image.PixelWidth), 32.0 / Math.Max(1, image.PixelHeight)));
            var converted = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            var a = Average(pixels, stride, converted.PixelWidth, converted.PixelHeight, 0, 0, converted.PixelWidth / 2, converted.PixelHeight / 2);
            var b = Average(pixels, stride, converted.PixelWidth, converted.PixelHeight, converted.PixelWidth / 2, converted.PixelHeight / 2, converted.PixelWidth, converted.PixelHeight);
            return (Darken(a), Darken(b));
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return (FallbackA, FallbackB);
        }
    }

    public static Color Darken(Color c)
    {
        var (r, g, b) = StageGradient.CapLuminance(c.R, c.G, c.B);
        return Color.FromRgb(r, g, b);
    }

    private static Color Average(byte[] pixels, int stride, int width, int height, int x0, int y0, int x1, int y1)
    {
        x0 = Math.Clamp(x0, 0, width);
        y0 = Math.Clamp(y0, 0, height);
        x1 = Math.Clamp(x1, x0 + 1, width);
        y1 = Math.Clamp(y1, y0 + 1, height);
        long r = 0, g = 0, b = 0, n = 0;
        for (int y = y0; y < y1; y++)
        {
            int row = y * stride;
            for (int x = x0; x < x1; x++)
            {
                int i = row + x * 4;
                b += pixels[i];
                g += pixels[i + 1];
                r += pixels[i + 2];
                n++;
            }
        }

        if (n == 0)
        {
            return FallbackA;
        }

        return Color.FromRgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }
}
