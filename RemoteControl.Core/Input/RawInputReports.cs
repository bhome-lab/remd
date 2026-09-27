using System.Buffers.Binary;

namespace RemoteControl.Core.Input;

public static class RawInputReports
{
    public static bool TryReadMouse(ReadOnlySpan<byte> bytes, out MouseReport report)
    {
        report = default;
        if (bytes.Length != 8 || bytes[0] is not (3 or 4) || (bytes[1] & ~31) != 0) return false;
        var x = BinaryPrimitives.ReadInt16LittleEndian(bytes[2..4]);
        var y = BinaryPrimitives.ReadInt16LittleEndian(bytes[4..6]);
        if (bytes[0] == 4 && (x < 0 || y < 0)) return false;
        report = new(bytes[0] == 4, bytes[1], x, y, unchecked((sbyte)bytes[6]), unchecked((sbyte)bytes[7]));
        return true;
    }

    public static bool IsKeyboard(ReadOnlySpan<byte> bytes) => bytes.Length == 9 && bytes[0] == 1 && bytes[2] == 0;
}
