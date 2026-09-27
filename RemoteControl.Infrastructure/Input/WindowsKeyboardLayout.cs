using System.Runtime.InteropServices;
using RemoteControl.Core.Input;

namespace RemoteControl.Infrastructure.Input;

public sealed class WindowsKeyboardLayout : IKeyboardLayout
{
    // Scan code сохраняет физическую клавишу при перестановке virtual keys, например немецких Y/Z.
    private static readonly Dictionary<uint, byte> ScanToUsage = new()
    {
        [0x1E] = 4, [0x30] = 5, [0x2E] = 6, [0x20] = 7, [0x12] = 8, [0x21] = 9,
        [0x22] = 10, [0x23] = 11, [0x17] = 12, [0x24] = 13, [0x25] = 14, [0x26] = 15,
        [0x32] = 16, [0x31] = 17, [0x18] = 18, [0x19] = 19, [0x10] = 20, [0x13] = 21,
        [0x1F] = 22, [0x14] = 23, [0x16] = 24, [0x2F] = 25, [0x11] = 26, [0x2D] = 27,
        [0x15] = 28, [0x2C] = 29,
        [0x02] = 30, [0x03] = 31, [0x04] = 32, [0x05] = 33, [0x06] = 34,
        [0x07] = 35, [0x08] = 36, [0x09] = 37, [0x0A] = 38, [0x0B] = 39,
        [0x1C] = 40, [0x01] = 41, [0x0E] = 42, [0x0F] = 43, [0x39] = 44,
        [0x0C] = 45, [0x0D] = 46, [0x1A] = 47, [0x1B] = 48, [0x2B] = 49,
        [0x27] = 51, [0x28] = 52, [0x29] = 53, [0x33] = 54, [0x34] = 55,
        [0x35] = 56, [0x56] = 100
    };

    public bool TryMapText(string text, out KeyStroke[] strokes)
    {
        strokes = [];
        if (!OperatingSystem.IsWindows()) return false;
        var thread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var layout = GetKeyboardLayout(thread);
        var mapped = new KeyStroke[text.Length];
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character is '\r' or '\n') { mapped[index] = new(40); continue; }
            if (character == '\t') { mapped[index] = new(43); continue; }
            var combination = VkKeyScanEx(character, layout);
            if (combination == -1) return false;
            var scan = MapVirtualKeyEx((uint)(combination & 0xff), 4, layout);
            if (!ScanToUsage.TryGetValue(scan, out var usage)) return false;
            var shift = (combination >> 8) & 7;
            var modifiers = (byte)(((shift & 1) != 0 ? 2 : 0) | ((shift & 2) != 0 ? 1 : 0) | ((shift & 4) != 0 ? 4 : 0));
            mapped[index] = new(usage, modifiers);
        }
        strokes = mapped;
        return true;
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", EntryPoint = "VkKeyScanExW", CharSet = CharSet.Unicode)] private static extern short VkKeyScanEx(char character, nint layout);
    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyExW")] private static extern uint MapVirtualKeyEx(uint code, uint mapType, nint layout);
}
