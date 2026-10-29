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
    internal static void AttachDrag(FrameworkElement handle, Func<FloatingWindow> owner, bool resize = false)
    {
        bool dragging = false;
        FloatingWindow? active = null;
        PointInt32 startCursor = default, startPosition = default;
        SizeInt32 startSize = default;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        void Stop(bool remember = true)
        {
            if (!dragging) return;
            dragging = false; timer.Stop();
            if (remember && active is { _destroying: false }) active.Remember();
            active = null; handle.ReleasePointerCaptures();
        }
        void Move()
        {
            if (!dragging || active is null || !GetCursorPos(out var cursor)) return;
            var dx = cursor.X - startCursor.X; var dy = cursor.Y - startCursor.Y;
            if (resize) active.AppWindow.Resize(new SizeInt32(Math.Max(360, startSize.Width + dx), Math.Max(260, startSize.Height + dy)));
            else active.AppWindow.Move(new PointInt32(startPosition.X + dx, startPosition.Y + dy));
        }
        timer.Tick += (_, _) =>
        {
            if (active is null || active._destroying || !active.IsShown) { Stop(false); return; }
            Move();
            if ((GetAsyncKeyState(1) & 0x8000) == 0) Stop();
        };
        handle.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed || !GetCursorPos(out startCursor)) return;
            active = owner(); startPosition = active.AppWindow.Position; startSize = active.AppWindow.Size;
            dragging = true; handle.CapturePointer(e.Pointer); timer.Start(); e.Handled = true;
            Program.Trace($"Drag press: {active.Title}, resize={resize}, cursor={startCursor.X},{startCursor.Y}, window={startPosition.X},{startPosition.Y}");
        }), true);
        handle.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, e) =>
        {
            if (!dragging) return;
            Move(); e.Handled = true;
        }), true);
        handle.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, e) =>
        {
            if (!dragging) return;
            Move(); Stop(); e.Handled = true;
        }), true);
        // A borderless resize can cross the XAML island before the window grows.
        // Keep following the screen pointer until Windows reports button-up, even
        // if XAML loses its routed capture outside that island.
        handle.PointerCaptureLost += (_, _) => { if ((GetAsyncKeyState(1) & 0x8000) == 0) Stop(); };
        handle.Unloaded += (_, _) => Stop(false);
    }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
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
