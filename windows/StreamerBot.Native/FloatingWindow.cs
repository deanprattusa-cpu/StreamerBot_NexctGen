using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using StreamerBot.Native.Core;
using Windows.Graphics;

namespace StreamerBot.Native;

internal sealed class FloatingWindow : Window
{
    private readonly Profile _profile;
    private readonly string _geometryKey;
    private readonly bool _hideOnClose;
    private bool _destroying;
    internal Grid Root { get; }
    internal bool IsShown => AppWindow.IsVisible;
    internal event Action? Dismissed;
    internal FloatingWindow(Profile profile, string title, string geometryKey, UIElement content, int width, int height, bool hideOnClose = true, bool borderless = false)
    {
        _profile = profile; _geometryKey = geometryKey; _hideOnClose = hideOnClose;
        Title = title;
        Root = new Grid { RequestedTheme = ElementTheme.Dark, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 30)) };
        Root.Children.Add(content); Content = Root;
        AppWindow.Resize(new SizeInt32(width, height));
        if (AppWindow.Presenter is OverlappedPresenter presenter) { presenter.IsAlwaysOnTop = true; if (borderless) presenter.SetBorderAndTitleBar(false, false); }
        var match = Regex.Match(profile.Text(geometryKey), @"^(\d+)x(\d+)([+-]\d+)([+-]\d+)$");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var w) && int.TryParse(match.Groups[2].Value, out var h) && int.TryParse(match.Groups[3].Value, out var x) && int.TryParse(match.Groups[4].Value, out var y))
        {
            var area = DisplayArea.GetFromPoint(new PointInt32(x, y), DisplayAreaFallback.Nearest).WorkArea;
            w = Math.Clamp(w, Math.Min(360, area.Width), area.Width); h = Math.Clamp(h, Math.Min(260, area.Height), area.Height);
            AppWindow.MoveAndResize(new RectInt32(Math.Clamp(x, area.X, area.X + area.Width - w), Math.Clamp(y, area.Y, area.Y + area.Height - h), w, h));
        }
        AppWindow.Closing += (_, args) =>
        {
            Remember();
            if (_hideOnClose && !_destroying) { args.Cancel = true; AppWindow.Hide(); Dismissed?.Invoke(); }
        };
    }
    internal void Show()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter) { presenter.IsAlwaysOnTop = true; presenter.Restore(); }
        AppWindow.Show(); Activate();
    }
    internal void Hide() { Remember(); AppWindow.Hide(); Dismissed?.Invoke(); }
    internal void Destroy() { _destroying = true; Close(); }
    private void Remember()
    {
        var p = AppWindow.Position; var s = AppWindow.Size;
        _profile.Set(_geometryKey, $"{s.Width}x{s.Height}{p.X:+0;-0;+0}{p.Y:+0;-0;+0}");
        // Persist from the normal profile save/owner close path; never write during UI teardown.
    }
    internal void Drag(bool resize = false)
    {
        ReleaseCapture(); SendMessage(WinRT.Interop.WindowNative.GetWindowHandle(this), 0x00A1, resize ? 17 : 2, 0);
    }
    internal void SetOpacity(double opacity)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x00080000);
        SetLayeredWindowAttributes(hwnd, 0, (byte)(Math.Clamp(opacity, 0.1, 1) * 255), 2);
    }
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, uint message, nint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(nint hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint color, byte alpha, uint flags);
    internal static bool IsForeground(Window window) => GetForegroundWindow() == WinRT.Interop.WindowNative.GetWindowHandle(window);
    internal static bool IsEditing(Window window, FrameworkElement root)
    {
        if (GetForegroundWindow() != WinRT.Interop.WindowNative.GetWindowHandle(window) || root.XamlRoot is null) return false;
        return FocusManager.GetFocusedElement(root.XamlRoot) is TextBox { IsReadOnly: false } or PasswordBox or NumberBox or ComboBox;
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
