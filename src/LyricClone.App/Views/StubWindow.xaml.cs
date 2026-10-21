using System.Windows;

namespace LyricClone.App.Views;

public partial class StubWindow : Window
{
    public StubWindow(string title, string screenId, string hint)
    {
        InitializeComponent();
        Title = title;
        Heading.Text = title;
        ScreenId.Text = screenId;
        Hint.Text = hint;
    }
}
