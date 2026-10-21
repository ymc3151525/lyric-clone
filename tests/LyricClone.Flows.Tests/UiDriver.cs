using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace LyricClone.Flows.Tests;

/// <summary>Drives modal dialogs from inside their nested message loop, and finds controls by role and text.</summary>
public static class UiDriver
{
    /// <summary>
    /// Arms a handler for the next dialog of type <typeparamref name="T"/>. Call before the click that opens it:
    /// the click blocks inside ShowDialog, and the handler runs in the dialog's message loop.
    /// </summary>
    public static Task WhenDialog<T>(Func<T, Task> act, int timeoutMs = 8000, Func<T, bool>? match = null) where T : Window
    {
        match ??= _ => true;
        var done = new TaskCompletionSource();
        var started = Environment.TickCount64;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        bool running = false;
        timer.Tick += async (_, _) =>
        {
            if (running)
            {
                return;
            }

            var dialog = Application.Current.Windows.OfType<T>().FirstOrDefault(w => w.IsLoaded && match(w));
            if (dialog is null)
            {
                if (Environment.TickCount64 - started > timeoutMs)
                {
                    timer.Stop();
                    done.TrySetException(new TimeoutException($"{typeof(T).Name} never opened"));
                }

                return;
            }

            running = true;
            timer.Stop();
            var closed = new TaskCompletionSource();
            dialog.Closed += (_, _) => closed.TrySetResult();
            try
            {
                await act(dialog);
                if (await Task.WhenAny(closed.Task, Task.Delay(5000)) != closed.Task)
                {
                    throw new TimeoutException($"{typeof(T).Name} did not close after the test's last action");
                }

                done.TrySetResult();
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
                dialog.Close();
            }
        };
        timer.Start();
        return done.Task;
    }

    /// <summary>For code-built dialogs (plain Window) matched by title.</summary>
    public static Task WhenWindowTitled(string title, Func<Window, Task> act) =>
        WhenDialog<Window>(act, match: w => w.Title == title);

    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>([root]);
        var seen = new HashSet<DependencyObject>();
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!seen.Add(node))
            {
                continue;
            }

            if (node is T match)
            {
                yield return match;
            }

            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            {
                queue.Enqueue(child);
            }

            if (node is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                {
                    queue.Enqueue(VisualTreeHelper.GetChild(node, i));
                }
            }
        }
    }

    public static Button ButtonWithText(DependencyObject root, string text) =>
        Descendants<Button>(root).FirstOrDefault(b => b.Content as string == text)
        ?? throw new InvalidOperationException($"No button '{text}'");

    /// <summary>Clicks through UI Automation, which runs Button.OnClick (IsCancel / IsDefault handling included).</summary>
    public static void Press(Button button)
    {
        if (!button.IsEnabled)
        {
            throw new InvalidOperationException($"Button '{button.Content}' is disabled");
        }

        var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(button);
        ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)!).Invoke();
    }

    public static void Menu(Button owner, string header)
    {
        var item = owner.ContextMenu!.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header as string == header)
            ?? throw new InvalidOperationException($"No menu item '{header}'");
        if (!item.IsEnabled || item.Visibility != Visibility.Visible)
        {
            throw new InvalidOperationException($"Menu item '{header}' is not available");
        }

        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
    }
}
