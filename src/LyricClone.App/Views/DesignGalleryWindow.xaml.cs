using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LyricClone.Core.Lyrics;

namespace LyricClone.App.Views;

public partial class DesignGalleryWindow : Window
{
    public DesignGalleryWindow()
    {
        InitializeComponent();

        var sample = LrcParser.Parse(
            "[00:01.00]<00:01.00>这是 <00:01.50>一行 <00:02.00>用于 <00:02.50>测试 <00:03.00>的示例<00:04.00>",
            "[00:01.00]A made-up line for testing");
        WordLine.Line = sample.Lines[0];
        WordLine.PositionMs = 2_700;

        NextSample.Line = new LyricLine(5_000, 8_000, "下一行同样是虚构的示例", []);

        IslandSample.Line = sample.Lines[0];
        IslandSample.PositionMs = 2_200;
    }

    /// <summary>Renders the page to a PNG so the design system can be checked without a person at the screen.</summary>
    public void SaveScreenshot(string path)
    {
        UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        var size = new Size(Page.ActualWidth + 48, Page.ActualHeight + 48);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width * dpi.DpiScaleX), (int)Math.Ceiling(size.Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle((Brush)FindResource("Brush.Bg"), null, new Rect(size));
            dc.DrawRectangle(new VisualBrush(Page), null, new Rect(new Point(24, 24), new Size(Page.ActualWidth, Page.ActualHeight)));
        }

        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
