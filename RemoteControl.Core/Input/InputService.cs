namespace RemoteControl.Core.Input;

/// <summary>Последовательные пользовательские действия поверх HID-устройства.</summary>
public sealed class InputService(IInputDevice device, IKeyboardLayout layout, IDesktopPointer pointer, TimeProvider? timeProvider = null) : IInputService
{
    private const string Backend = "fakerinput";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public Task<InputStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = device.Status;
        return Task.FromResult(new InputStatus(status.Connected ? Backend : "unavailable", status));
    }

    public Task<InputResult> ClickAsync(int x, int y, CancellationToken cancellationToken) => ExecuteAsync(() => ClickCoreAsync(x, y, cancellationToken), cancellationToken);
    public Task<InputResult> DoubleClickAsync(int x, int y, CancellationToken cancellationToken) => ExecuteAsync(() => DoubleClickCoreAsync(x, y, cancellationToken), cancellationToken);
    public Task<InputResult> TypeAsync(string text, CancellationToken cancellationToken) => ExecuteAsync(() => TypeCoreAsync(text, cancellationToken), cancellationToken);
    public Task<InputResult> PressAsync(KeyInput key, CancellationToken cancellationToken) => ExecuteAsync(() => PressCoreAsync(key, cancellationToken), cancellationToken);
    public Task<InputResult> HotkeyAsync(KeyChord keys, CancellationToken cancellationToken) => ExecuteAsync(() => HotkeyCoreAsync(keys, cancellationToken), cancellationToken);
    public Task<InputResult> ScrollAsync(int delta, CancellationToken cancellationToken) => ExecuteAsync(() => ScrollCoreAsync(delta, cancellationToken), cancellationToken);
    public Task<InputResult> DragAsync(int x1, int y1, int x2, int y2, CancellationToken cancellationToken) => ExecuteAsync(() => DragCoreAsync(x1, y1, x2, y2, cancellationToken), cancellationToken);
    public Task<InputResult> SequenceAsync(InputAction[] actions, CancellationToken cancellationToken) => ExecuteAsync(() => SequenceCoreAsync(actions, cancellationToken), cancellationToken);

    public Task<InputResult> RawMouseAsync(byte[] report, CancellationToken cancellationToken) => ExecuteAsync(() =>
        Task.FromResult(RawInputReports.TryReadMouse(report, out var mouse) ? WriteResult(device.WriteMouse(mouse)) : Failure("invalid_report")), cancellationToken);

    public Task<InputResult> RawKeyboardAsync(byte[] report, CancellationToken cancellationToken) => ExecuteAsync(() =>
        Task.FromResult(RawInputReports.IsKeyboard(report) ? WriteResult(device.WriteKeyboard(report[1], report.AsSpan(3, 6))) : Failure("invalid_report")), cancellationToken);

    public Task<InputResult> ReleaseAllAsync(CancellationToken cancellationToken) => ExecuteAsync(() =>
    {
        var keyboard = device.WriteKeyboard(0, []);
        var mouse = device.WriteMouse(new(false, 0, 0, 0));
        return Task.FromResult(WriteResult(keyboard && mouse));
    }, cancellationToken);

    private async Task<InputResult> ExecuteAsync(Func<Task<InputResult>> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!device.Status.Connected) return Failure("driver_unavailable");
            cancellationToken.ThrowIfCancellationRequested();
            return await action();
        }
        finally { _gate.Release(); }
    }

    private async Task<InputResult> ClickCoreAsync(int x, int y, CancellationToken cancellationToken)
    {
        if (!await MoveAsync(x, y, cancellationToken)) return Failure("driver_write_failed");
        await DelayAsync(35, cancellationToken);
        var pressed = false;
        var released = false;
        try
        {
            pressed = WriteButton(true);
            if (pressed) await DelayAsync(45, cancellationToken);
        }
        finally { released = WriteButton(false); }
        return WriteResult(pressed && released);
    }

    private async Task<InputResult> DoubleClickCoreAsync(int x, int y, CancellationToken cancellationToken)
    {
        var first = await ClickCoreAsync(x, y, cancellationToken);
        if (!first.Ok) return first;
        await DelayAsync(80, cancellationToken);
        return await ClickCoreAsync(x, y, cancellationToken);
    }

    private async Task<InputResult> TypeCoreAsync(string text, CancellationToken cancellationToken)
    {
        if (text is null || !layout.TryMapText(text.Replace("\r\n", "\n"), out var strokes)) return Failure("unsupported_character");
        foreach (var stroke in strokes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await TapAsync(stroke, cancellationToken)) return Failure("driver_write_failed");
            await DelayAsync(20, cancellationToken);
        }
        return Success();
    }

    private async Task<InputResult> PressCoreAsync(KeyInput key, CancellationToken cancellationToken)
    {
        return KeyParser.TryParse(key, out var stroke)
            ? WriteResult(await TapAsync(stroke, cancellationToken)) : Failure("invalid_key");
    }

    private async Task<InputResult> HotkeyCoreAsync(KeyChord chord, CancellationToken cancellationToken)
    {
        return KeyParser.TryParseHotkey(chord, out var keys, out var modifiers)
            ? WriteResult(await TapAsync(keys, modifiers, cancellationToken)) : Failure("invalid_key");
    }

    private Task<bool> TapAsync(KeyStroke stroke, CancellationToken cancellationToken) =>
        TapAsync(stroke.Key == 0 ? [] : [stroke.Key], stroke.Modifiers, cancellationToken);

    private async Task<bool> TapAsync(byte[] keys, byte modifiers, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pressed = false;
        var released = false;
        try
        {
            pressed = device.WriteKeyboard(modifiers, keys);
            if (pressed) await DelayAsync(35, cancellationToken);
        }
        finally { released = device.WriteKeyboard(0, []); }
        return pressed && released;
    }

    private Task<InputResult> ScrollCoreAsync(int delta, CancellationToken cancellationToken)
    {
        while (delta != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = (sbyte)Math.Clamp(delta, -127, 127);
            if (!device.WriteMouse(new(false, device.MouseButtons, 0, 0, step))) return Task.FromResult(Failure("driver_write_failed"));
            delta -= step;
        }
        return Task.FromResult(Success());
    }

    private async Task<InputResult> DragCoreAsync(int x1, int y1, int x2, int y2, CancellationToken cancellationToken)
    {
        if (!await MoveAsync(x1, y1, cancellationToken)) return Failure("driver_write_failed");
        await DelayAsync(80, cancellationToken);
        var moved = false;
        var released = false;
        try
        {
            if (WriteButton(true))
            {
                await DelayAsync(45, cancellationToken);
                moved = await MovePathAsync(new(x1, y1), new(x2, y2), 24, false, cancellationToken);
            }
        }
        finally { released = WriteButton(false); }
        return WriteResult(moved && released);
    }

    private async Task<InputResult> SequenceCoreAsync(InputAction[] actions, CancellationToken cancellationToken)
    {
        if (actions is null) return Failure("unknown_action");
        foreach (var action in actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = action?.Op?.ToLowerInvariant() switch
            {
                "click" => await ClickCoreAsync(action.X, action.Y, cancellationToken),
                "double_click" => await DoubleClickCoreAsync(action.X, action.Y, cancellationToken),
                "type" => await TypeCoreAsync(action.Text ?? "", cancellationToken),
                "press" => await PressCoreAsync(action.Key ?? default, cancellationToken),
                "hotkey" => await HotkeyCoreAsync(action.Keys ?? default, cancellationToken),
                "scroll" => await ScrollCoreAsync(action.Delta, cancellationToken),
                "drag" => await DragCoreAsync(action.X, action.Y, action.X2, action.Y2, cancellationToken),
                _ => Failure("unknown_action")
            };
            if (!result.Ok) return result;
        }
        return Success();
    }

    private Task<bool> MoveAsync(int x, int y, CancellationToken cancellationToken) => pointer.TryGetPosition(out var start)
        ? MovePathAsync(start, new(x, y), 12, true, cancellationToken)
        : Task.FromResult(false);

    private async Task<bool> MovePathAsync(PointerPosition start, PointerPosition end, int steps, bool ease, CancellationToken cancellationToken)
    {
        var size = pointer.Size;
        if (size.Width <= 0 || size.Height <= 0) return false;
        for (var i = 1; i <= steps; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var t = (double)i / steps;
            if (ease) t = t * t * (3 - 2 * t);
            var x = (long)Math.Round(start.X + ((double)end.X - start.X) * t);
            var y = (long)Math.Round(start.Y + ((double)end.Y - start.Y) * t);
            var report = new MouseReport(true, device.MouseButtons, Normalize(x, size.Width), Normalize(y, size.Height));
            if (!device.WriteMouse(report)) return false;
            await DelayAsync(15, cancellationToken);
        }
        return true;
    }

    private bool WriteButton(bool down)
    {
        var buttons = down ? (byte)(device.MouseButtons | 1) : (byte)(device.MouseButtons & ~1);
        return device.WriteMouse(new(false, buttons, 0, 0));
    }

    private static short Normalize(long position, int length) => (short)Math.Clamp(position * 32767 / Math.Max(1, length - 1), 0, 32767);
    private Task DelayAsync(int milliseconds, CancellationToken cancellationToken) => Task.Delay(TimeSpan.FromMilliseconds(milliseconds), _time, cancellationToken);
    private static InputResult WriteResult(bool success) => success ? Success() : Failure("driver_write_failed");
    private static InputResult Success() => new(true, Backend);
    private static InputResult Failure(string error) => new(false, Backend, error);
}
