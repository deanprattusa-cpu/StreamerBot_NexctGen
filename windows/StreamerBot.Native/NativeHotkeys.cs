using System.Runtime.InteropServices;
using StreamerBot.Native.Core;

namespace StreamerBot.Native;

internal sealed class NativeHotkeys : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Action<string> _pressed;
    private readonly (string Name, KeyChord Chord)[] _bindings;
    private uint _threadId;
    public List<string> Problems { get; } = [];
    public int Count { get; private set; }
    public NativeHotkeys(Profile profile, Action<string> pressed)
    {
        _pressed = pressed;
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
        _thread = new Thread(Run) { IsBackground = true, Name = "Native global hotkeys" };
        _thread.Start(); _ready.Wait();
    }
    private void Run()
    {
        _threadId = GetCurrentThreadId();
        var registered = new List<int>();
        try
        {
            PeekMessage(out _, 0, 0, 0, 0);
            for (var index = 0; index < _bindings.Length; index++)
                if (RegisterHotKey(0, index + 1, _bindings[index].Chord.Modifiers | 0x4000, _bindings[index].Chord.Key)) registered.Add(index + 1);
                else Problems.Add(_bindings[index].Name + ": Windows could not register the key (already in use or reserved).");
            Count = registered.Count; _ready.Set();
            while (GetMessage(out var message, 0, 0, 0) > 0)
                if (message.Id == 0x0312 && message.WParam > 0 && message.WParam <= (nuint)_bindings.Length)
                {
                    GetWindowThreadProcessId(GetForegroundWindow(), out var foreground);
                    // The UI owner checks focus on editable controls, including detached windows.
                    _pressed(_bindings[(int)message.WParam - 1].Name);
                }
        }
        finally { foreach (var id in registered) UnregisterHotKey(0, id); _ready.Set(); }
    }
    public void Dispose() { if (_thread.IsAlive) { PostThreadMessage(_threadId, 0x12, 0, 0); _thread.Join(2000); } if (!_thread.IsAlive) _ready.Dispose(); }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X; public int Y; public uint Private; }
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
