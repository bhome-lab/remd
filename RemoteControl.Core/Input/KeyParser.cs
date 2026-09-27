namespace RemoteControl.Core.Input;

/// <summary>Имена физических HID-клавиш; преобразование текста выполняет IKeyboardLayout.</summary>
public static class KeyParser
{
    private static readonly Dictionary<string, byte> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ENTER"] = 40, ["RETURN"] = 40, ["ESC"] = 41, ["ESCAPE"] = 41,
        ["BACKSPACE"] = 42, ["TAB"] = 43, ["SPACE"] = 44,
        ["MINUS"] = 45, ["EQUAL"] = 46, ["LEFTBRACE"] = 47, ["RIGHTBRACE"] = 48,
        ["BACKSLASH"] = 49, ["NONUSHASH"] = 50, ["SEMICOLON"] = 51,
        ["APOSTROPHE"] = 52, ["GRAVE"] = 53, ["COMMA"] = 54, ["DOT"] = 55, ["SLASH"] = 56,
        ["CAPSLOCK"] = 57, ["PRINTSCREEN"] = 70, ["SYSRQ"] = 70, ["SCROLLLOCK"] = 71,
        ["PAUSE"] = 72, ["INSERT"] = 73, ["HOME"] = 74, ["PAGEUP"] = 75,
        ["DELETE"] = 76, ["DEL"] = 76, ["END"] = 77, ["PAGEDOWN"] = 78,
        ["RIGHT"] = 79, ["LEFT"] = 80, ["DOWN"] = 81, ["UP"] = 82,
        ["NUMLOCK"] = 83, ["KPSLASH"] = 84, ["KPASTERISK"] = 85, ["KPMINUS"] = 86,
        ["KPPLUS"] = 87, ["KPENTER"] = 88, ["KPDOT"] = 99, ["NONUSBACKSLASH"] = 100,
        ["COMPOSE"] = 101, ["APPLICATION"] = 101, ["POWER"] = 102, ["KPEQUAL"] = 103,
        ["EXECUTE"] = 116, ["HELP"] = 117, ["MENU"] = 118, ["SELECT"] = 119,
        ["STOP"] = 120, ["AGAIN"] = 121, ["UNDO"] = 122, ["CUT"] = 123,
        ["COPY"] = 124, ["PASTE"] = 125, ["FIND"] = 126, ["MUTE"] = 127,
        ["VOLUMEUP"] = 128, ["VOLUMEDOWN"] = 129
    };

    private static readonly Dictionary<string, byte> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CTRL"] = 1, ["CONTROL"] = 1, ["LCTRL"] = 1, ["LEFTCTRL"] = 1,
        ["SHIFT"] = 2, ["LSHIFT"] = 2, ["LEFTSHIFT"] = 2,
        ["ALT"] = 4, ["LALT"] = 4, ["LEFTALT"] = 4,
        ["WIN"] = 8, ["LWIN"] = 8, ["LEFTMETA"] = 8,
        ["RCTRL"] = 16, ["RIGHTCTRL"] = 16,
        ["RSHIFT"] = 32, ["RIGHTSHIFT"] = 32,
        ["RALT"] = 64, ["RIGHTALT"] = 64,
        ["RWIN"] = 128, ["RIGHTMETA"] = 128
    };

    public static IEnumerable<string> NamedKeyNames => NamedKeys.Keys;
    public static IEnumerable<string> ModifierNames => Modifiers.Keys;

    public static bool TryParse(KeyInput input, out KeyStroke stroke)
    {
        stroke = default;
        if (input.Usage is byte direct) { stroke = FromUsage(direct); return true; }
        if (input.Text is not string text) return false;
        // A literal space/tab/newline is a key, not formatting to trim.
        if (text.Length == 1 && TryParseAscii(text[0], out stroke)) return true;
        var key = text.Trim();
        if (key.Length == 0 || key.Any(character => character > 127)) return false;
        if (key.Length == 1 && TryParseAscii(key[0], out stroke)) return true;
        if (key.Length == 4 && key.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && byte.TryParse(key.AsSpan(2), System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture, out var usage))
        {
            stroke = FromUsage(usage);
            return true;
        }
        if (Modifiers.TryGetValue(key, out var modifier)) { stroke = new(0, modifier); return true; }
        key = key.ToUpperInvariant();
        if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out var function) && function is >= 1 and <= 24)
            usage = (byte)(function <= 12 ? 57 + function : 91 + function);
        else if (key.StartsWith("KP", StringComparison.Ordinal) && key.Length == 3 && key[2] is >= '0' and <= '9')
            usage = key[2] == '0' ? (byte)98 : (byte)(89 + key[2] - '1');
        else if (!NamedKeys.TryGetValue(key, out usage)) return false;
        stroke = new(usage);
        return true;
    }

    public static bool TryParseHotkey(KeyChord chord, out byte[] keys, out byte modifiers)
    {
        keys = [];
        modifiers = 0;
        var items = chord.Items;
        if (chord.Shortcut is string shortcut)
            items = shortcut.Split('+', StringSplitOptions.TrimEntries).Select(KeyInput.FromText).ToArray();
        if (items is null || items.Length == 0) return false;
        var usages = new List<byte>(6);
        foreach (var item in items)
        {
            if (!TryParse(item, out var stroke)) return false;
            modifiers |= stroke.Modifiers;
            if (stroke.Key != 0 && !usages.Contains(stroke.Key)) usages.Add(stroke.Key);
            if (usages.Count > 6) return false;
        }
        keys = usages.ToArray();
        return true;
    }

    private static KeyStroke FromUsage(byte usage) => usage is >= 0xE0 and <= 0xE7
        ? new(0, (byte)(1 << (usage - 0xE0)))
        : new(usage);

    private static bool TryParseAscii(char character, out KeyStroke stroke)
    {
        stroke = default;
        if (character is >= 'A' and <= 'Z') character = (char)(character + ('a' - 'A'));
        if (character is >= 'a' and <= 'z') { stroke = new((byte)(4 + character - 'a')); return true; }
        if (character is >= '1' and <= '9') { stroke = new((byte)(30 + character - '1')); return true; }
        if (character == '0') { stroke = new(39); return true; }
        var control = character switch { '\b' => 42, '\t' => 43, '\n' or '\r' => 40, '\x1b' => 41, '\x7f' => 76, ' ' => 44, _ => 0 };
        if (control != 0) { stroke = new((byte)control); return true; }
        var digit = "!@#$%^&*()".IndexOf(character);
        if (digit >= 0) { stroke = new((byte)(digit == 9 ? 39 : 30 + digit), 2); return true; }
        const string plain = "-=[]\\;'\u0060,./";
        const string shifted = "_+{}|:\"~<>?";
        var index = plain.IndexOf(character);
        var shift = false;
        if (index < 0) { index = shifted.IndexOf(character); shift = true; }
        if (index < 0) return false;
        stroke = new((byte)(index < 5 ? 45 + index : 46 + index), (byte)(shift ? 2 : 0));
        return true;
    }
}
