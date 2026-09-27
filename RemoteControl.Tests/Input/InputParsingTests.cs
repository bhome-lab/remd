using RemoteControl.Core.Input;

namespace RemoteControl.Tests.Input;

public sealed class InputParsingTests
{
    [Theory]
    [InlineData(" A ", 4, 0)]
    [InlineData("F1", 58, 0)]
    [InlineData("F12", 69, 0)]
    [InlineData("F13", 104, 0)]
    [InlineData("F24", 115, 0)]
    [InlineData("0", 39, 0)]
    [InlineData("RCTRL", 0, 16)]
    public void PhysicalKeyNamesHaveStableHidCodes(string name, int key, int modifiers)
    {
        Assert.True(KeyParser.TryParse(name, out var stroke));
        Assert.Equal(key, stroke.Key);
        Assert.Equal(modifiers, stroke.Modifiers);
    }

    [Theory]
    [InlineData("CTRL++A")]
    [InlineData("+A")]
    [InlineData("CTRL+")]
    [InlineData("UNKNOWN+A")]
    [InlineData("CTRL+ф")]
    public void InvalidHotkeyIsRejectedBeforeInput(string name) => Assert.False(KeyParser.TryParseHotkey(name, out _, out _));

    [Fact]
    public void CombinedModifiersArePreserved()
    {
        Assert.True(KeyParser.TryParseHotkey("ctrl + rshift + F5", out var keys, out var modifiers));
        Assert.Equal(new byte[] { 62 }, keys);
        Assert.Equal(33, modifiers);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(9)]
    public void MouseReportMustHaveExactlyEightBytes(int length)
    {
        var bytes = new byte[length];
        if (length > 0) bytes[0] = 3;
        Assert.False(RawInputReports.TryReadMouse(bytes, out _));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    public void KeyboardReportMustHaveExactlyNineBytes(int length)
    {
        var bytes = new byte[length];
        bytes[0] = 1;
        Assert.False(RawInputReports.IsKeyboard(bytes));
    }

    [Fact]
    public void ReservedFieldsAndOutOfRangeAbsoluteCoordinatesAreRejected()
    {
        Assert.False(RawInputReports.IsKeyboard([1, 0, 1, 0, 0, 0, 0, 0, 0]));
        Assert.False(RawInputReports.TryReadMouse([3, 32, 0, 0, 0, 0, 0, 0], out _));
        Assert.False(RawInputReports.TryReadMouse([4, 0, 0, 128, 0, 0, 0, 0], out _));
    }

    [Fact]
    public void SignedRelativeFieldsAreDecodedWithoutClamping()
    {
        Assert.True(RawInputReports.TryReadMouse([3, 5, 255, 255, 0, 128, 255, 128], out var report));
        Assert.Equal(new MouseReport(false, 5, -1, short.MinValue, -1, sbyte.MinValue), report);
    }
}
