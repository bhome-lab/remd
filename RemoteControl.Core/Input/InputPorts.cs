namespace RemoteControl.Core.Input;

public readonly record struct KeyStroke(byte Key, byte Modifiers = 0);
public readonly record struct PointerPosition(int X, int Y);
public readonly record struct DesktopSize(int Width, int Height);
public readonly record struct MouseReport(bool Absolute, byte Buttons, short X, short Y, sbyte Wheel = 0, sbyte HorizontalWheel = 0);

public interface IInputDevice
{
    DriverStatus Status { get; }
    byte MouseButtons { get; }
    bool WriteMouse(MouseReport report);
    bool WriteKeyboard(byte modifiers, ReadOnlySpan<byte> keys);
}

public interface IKeyboardLayout
{
    bool TryMapText(string text, out KeyStroke[] strokes);
}

public interface IDesktopPointer
{
    DesktopSize Size { get; }
    bool TryGetPosition(out PointerPosition position);
}
