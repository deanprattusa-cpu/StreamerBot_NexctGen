using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using StreamerBot.Native.Core;

namespace StreamerBot.Native;

internal sealed class ReviewWindow
{
    internal FloatingWindow Window { get; }
    internal TextBox Editor { get; }
    private readonly TaskCompletionSource<ReviewDecision?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _closed = new();
    private bool _finished;
    internal Task<ReviewDecision?> Completion => _completion.Task;
    internal ReviewWindow(Profile profile, ReviewRequest request, Func<string, CancellationToken, Task<string>> revise)
    {
        Editor = new TextBox { Text = request.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Stretch };
        var grid = new Grid { Margin = new Thickness(12), RowSpacing = 10 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var context = new TextBlock { Text = "Replying to: " + request.Context, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 255, 255)), TextWrapping = TextWrapping.Wrap, MaxHeight = 100 };
        grid.Children.Add(context); Grid.SetRow(Editor, 1); grid.Children.Add(Editor);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; Grid.SetRow(status, 2); grid.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var mode in new[] { "V+T", "V", "T" })
        {
            var button = new Button { Content = "SEND (" + mode + ")", Background = new SolidColorBrush(mode == "V+T" ? Windows.UI.Color.FromArgb(255, 0, 128, 0) : mode == "V" ? Windows.UI.Color.FromArgb(255, 0, 128, 255) : Windows.UI.Color.FromArgb(255, 184, 134, 11)) };
            button.Click += (_, _) => Submit(mode); buttons.Children.Add(button);
        }
        var improve = new Button { Content = "REVISE", Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 85, 0, 170)) }; buttons.Children.Add(improve);
        improve.Click += async (_, _) =>
        {
            improve.IsEnabled = false; status.Text = "Revising…";
            try { var text = await revise(Editor.Text, _closed.Token); if (!_finished) { Editor.Text = text; status.Text = ""; } }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!_finished) status.Text = error.Message; }
            finally { if (!_finished) improve.IsEnabled = true; }
        };
        var reject = new Button { Content = "REJECT", Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 0, 0)) }; reject.Click += (_, _) => Reject(); buttons.Children.Add(reject);
        Grid.SetRow(buttons, 3); grid.Children.Add(new ScrollViewer { Content = buttons, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }.AtRow(3));
        Window = new FloatingWindow(profile, "AI Reply Review", "geo_review", grid, 650, 520, false);
        Window.Closed += (_, _) => { if (!_finished) { _finished = true; _closed.Cancel(); _completion.TrySetResult(null); } _closed.Dispose(); };
        grid.Loaded += (_, _) => Editor.Focus(FocusState.Programmatic);
    }
    internal void Submit(string mode)
    {
        if (_finished || string.IsNullOrWhiteSpace(Editor.Text)) return;
        _finished = true; _closed.Cancel(); _completion.TrySetResult(new ReviewDecision(Editor.Text, mode)); Window.Destroy();
    }
    internal void Reject()
    {
        if (_finished) return;
        _finished = true; _closed.Cancel(); _completion.TrySetResult(null); Window.Destroy();
    }
}
internal static class GridPlacement
{
    internal static T AtRow<T>(this T element, int row) where T : FrameworkElement { Grid.SetRow(element, row); return element; }
}
