using RemoteControl.Core;
using RemoteControl.Core.Input;

namespace RemoteControl.Tests.Input;

public sealed class InputServiceTests
{
    [Theory]
    [InlineData("ф")]
    [InlineData("é")]
    [InlineData("λ")]
    [InlineData("ſ")]
    [InlineData("UNKNOWN")]
    public async Task InvalidPhysicalKeyDoesNotEmitInput(string key)
    {
        var device = new FakeDevice();
        var result = await Create(device).PressAsync(key, default);
        Assert.Equal("invalid_key", result.Error);
        Assert.Empty(device.KeyboardWrites);
    }

    [Theory]
    [InlineData(130, 127, 3)]
    [InlineData(-130, -127, -3)]
    [InlineData(254, 127, 127)]
    public async Task ScrollPreservesEveryRequestedStep(int requested, int first, int second)
    {
        var device = new FakeDevice();
        var result = await Create(device).ScrollAsync(requested, default);
        Assert.True(result.Ok);
        Assert.Equal(new[] { first, second }, device.MouseWrites.Select(report => (int)report.Wheel));
        Assert.Equal(requested, device.MouseWrites.Sum(report => report.Wheel));
    }

    [Fact]
    public async Task ScrollStopsOnFirstFailedWrite()
    {
        var device = new FakeDevice { MouseWriteSucceeds = _ => false };
        var result = await Create(device).ScrollAsync(300, default);
        Assert.Equal("driver_write_failed", result.Error);
        Assert.Single(device.MouseWrites);
    }

    [Fact]
    public async Task RawMouseFailureHasDriverError()
    {
        var device = new FakeDevice { MouseWriteSucceeds = _ => false };
        var result = await Create(device).RawMouseAsync([3, 0, 0, 0, 0, 0, 0, 0], default);
        Assert.Equal("driver_write_failed", result.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidKeyWriteFailureIsNotReportedAsInvalidKey(bool hotkey)
    {
        var device = new FakeDevice { KeyboardWriteSucceeds = (_, _) => false };
        var service = Create(device);
        var result = hotkey ? await service.HotkeyAsync("CTRL+A", default) : await service.PressAsync("A", default);
        Assert.Equal("driver_write_failed", result.Error);
        Assert.Equal(2, device.KeyboardWrites.Count);
        Assert.Empty(device.KeyboardWrites[^1].Keys);
        Assert.Equal(0, device.KeyboardWrites[^1].Modifiers);
    }

    [Fact]
    public async Task FailedKeyReleaseIsReported()
    {
        var device = new FakeDevice { KeyboardWriteSucceeds = (_, keys) => keys.Length > 0 };
        var result = await Create(device).PressAsync("A", default);
        Assert.Equal("driver_write_failed", result.Error);
    }

    [Fact]
    public async Task TextIsFullyMappedBeforeAnyKeyIsPressed()
    {
        var device = new FakeDevice();
        var service = Create(device, new FakeLayout { CanMap = false });
        var result = await service.TypeAsync("hello🙂", default);
        Assert.Equal("unsupported_character", result.Error);
        Assert.Empty(device.KeyboardWrites);
    }

    [Fact]
    public async Task TextUsesMappedPhysicalKeysAndModifiers()
    {
        var device = new FakeDevice();
        var layout = new FakeLayout { Strokes = [new(4, 2), new(5)] };
        var result = await Create(device, layout).TypeAsync("A\r\nb", default);
        Assert.True(result.Ok);
        Assert.Equal("A\nb", layout.LastText);
        Assert.Collection(device.KeyboardWrites,
            report => { Assert.Equal(2, report.Modifiers); Assert.Equal(new byte[] { 4 }, report.Keys); },
            report => Assert.Empty(report.Keys),
            report => { Assert.Equal(0, report.Modifiers); Assert.Equal(new byte[] { 5 }, report.Keys); },
            report => Assert.Empty(report.Keys));
    }

    [Fact]
    public async Task CancelledKeyHoldStillReleasesKeyboard()
    {
        using var cancellation = new CancellationTokenSource();
        var device = new FakeDevice();
        device.KeyboardWriteSucceeds = (_, keys) => { if (keys.Length > 0) cancellation.Cancel(); return true; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(device).PressAsync("A", cancellation.Token));
        Assert.Equal(2, device.KeyboardWrites.Count);
        Assert.Empty(device.KeyboardWrites[^1].Keys);
    }

    [Fact]
    public async Task CancelledDragStillReleasesMouse()
    {
        using var cancellation = new CancellationTokenSource();
        var device = new FakeDevice();
        device.MouseWriteSucceeds = report => { if ((report.Buttons & 1) != 0) cancellation.Cancel(); return true; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(device).DragAsync(10, 20, 500, 600, cancellation.Token));
        Assert.Contains(device.MouseWrites, report => report.Buttons == 1);
        Assert.Equal(0, device.MouseWrites[^1].Buttons);
        Assert.Equal(0, device.MouseButtons);
    }

    [Fact]
    public async Task FailedDragMovementStillReleasesMouse()
    {
        var device = new FakeDevice { MouseWriteSucceeds = report => !(report.Absolute && report.Buttons == 1) };
        var result = await Create(device).DragAsync(10, 20, 500, 600, default);
        Assert.Equal("driver_write_failed", result.Error);
        Assert.Equal(0, device.MouseWrites[^1].Buttons);
    }

    [Fact]
    public async Task MouseTrajectoryEndsAtRequestedPointAndPreservesOtherHeldButtons()
    {
        var device = new FakeDevice();
        var service = Create(device);
        Assert.True((await service.RawMouseAsync([3, 2, 0, 0, 0, 0, 0, 0], default)).Ok);
        Assert.True((await service.ClickAsync(1919, 1079, default)).Ok);
        var movements = device.MouseWrites.Where(report => report.Absolute).ToArray();
        Assert.True(movements.Length > 1);
        Assert.Equal(short.MaxValue, movements[^1].X);
        Assert.Equal(short.MaxValue, movements[^1].Y);
        Assert.All(movements, report => Assert.Equal(2, report.Buttons));
        Assert.Equal(3, device.MouseWrites[^2].Buttons);
        Assert.Equal(2, device.MouseWrites[^1].Buttons);
    }

    [Fact]
    public async Task CompositeStopsBeforeActionsAfterFailure()
    {
        var device = new FakeDevice();
        var result = await Create(device).SequenceAsync([new("press", Key: "A"), new("press", Key: "UNKNOWN"), new("press", Key: "B")], default);
        Assert.Equal("invalid_key", result.Error);
        Assert.Equal(2, device.KeyboardWrites.Count);
        Assert.Equal(new byte[] { 4 }, device.KeyboardWrites[0].Keys);
    }

    [Fact]
    public async Task CommandsCannotInterleaveKeyDownAndKeyUp()
    {
        var device = new FakeDevice();
        var service = Create(device);
        Task<InputResult>? second = null;
        device.KeyboardWriteSucceeds = (_, keys) =>
        {
            if (keys.SequenceEqual(new byte[] { 4 })) second = service.PressAsync("B", default);
            return true;
        };
        Assert.True((await service.PressAsync("A", default)).Ok);
        Assert.NotNull(second);
        Assert.True((await second).Ok);
        Assert.Equal(new[] { "4", "", "5", "" }, device.KeyboardWrites.Select(report => string.Join(',', report.Keys)));
    }

    [Fact]
    public async Task ReleaseAllAttemptsBothDevicesEvenIfKeyboardFails()
    {
        var device = new FakeDevice { KeyboardWriteSucceeds = (_, _) => false };
        var result = await Create(device).ReleaseAllAsync(default);
        Assert.Equal("driver_write_failed", result.Error);
        Assert.Single(device.KeyboardWrites);
        Assert.Single(device.MouseWrites);
        Assert.Equal(0, device.MouseWrites[0].Buttons);
    }

    [Fact]
    public async Task DisconnectedDeviceDoesNotReceiveInput()
    {
        var device = new FakeDevice { Connected = false };
        var result = await Create(device).PressAsync("A", default);
        Assert.Equal("driver_unavailable", result.Error);
        Assert.Empty(device.KeyboardWrites);
        Assert.Equal("unavailable", (await Create(device).GetStatusAsync(default)).Backend);
    }

    private static InputService Create(FakeDevice device, FakeLayout? layout = null) => new(device, layout ?? new(), new FakePointer(), new FastTimeProvider());

    private sealed class FakeDevice : IInputDevice
    {
        public bool Connected { get; set; } = true;
        public DriverStatus Status => new(Connected, "fake", true, true);
        public byte MouseButtons { get; private set; }
        public List<MouseReport> MouseWrites { get; } = [];
        public List<(byte Modifiers, byte[] Keys)> KeyboardWrites { get; } = [];
        public Func<MouseReport, bool> MouseWriteSucceeds { get; set; } = _ => true;
        public Func<byte, byte[], bool> KeyboardWriteSucceeds { get; set; } = (_, _) => true;
        public bool WriteMouse(MouseReport report)
        {
            MouseWrites.Add(report);
            var success = MouseWriteSucceeds(report);
            if (success) MouseButtons = report.Buttons;
            return success;
        }
        public bool WriteKeyboard(byte modifiers, ReadOnlySpan<byte> keys)
        {
            var copy = keys.ToArray();
            KeyboardWrites.Add((modifiers, copy));
            return KeyboardWriteSucceeds(modifiers, copy);
        }
    }

    private sealed class FakeLayout : IKeyboardLayout
    {
        public bool CanMap { get; set; } = true;
        public string? LastText { get; private set; }
        public KeyStroke[] Strokes { get; set; } = [];
        public bool TryMapText(string text, out KeyStroke[] strokes) { LastText = text; strokes = Strokes; return CanMap; }
    }

    private sealed class FakePointer : IDesktopPointer
    {
        public DesktopSize Size => new(1920, 1080);
        public bool TryGetPosition(out PointerPosition position) { position = new(100, 100); return true; }
    }

    private sealed class FastTimeProvider : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            System.CreateTimer(callback, state, dueTime == Timeout.InfiniteTimeSpan ? dueTime : TimeSpan.Zero, period);
    }
}
