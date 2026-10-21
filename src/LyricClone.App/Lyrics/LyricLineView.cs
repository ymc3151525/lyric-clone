using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LyricClone.Core.Lyrics;

namespace LyricClone.App.Lyrics;

/// <summary>
/// Draws one lyric line with an optional translation row. Word / syllable lines fill left to right with
/// <see cref="SungBrush"/>; line-only lines are drawn in <see cref="TextBrush"/>. Geometry is cached per
/// line, font and DPI; only the clip moves between frames.
/// </summary>
public sealed class LyricLineView : FrameworkElement
{
    public static readonly DependencyProperty LineProperty = DependencyProperty.Register(
        nameof(Line), typeof(LyricLine), typeof(LyricLineView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutChanged));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(LyricLineView),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutChanged));

    public static readonly DependencyProperty PositionMsProperty = DependencyProperty.Register(
        nameof(PositionMs), typeof(double), typeof(LyricLineView),
        new FrameworkPropertyMetadata(0.0, OnPositionChanged));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(LyricLineView),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(LyricLineView),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SungBrushProperty = DependencyProperty.Register(
        nameof(SungBrush), typeof(Brush), typeof(LyricLineView),
        new FrameworkPropertyMetadata(Brushes.LightSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DimBrushProperty = DependencyProperty.Register(
        nameof(DimBrush), typeof(Brush), typeof(LyricLineView),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TranslationBrushProperty = DependencyProperty.Register(
        nameof(TranslationBrush), typeof(Brush), typeof(LyricLineView),
        new FrameworkPropertyMetadata(Brushes.Silver, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeBrushProperty = DependencyProperty.Register(
        nameof(StrokeBrush), typeof(Brush), typeof(LyricLineView),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OutlineThicknessProperty = DependencyProperty.Register(
        nameof(OutlineThickness), typeof(double), typeof(LyricLineView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowTranslationProperty = DependencyProperty.Register(
        nameof(ShowTranslation), typeof(bool), typeof(LyricLineView),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutChanged));

    public static readonly DependencyProperty KaraokeProperty = DependencyProperty.Register(
        nameof(Karaoke), typeof(bool), typeof(LyricLineView),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutChanged));

    public static readonly DependencyProperty TranslationRatioProperty = DependencyProperty.Register(
        nameof(TranslationRatio), typeof(double), typeof(LyricLineView),
        new FrameworkPropertyMetadata(0.6, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutChanged));

    public static readonly DependencyProperty TranslationGapProperty = DependencyProperty.Register(
        nameof(TranslationGap), typeof(double), typeof(LyricLineView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutChanged));

    public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(
        nameof(TextAlignment), typeof(TextAlignment), typeof(LyricLineView),
        new FrameworkPropertyMetadata(TextAlignment.Center, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MarqueeOffsetProperty = DependencyProperty.Register(
        nameof(MarqueeOffset), typeof(double), typeof(LyricLineView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty = TextElementFontSize();
    public static readonly DependencyProperty FontFamilyProperty = TextElementFontFamily();
    public static readonly DependencyProperty FontWeightProperty = TextElementFontWeight();

    private Layout? _layout;
    private double _lastFilled = -1;

    static LyricLineView()
    {
        SnapsToDevicePixelsProperty.OverrideMetadata(typeof(LyricLineView), new FrameworkPropertyMetadata(true));
    }

    public LyricLine? Line { get => (LyricLine?)GetValue(LineProperty); set => SetValue(LineProperty, value); }

    /// <summary>Shown when <see cref="Line"/> is null, e.g. "title — artist" or a status message.</summary>
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    public double PositionMs { get => (double)GetValue(PositionMsProperty); set => SetValue(PositionMsProperty, value); }

    /// <summary>False for "next line" rows: drawn dim, never filled.</summary>
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    public Brush SungBrush { get => (Brush)GetValue(SungBrushProperty); set => SetValue(SungBrushProperty, value); }

    public Brush DimBrush { get => (Brush)GetValue(DimBrushProperty); set => SetValue(DimBrushProperty, value); }

    public Brush TranslationBrush { get => (Brush)GetValue(TranslationBrushProperty); set => SetValue(TranslationBrushProperty, value); }

    public Brush StrokeBrush { get => (Brush)GetValue(StrokeBrushProperty); set => SetValue(StrokeBrushProperty, value); }

    public double OutlineThickness { get => (double)GetValue(OutlineThicknessProperty); set => SetValue(OutlineThicknessProperty, value); }

    public bool ShowTranslation { get => (bool)GetValue(ShowTranslationProperty); set => SetValue(ShowTranslationProperty, value); }

    /// <summary>When false, word timestamps are ignored and the whole line is drawn as one colour.</summary>
    public bool Karaoke { get => (bool)GetValue(KaraokeProperty); set => SetValue(KaraokeProperty, value); }

    public double TranslationRatio { get => (double)GetValue(TranslationRatioProperty); set => SetValue(TranslationRatioProperty, value); }

    public double TranslationGap { get => (double)GetValue(TranslationGapProperty); set => SetValue(TranslationGapProperty, value); }

    public TextAlignment TextAlignment { get => (TextAlignment)GetValue(TextAlignmentProperty); set => SetValue(TextAlignmentProperty, value); }

    /// <summary>Horizontal shift in dips when the line is wider than the view. 0 shows the start.</summary>
    public double MarqueeOffset { get => (double)GetValue(MarqueeOffsetProperty); set => SetValue(MarqueeOffsetProperty, value); }

    /// <summary>Natural width of the current line (and translation), ignoring the available slot.</summary>
    public double ContentWidth
    {
        get
        {
            var layout = GetLayout();
            return Math.Max(layout.MainWidth, layout.TranslationWidth);
        }
    }

    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var layout = GetLayout();
        double content = Math.Max(layout.MainWidth, layout.TranslationWidth);
        return new Size(Math.Min(content, availableSize.Width), layout.Height);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (sizeInfo.WidthChanged)
        {
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var layout = GetLayout();
        if (layout.Main is null)
        {
            return;
        }

        bool clip = ActualWidth > 0 && ActualHeight > 0;
        if (clip)
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        }

        double mainX = AlignX(layout.MainWidth);
        dc.PushTransform(new TranslateTransform(mainX, 0));

        var pen = OutlineThickness > 0 ? new Pen(StrokeBrush, OutlineThickness * 2) { LineJoin = PenLineJoin.Round } : null;
        if (pen is not null)
        {
            dc.DrawGeometry(null, pen, layout.Main);
        }

        var line = Line;
        bool fillable = Karaoke && IsActive && line is { HasSyllables: true };
        dc.DrawGeometry(IsActive ? (line is null || fillable ? TextBrush : SungBrush) : DimBrush, null, layout.Main);

        if (fillable)
        {
            double filled = FilledWidth(layout, line!, PositionMs);
            _lastFilled = filled;
            if (filled > 0)
            {
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, filled, layout.MainHeight)));
                dc.DrawGeometry(SungBrush, null, layout.Main);
                dc.Pop();
            }
        }

        dc.Pop();

        if (layout.Translation is not null)
        {
            dc.PushTransform(new TranslateTransform(AlignX(layout.TranslationWidth), layout.MainHeight + Math.Max(0, TranslationGap)));
            if (pen is not null)
            {
                dc.DrawGeometry(null, new Pen(StrokeBrush, OutlineThickness * 1.5) { LineJoin = PenLineJoin.Round }, layout.Translation);
            }

            dc.DrawGeometry(IsActive ? TranslationBrush : DimBrush, null, layout.Translation);
            dc.Pop();
        }

        if (clip)
        {
            dc.Pop();
        }
    }

    private double AlignX(double contentWidth)
    {
        double extra = contentWidth - ActualWidth;
        if (extra > 0.5)
        {
            return -Math.Clamp(MarqueeOffset, 0, extra);
        }

        return TextAlignment switch
        {
            TextAlignment.Left => 0,
            TextAlignment.Right => Math.Max(0, ActualWidth - contentWidth),
            _ => Math.Max(0, (ActualWidth - contentWidth) / 2),
        };
    }

    private static double FilledWidth(Layout layout, LyricLine line, double positionMs)
    {
        if (positionMs <= line.Syllables[0].StartMs)
        {
            return 0;
        }

        int chars = 0;
        double fraction = 0;
        foreach (var s in line.Syllables)
        {
            if (positionMs >= s.EndMs)
            {
                chars += s.Text.Length;
                continue;
            }

            if (positionMs > s.StartMs && s.DurationMs > 0)
            {
                fraction = (positionMs - s.StartMs) / s.DurationMs * s.Text.Length;
            }

            break;
        }

        int whole = Math.Min(chars + (int)fraction, layout.PrefixWidths.Length - 1);
        double partial = fraction - Math.Floor(fraction);
        double w = layout.PrefixWidths[whole];
        if (whole + 1 < layout.PrefixWidths.Length)
        {
            w += (layout.PrefixWidths[whole + 1] - w) * partial;
        }

        return w;
    }

    private Layout GetLayout()
    {
        if (_layout is not null)
        {
            return _layout;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
        var line = Line;
        string mainText = line is null ? Placeholder ?? "" : line.HasSyllables ? string.Concat(line.Syllables.Select(s => s.Text)) : line.Text;

        if (string.IsNullOrEmpty(mainText))
        {
            mainText = line is null ? "" : "\u2022 \u2022 \u2022";
        }

        Geometry? main = null;
        double mainWidth = 0, mainHeight = FontSize * 1.3;
        double[] prefix = [0];
        if (mainText.Length > 0)
        {
            var ft = Format(mainText, typeface, FontSize, dpi);
            main = ft.BuildGeometry(new Point(0, 0));
            main.Freeze();
            mainWidth = ft.WidthIncludingTrailingWhitespace;
            mainHeight = ft.Height;

            if (line is { HasSyllables: true })
            {
                prefix = new double[mainText.Length + 1];
                for (int i = 1; i <= mainText.Length; i++)
                {
                    prefix[i] = Format(mainText[..i], typeface, FontSize, dpi).WidthIncludingTrailingWhitespace;
                }
            }
        }

        Geometry? translation = null;
        double trWidth = 0, trHeight = 0;
        if (ShowTranslation && !string.IsNullOrEmpty(line?.Translation))
        {
            var ft = Format(line.Translation, typeface, Math.Max(8, FontSize * TranslationRatio), dpi);
            translation = ft.BuildGeometry(new Point(0, 0));
            translation.Freeze();
            trWidth = ft.WidthIncludingTrailingWhitespace;
            trHeight = ft.Height;
        }

        double gap = translation is null ? 0 : Math.Max(0, TranslationGap);
        _layout = new Layout(main, mainWidth, mainHeight, translation, trWidth, mainHeight + gap + trHeight, prefix);
        return _layout;
    }

    private static FormattedText Format(string text, Typeface typeface, double size, double dpi) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, Brushes.White, dpi);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _layout = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (LyricLineView)d;
        view._layout = null;
        view._lastFilled = -1;
    }

    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (LyricLineView)d;
        if (!view.Karaoke || view.Line is not { HasSyllables: true } line || !view.IsActive)
        {
            return;
        }

        var layout = view.GetLayout();
        double filled = FilledWidth(layout, line, (double)e.NewValue);
        if (Math.Abs(filled - view._lastFilled) >= 0.5)
        {
            view.InvalidateVisual();
        }
    }

    private static DependencyProperty TextElementFontSize() =>
        System.Windows.Documents.TextElement.FontSizeProperty.AddOwner(typeof(LyricLineView),
            new FrameworkPropertyMetadata(32.0, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutChanged));

    private static DependencyProperty TextElementFontFamily() =>
        System.Windows.Documents.TextElement.FontFamilyProperty.AddOwner(typeof(LyricLineView),
            new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutChanged));

    private static DependencyProperty TextElementFontWeight() =>
        System.Windows.Documents.TextElement.FontWeightProperty.AddOwner(typeof(LyricLineView),
            new FrameworkPropertyMetadata(FontWeights.Bold, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutChanged));

    private sealed record Layout(
        Geometry? Main,
        double MainWidth,
        double MainHeight,
        Geometry? Translation,
        double TranslationWidth,
        double Height,
        double[] PrefixWidths);
}
