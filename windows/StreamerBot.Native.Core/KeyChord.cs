namespace StreamerBot.Native.Core;

public sealed record KeyChord(uint Modifiers, uint Key)
{
    public static bool TryParse(string text, out KeyChord? chord)
    {
        chord = null;
        uint modifiers = 0, key = 0;
        foreach (var part in text.ToLowerInvariant().Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part)
            {
                case "ctrl": case "control": modifiers |= 2; continue;
                case "alt": modifiers |= 1; continue;
                case "shift": modifiers |= 4; continue;
                case "win": case "windows": modifiers |= 8; continue;
            }
            if (key != 0) return false;
            if (part.Length == 1 && char.IsAsciiLetterOrDigit(part[0])) key = char.ToUpperInvariant(part[0]);
            else if (part.StartsWith('f') && int.TryParse(part.AsSpan(1), out var function) && function is >= 1 and <= 24) key = (uint)(0x6F + function);
            else key = part switch { "space" => 0x20, "enter" or "return" => 0x0D, "tab" => 0x09, "esc" or "escape" => 0x1B, "backspace" => 0x08, "delete" or "del" => 0x2E, "insert" => 0x2D, "home" => 0x24, "end" => 0x23, "page up" or "pageup" => 0x21, "page down" or "pagedown" => 0x22, "up" => 0x26, "down" => 0x28, "left" => 0x25, "right" => 0x27, "-" => 0xBD, "=" => 0xBB, "[" => 0xDB, "]" => 0xDD, ";" => 0xBA, "'" => 0xDE, "," => 0xBC, "." => 0xBE, "/" => 0xBF, "\\" => 0xDC, "`" => 0xC0, "pause" => 0x13, "caps lock" => 0x14, _ => 0 };
            if (key == 0) return false;
        }
        if (key == 0) return false;
        chord = new KeyChord(modifiers, key); return true;
    }
}
