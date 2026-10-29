using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using StreamerBot.Native.Core;

namespace StreamerBot.Native;

internal sealed partial class NativeWindow
{
    private async Task SmokeKeyboardAsync()
    {
        var profile = new Profile(Path.Combine(Program.DataRoot, "keyboard-fixture.json"));
        foreach (var key in Catalog.SmartKeys.Concat(Catalog.SystemKeys)) profile.Set(key, "");
        profile.Set("hk_yt_play_pause", "e");
        var events = 0;
        var original = _game.Text;
        using (var observer = new NativeHotkeys(profile, _ => Interlocked.Increment(ref events)))
        {
            if (observer.Count != 1) throw new IOException("Keyboard observer failed: " + string.Join("; ", observer.Problems));
            Activate(); _game.Text = ""; _game.Focus(FocusState.Programmatic);
            await Task.Delay(150);
            TypeE(); await Task.Delay(150);
            if (!_game.Text.Equals("e", StringComparison.OrdinalIgnoreCase) || events != 1) throw new IOException("Registered bare E did not pass through to native text input.");
            // Ignoring actions (the UI's suspended state) must not interfere with delivery.
            _suspended = true; TypeE(); await Task.Delay(150); _suspended = false;
            if (!_game.Text.Equals("ee", StringComparison.OrdinalIgnoreCase) || events != 2) throw new IOException("Suspended shortcut blocked E typing.");
        }
        TypeE(); await Task.Delay(150);
        if (!_game.Text.Equals("eee", StringComparison.OrdinalIgnoreCase) || events != 2) throw new IOException("Keyboard observer was not released on disposal.");
        // Rebinding must remove the old observer rather than accumulating callbacks.
        using (var observer = new NativeHotkeys(profile, _ => Interlocked.Increment(ref events)))
        {
            TypeE(); await Task.Delay(150);
            if (!_game.Text.Equals("eeee", StringComparison.OrdinalIgnoreCase) || events != 3) throw new IOException("Rebinding shortcuts duplicated callbacks or blocked typing.");
        }
        _game.Text = original;
        File.WriteAllText(Path.Combine(Program.DataRoot, "keyboard-ready.txt"), "E types while observed and suspended; disposal releases callbacks; rebinding does not duplicate them.");
    }
    private async Task SmokeOverlayDragAsync(string key)
    {
        var window = _toolWindows[key];
        window.AppWindow.Move(new Windows.Graphics.PointInt32(30, 30));
        window.Show(); await Task.Delay(150);
        var start = window.AppWindow.Position;
        var header = OverlayPoint(window, "OverlayDragHandle");
        SetCursorPos(header.X, header.Y); await Task.Delay(100);
        MouseButton(true); await Task.Delay(100);
        SetCursorPos(header.X + 50, header.Y + 40); await Task.Delay(150);
        MouseButton(false); await Task.Delay(100);
        var moved = window.AppWindow.Position;
        if (Math.Abs(moved.X - start.X - 50) > 3 || Math.Abs(moved.Y - start.Y - 40) > 3) throw new IOException(key + " overlay header did not drag to the pointer position.");
        window.Hide();
        var geometry = _profile.Text(key == "chat" ? "geo_selector" : "geo_guide");
        window.Show(); await Task.Delay(100);
        if (window.AppWindow.Position.X != moved.X || window.AppWindow.Position.Y != moved.Y || geometry.Length == 0) throw new IOException(key + " overlay lost its dragged position after reopen.");
        if (key == "chat")
        {
            var size = window.AppWindow.Size;
            var grip = OverlayPoint(window, "OverlayResizeGrip");
            var x = grip.X; var y = grip.Y;
            SetCursorPos(x, y); await Task.Delay(80); MouseButton(true); await Task.Delay(80);
            SetCursorPos(x + 35, y + 20); await Task.Delay(150); MouseButton(false); await Task.Delay(100);
            if (window.AppWindow.Size.Width <= size.Width || window.AppWindow.Size.Height <= size.Height) throw new IOException("Chat overlay resize grip did not resize.");
        }
    }
    private static Windows.Graphics.PointInt32 OverlayPoint(FloatingWindow window, string name)
    {
        FrameworkElement? Find(DependencyObject parent)
        {
            if (parent is FrameworkElement element && element.Name == name) return element;
            for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
                if (Find(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i)) is FrameworkElement found) return found;
            return null;
        }
        var handle = Find(window.Root) ?? throw new IOException("Overlay drag handle did not load.");
        var location = handle.TransformToVisual(window.Root).TransformPoint(new Windows.Foundation.Point(handle.ActualWidth / 2, handle.ActualHeight / 2));
        var scale = window.Root.XamlRoot.RasterizationScale; var position = window.AppWindow.Position;
        return new Windows.Graphics.PointInt32(position.X + (int)(location.X * scale), position.Y + (int)(location.Y * scale));
    }
    private static void MouseButton(bool down)
    {
        var input = new[] { new KeyboardInput { Type = 0, MouseFlags = down ? 2u : 4u } };
        if (SendInput(1, input, Marshal.SizeOf<KeyboardInput>()) != 1) throw new IOException("Mouse SendInput failed for overlay drag smoke.");
    }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    private static void TypeE()
    {
        var input = new[] { new KeyboardInput { Type = 1, Key = 0x45 }, new KeyboardInput { Type = 1, Key = 0x45, Flags = 2 } };
        if (SendInput((uint)input.Length, input, Marshal.SizeOf<KeyboardInput>()) != input.Length) throw new IOException("Windows SendInput failed for keyboard smoke.");
    }
    // INPUT is 40 bytes on x64: DWORD, alignment padding, then the 32-byte input union.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct KeyboardInput
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ushort Key;
        [FieldOffset(10)] public ushort Scan;
        [FieldOffset(12)] public uint Flags;
        [FieldOffset(20)] public uint MouseFlags;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, KeyboardInput[] inputs, int size);
}
