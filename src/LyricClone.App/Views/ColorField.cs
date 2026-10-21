using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LyricClone.Core.Settings;

namespace LyricClone.App.Views;

/// <summary>Preset swatches, a hex box and a "default" button for one colour role. Null means the theme colour.</summary>
public sealed class ColorField : UserControl
{
    public static readonly string[] Presets = ["#FFFFFF", "#FFD54F", "#7FDBFF", "#A5F3C3", "#FF8FB1", "#B39DDB", "#000000"];

    private readonly TextBox _hex;
    private readonly Border _preview;
    private string? _value;
    private Brush _defaultBrush = Brushes.Transparent;

    public ColorField(string label)
    {
        Label = label;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var hex in Presets)
        {
            var swatch = new Button
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = Cursors.Hand,
                ToolTip = hex,
                Tag = hex,
                Template = SwatchTemplate(),
                Background = new SolidColorBrush(ToColor(hex)),
            };
            AutomationProperties.SetName(swatch, $"{label} {hex}");
            swatch.Click += (_, _) => Commit(hex);
            row.Children.Add(swatch);
        }

        _preview = new Border
        {
            Width = 22,
            Height = 22,
            Margin = new Thickness(8, 0, 6, 0),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
        };
        _preview.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        row.Children.Add(_preview);

        _hex = new TextBox { Width = 96, VerticalContentAlignment = VerticalAlignment.Center };
        _hex.SetResourceReference(Control.FontFamilyProperty, "Font.Mono");
        AutomationProperties.SetName(_hex, $"{label} 颜色值");
        _hex.LostKeyboardFocus += (_, _) => CommitText();
        _hex.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitText();
            }
        };
        row.Children.Add(_hex);

        var reset = new Button { Content = "默认", Margin = new Thickness(6, 0, 0, 0) };
        reset.SetResourceReference(StyleProperty, "Button.Subtle");
        AutomationProperties.SetName(reset, $"{label} 恢复默认");
        reset.Click += (_, _) => Commit(null);
        row.Children.Add(reset);

        Content = row;
        Refresh();
    }

    public string Label { get; }

    /// <summary>Raised with the normalised value ("#RRGGBB" or null) when the user picks or types a colour.</summary>
    public event Action<string?>? Committed;

    public string? Value
    {
        get => _value;
        set
        {
            _value = ColorHex.Normalize(value);
            Refresh();
        }
    }

    /// <summary>Shown in the preview while <see cref="Value"/> is null.</summary>
    public Brush DefaultBrush
    {
        get => _defaultBrush;
        set
        {
            _defaultBrush = value;
            Refresh();
        }
    }

    public static Color ToColor(string hex)
    {
        ColorHex.TryParse(hex, out var v);
        return Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    private void CommitText()
    {
        var text = _hex.Text.Trim();
        if (text.Length == 0)
        {
            Commit(null);
            return;
        }

        if (ColorHex.Normalize(text) is { } normalized)
        {
            Commit(normalized);
        }
        else
        {
            Refresh();
        }
    }

    private void Commit(string? value)
    {
        var normalized = ColorHex.Normalize(value);
        if (normalized == _value)
        {
            Refresh();
            return;
        }

        _value = normalized;
        Refresh();
        Committed?.Invoke(normalized);
    }

    private void Refresh()
    {
        _hex.Text = _value ?? "";
        _preview.Background = _value is null ? _defaultBrush : new SolidColorBrush(ToColor(_value));
        _preview.ToolTip = _value ?? "跟随主题";
    }

    private static ControlTemplate SwatchTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }
}
