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
    internal static void AttachDrag(UIElement handle, Func<FloatingWindow> owner, bool resize = false)
    {
        bool dragging = false;
        PointInt32 startCursor = default, startPosition = default;
        SizeInt32 startSize = default;
        handle.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed || !GetCursorPos(out startCursor)) return;
            var window = owner(); startPosition = window.AppWindow.Position; startSize = window.AppWindow.Size;
            dragging = handle.CapturePointer(e.Pointer); e.Handled = dragging;
            Program.Trace($"Drag press: {window.Title}, captured={dragging}, cursor={startCursor.X},{startCursor.Y}, window={startPosition.X},{startPosition.Y}");
        }), true);
        handle.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, e) =>
        {
            if (!dragging || !GetCursorPos(out var cursor)) return;
            var window = owner(); var dx = cursor.X - startCursor.X; var dy = cursor.Y - startCursor.Y;
            if (resize) window.AppWindow.Resize(new SizeInt32(Math.Max(360, startSize.Width + dx), Math.Max(260, startSize.Height + dy)));
            else window.AppWindow.Move(new PointInt32(startPosition.X + dx, startPosition.Y + dy));
            Program.Trace($"Drag move: {window.Title}, delta={dx},{dy}, position={window.AppWindow.Position.X},{window.AppWindow.Position.Y}");
            e.Handled = true;
        }), true);
        handle.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, e) =>
        {
            if (!dragging) return;
            dragging = false; handle.ReleasePointerCapture(e.Pointer); owner().Remember(); e.Handled = true;
        }), true);
        handle.PointerCaptureLost += (_, _) => { if (dragging) { dragging = false; owner().Remember(); } };
    }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointInt32 point);
    private bool _ghost;
    internal void ToggleGhost()
    {
        _ghost = !_ghost;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var style = GetWindowLong(hwnd, -20);
        SetWindowLong(hwnd, -20, _ghost ? style | 0x00080020 : style & ~0x20);
    }
    internal void SetOpacity(double opacity)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x00080000);
        SetLayeredWindowAttributes(hwnd, 0, (byte)(Math.Clamp(opacity, 0.1, 1) * 255), 2);
    }
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
