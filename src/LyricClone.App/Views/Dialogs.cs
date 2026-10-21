using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LyricClone.App.Views;

/// <summary>Small modal dialogs built in code: prompt (S17), info (S15), confirm.</summary>
public static class Dialogs
{
    public static string? Prompt(Window owner, string title, string label, string initial)
    {
        var box = new TextBox { Text = initial, MinWidth = 320 };
        var (window, ok) = Build(owner, title, new StackPanel
        {
            Children =
            {
                new TextBlock { Text = label, Style = (Style)owner.FindResource("Text.Body"), Margin = new Thickness(0, 0, 0, 8) },
                box,
            },
        }, "确定", primaryIsDanger: false);
        window.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        return window.ShowDialog() == true && ok() ? box.Text.Trim() : null;
    }

    public static void Info(Window owner, string title, string message)
    {
        var (window, _) = Build(owner, title, new TextBlock
        {
            Text = message,
            Style = (Style)owner.FindResource("Text.Body"),
            MaxWidth = 440,
        }, "知道了", primaryIsDanger: false, showCancel: false);
        window.ShowDialog();
    }

    public static bool Confirm(Window owner, string title, string message, string action, bool danger = false)
    {
        var (window, _) = Build(owner, title, new TextBlock
        {
            Text = message,
            Style = (Style)owner.FindResource("Text.Body"),
            MaxWidth = 440,
        }, action, danger);
        return window.ShowDialog() == true;
    }

    private static (Window Window, Func<bool> Ok) Build(Window owner, string title, UIElement body, string primary, bool primaryIsDanger, bool showCancel = true)
    {
        var window = new Window
        {
            Title = title,
            Owner = owner,
            Style = (Style)owner.FindResource("AppWindow"),
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        bool accepted = false;
        var ok = new Button
        {
            Content = primary,
            IsDefault = true,
            MinWidth = 88,
            Style = (Style)owner.FindResource(primaryIsDanger ? "Button.Danger" : "Button.Primary"),
        };
        ok.Click += (_, _) =>
        {
            accepted = true;
            window.DialogResult = true;
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        if (showCancel)
        {
            var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 88, Margin = new Thickness(0, 0, 8, 0) };
            buttons.Children.Add(cancel);
        }
        else
        {
            ok.IsCancel = true;
        }

        buttons.Children.Add(ok);

        window.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock { Text = title, Style = (Style)owner.FindResource("Text.Subtitle"), Margin = new Thickness(0, 0, 0, 12) },
                body,
                buttons,
            },
        };
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.DialogResult = false;
            }
        };
        return (window, () => accepted);
    }
}
