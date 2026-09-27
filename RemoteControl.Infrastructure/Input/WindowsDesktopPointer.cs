using System.Runtime.InteropServices;
using RemoteControl.Core.Input;

namespace RemoteControl.Infrastructure.Input;

public sealed class WindowsDesktopPointer : IDesktopPointer
{
    public DesktopSize Size => OperatingSystem.IsWindows() ? new(GetSystemMetrics(0), GetSystemMetrics(1)) : default;

    public bool TryGetPosition(out PointerPosition position)
    {
        position = default;
        if (!OperatingSystem.IsWindows() || !GetCursorPos(out var point)) return false;
        position = new(point.X, point.Y);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
}
