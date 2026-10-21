using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;
using LyricClone.Infrastructure.Data;

namespace LyricClone.App.Services;

/// <summary>Debug helpers for rendering windows to PNG and seeding a throwaway library with made-up lyrics.</summary>
public static class Screenshots
{
    public static void Save(Window window, string path, Brush? background = null)
    {
        if (window.Content is not FrameworkElement root)
        {
            return;
        }

        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var m = root.Margin;
        var size = new Size(root.ActualWidth + m.Left + m.Right, root.ActualHeight + m.Top + m.Bottom);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width * dpi.DpiScaleX), (int)Math.Ceiling(size.Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(background ?? (Brush)window.FindResource("Brush.Bg"), null, new Rect(size));
            var brush = new VisualBrush(root)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(size),
                Stretch = Stretch.None,
            };
            dc.DrawRectangle(brush, null, new Rect(size));
        }

        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static async Task SeedDemoAsync(ILibraryRepository repo, ILyricsParser parser)
    {
        if ((await repo.QueryTracksAsync(new TrackQuery(Limit: 1))).Count > 0)
        {
            return;
        }

        const string online = """
            [00:01.00]这是 一行 用于 测试 的示例
            [00:01.00]A made-up line for testing
            [00:05.00]下一行同样是虚构的示例
            [00:09.50]第三行：只为检查排版
            [00:14.00]最后一行，没有真实歌词
            """;
        const string mine = """
            [00:00.80]我自己写的 第一行
            [00:04.80]我自己写的 第二行
            """;

        var a = await repo.GetOrCreateTrackAsync("示例曲目一", "虚构乐队", "测试专辑", 201_000, "demo.player");
        var c1 = new LyricsCandidate(LyricsSources.Lrclib, "1000001", "示例曲目一", "虚构乐队", "测试专辑", 201_000, LyricsFormat.Lrc, online, null, false, false);
        var v1 = await repo.SaveCandidateAsync(a.Id, c1, Infrastructure.Data.LyricsFlags.From(parser.Parse(online, LyricsFormat.Lrc)));
        await repo.ConfirmAsync(v1.Id);
        await repo.AddUserVersionAsync(a.Id, "我的版本", LyricsFormat.Lrc, mine, null, Infrastructure.Data.LyricsFlags.From(parser.Parse(mine, LyricsFormat.Lrc)));
        await repo.MarkPlayedAsync(a.Id, "demo.player");

        var b = await repo.GetOrCreateTrackAsync("Placeholder Song", "Nobody Real", "Sample Album", 184_000, "demo.player");
        var c2 = new LyricsCandidate(LyricsSources.Lrclib, "1000002", "Placeholder Song", "Nobody Real", "Sample Album", 184_000, LyricsFormat.Plain, null, null, true, false);
        await repo.SaveCandidateAsync(b.Id, c2, default);

        await repo.GetOrCreateTrackAsync("还没有歌词的曲目", "测试艺人", "", 240_000, "demo.player");
    }
}
