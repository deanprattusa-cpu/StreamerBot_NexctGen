using System.ComponentModel;
using System.Runtime.InteropServices;
using StreamerBot.Native.Core;

namespace StreamerBot.Native;

/// <summary>Observes shortcuts without reserving or suppressing Windows keyboard input.</summary>
internal sealed class NativeHotkeys : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Action<string> _pressed;
    private readonly (string Name, KeyChord Chord)[] _bindings;
    private readonly KeyboardHook _callback;
    private readonly HashSet<uint> _down = [];
    private uint _threadId;
    private nint _hook;
    private int _disposed;
    public List<string> Problems { get; } = [];
    public int Count { get; private set; }
    public NativeHotkeys(Profile profile, Action<string> pressed)
    {
        _pressed = pressed; _callback = Observe;
        var list = new List<(string, KeyChord)>();
        var used = new HashSet<KeyChord>();
        foreach (var name in Catalog.SmartKeys.Concat(Catalog.SystemKeys).Distinct())
        {
            var value = profile.Text(name);
            if (value.Length == 0) continue;
            if (!KeyChord.TryParse(value, out var chord)) { Problems.Add(name + ": unsupported key expression " + value); continue; }
            if (!used.Add(chord!)) { Problems.Add(name + ": duplicate binding " + value); continue; }
            list.Add((name, chord!));
        }
        _bindings = list.ToArray();
        _thread = new Thread(Run) { IsBackground = true, Name = "Native keyboard observer" };
        _thread.Start(); _ready.Wait();
    }
    private void Run()
    {
        _threadId = GetCurrentThreadId();
        try
        {
            PeekMessage(out _, 0, 0, 0, 0);
            _hook = SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0); // WH_KEYBOARD_LL
            if (_hook == 0) { Problems.Add("Keyboard shortcuts unavailable: " + new Win32Exception(Marshal.GetLastWin32Error()).Message); return; }
            Count = _bindings.Length; _ready.Set();
            while (GetMessage(out _, 0, 0, 0) > 0) { }
        }
        finally
        {
            if (_hook != 0) { UnhookWindowsHookEx(_hook); _hook = 0; }
            Count = 0; _ready.Set();
        }
    }
    private nint Observe(int code, nuint message, nint data)
    {
        // Always forward every event, including injected/onscreen input and suspended shortcuts.
        // Keep this callback short: the UI callback only enqueues work on its DispatcherQueue.
        try
        {
            if (code >= 0 && Volatile.Read(ref _disposed) == 0)
            {
                var key = (uint)Marshal.ReadInt32(data);
                if (message is 0x0101 or 0x0105) _down.Remove(key);
                else if (message is 0x0100 or 0x0104 && _down.Add(key))
                {
                    uint modifiers = 0;
                    if (Held(0x12)) modifiers |= 1;
                    if (Held(0x11)) modifiers |= 2;
                    if (Held(0x10)) modifiers |= 4;
                    if (Held(0x5B) || Held(0x5C)) modifiers |= 8;
                    foreach (var binding in _bindings)
                        if (binding.Chord.Key == key && binding.Chord.Modifiers == modifiers) _pressed(binding.Name);
                }
            }
        }
        catch { /* A shortcut failure must never interrupt keyboard delivery. */ }
        return CallNextHookEx(_hook, code, message, data);
    }
    private static bool Held(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_thread.IsAlive)
        {
            if (!PostThreadMessage(_threadId, 0x12, 0, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not stop keyboard shortcuts.");
            if (!_thread.Join(5000)) throw new TimeoutException("Keyboard observer did not stop.");
        }
        _ready.Dispose();
    }
    private delegate nint KeyboardHook(int code, nuint message, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X; public int Y; public uint Private; }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int hook, KeyboardHook callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nuint message, nint data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostThreadMessage(uint thread, uint message, nuint wparam, nint lparam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
