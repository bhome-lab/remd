namespace RemoteControl.Core.Input;

/// <summary>Последовательные пользовательские действия поверх HID-устройства.</summary>
public sealed class InputService(IInputDevice device, IKeyboardLayout layout, IDesktopPointer pointer, TimeProvider? timeProvider = null) : IInputService, IDisposable
{
    private const string Backend = "fakerinput";
    private const int MaximumHoldTimeoutMs = 60000;
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly object _keyboardStateLock = new();
    private readonly Dictionary<KeyStroke, HeldKeyTiming> _heldKeys = [];
    private byte _rawKeyboardModifiers;
    private byte[] _rawKeyboardKeys = [];
    private byte _temporaryModifiers;
    private byte[] _temporaryKeys = [];
    private ITimer? _holdExpirationTimer;
    private bool _keyboardWriteRetryPending;
    private bool _disposed;

    private readonly record struct HeldKeyTiming(long PressedAtTimestamp, int HoldTimeoutMs);

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
    public Task<InputResult> KeyDownAsync(KeyInput key, int holdTimeoutMs, CancellationToken cancellationToken) =>
        ExecuteAsync(() => Task.FromResult(KeyDownCore(key, holdTimeoutMs)), cancellationToken);
    public Task<InputResult> KeyUpAsync(KeyInput key, CancellationToken cancellationToken) =>
        ExecuteAsync(() => Task.FromResult(KeyUpCore(key)), cancellationToken);
    public Task<InputResult> HotkeyAsync(KeyChord keys, CancellationToken cancellationToken) => ExecuteAsync(() => HotkeyCoreAsync(keys, cancellationToken), cancellationToken);
    public Task<InputResult> ScrollAsync(int delta, CancellationToken cancellationToken) => ExecuteAsync(() => ScrollCoreAsync(delta, cancellationToken), cancellationToken);
    public Task<InputResult> DragAsync(int x1, int y1, int x2, int y2, CancellationToken cancellationToken) => ExecuteAsync(() => DragCoreAsync(x1, y1, x2, y2, cancellationToken), cancellationToken);
    public Task<InputResult> SequenceAsync(InputAction[] actions, bool resetBefore, bool resetAfter, CancellationToken cancellationToken) =>
        ExecuteAsync(() => SequenceCoreAsync(actions, resetBefore, resetAfter, cancellationToken), cancellationToken);

    public Task<InputResult> RawMouseAsync(byte[] report, CancellationToken cancellationToken) => ExecuteAsync(() =>
        Task.FromResult(RawInputReports.TryReadMouse(report, out var mouse) ? WriteResult(device.WriteMouse(mouse)) : Failure("invalid_report")), cancellationToken);

    public Task<InputResult> RawKeyboardAsync(byte[] report, CancellationToken cancellationToken) => ExecuteAsync(() =>
        Task.FromResult(RawKeyboardCore(report)), cancellationToken);

    public Task<InputResult> ReleaseAllAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(() => Task.FromResult(ReleaseAllCore()), cancellationToken);

    private async Task<InputResult> ExecuteAsync(Func<Task<InputResult>> action, CancellationToken cancellationToken)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            if (!device.Status.Connected) return Failure("driver_unavailable");
            cancellationToken.ThrowIfCancellationRequested();
            return await action();
        }
        finally { _commandGate.Release(); }
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
            var result = await TapAsync(stroke, cancellationToken);
            if (!result.Ok) return result;
            await DelayAsync(20, cancellationToken);
        }
        return Success();
    }

    private async Task<InputResult> PressCoreAsync(KeyInput key, CancellationToken cancellationToken)
    {
        return KeyParser.TryParse(key, out var stroke)
            ? await TapAsync(stroke, cancellationToken) : Failure("invalid_key");
    }

    private async Task<InputResult> HotkeyCoreAsync(KeyChord chord, CancellationToken cancellationToken)
    {
        return KeyParser.TryParseHotkey(chord, out var keys, out var modifiers)
            ? await TapAsync(keys, modifiers, cancellationToken) : Failure("invalid_key");
    }

    private Task<InputResult> TapAsync(KeyStroke stroke, CancellationToken cancellationToken) =>
        TapAsync(stroke.Key == 0 ? [] : [stroke.Key], stroke.Modifiers, cancellationToken);

    private async Task<InputResult> TapAsync(byte[] keys, byte modifiers, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool pressed;
        var released = false;
        lock (_keyboardStateLock)
        {
            _temporaryKeys = keys;
            _temporaryModifiers = modifiers;
            if (!TryBuildKeyboardReportLocked(out _, out _))
            {
                ClearTemporaryKeysLocked();
                return Failure("invalid_key");
            }
            pressed = WriteKeyboardStateLocked();
        }
        try
        {
            if (pressed) await DelayAsync(35, cancellationToken);
        }
        finally
        {
            lock (_keyboardStateLock)
            {
                ClearTemporaryKeysLocked();
                released = WriteKeyboardStateLocked();
                _keyboardWriteRetryPending = !released;
                ScheduleHoldTimerLocked();
            }
        }
        return WriteResult(pressed && released);
    }

    private InputResult KeyDownCore(KeyInput key, int holdTimeoutMs)
    {
        if (holdTimeoutMs is < 1 or > MaximumHoldTimeoutMs) return Failure("invalid_timeout");
        if (!KeyParser.TryParse(key, out var stroke) || stroke is { Key: 0, Modifiers: 0 }) return Failure("invalid_key");
        lock (_keyboardStateLock)
        {
            var wasAlreadyHeld = _heldKeys.TryGetValue(stroke, out var previousTiming);
            _heldKeys[stroke] = new(_timeProvider.GetTimestamp(), holdTimeoutMs);
            if (!TryBuildKeyboardReportLocked(out _, out _))
            {
                if (wasAlreadyHeld) _heldKeys[stroke] = previousTiming;
                else _heldKeys.Remove(stroke);
                return Failure("invalid_key");
            }
            if (!wasAlreadyHeld && !WriteKeyboardStateLocked())
            {
                _heldKeys.Remove(stroke);
                _keyboardWriteRetryPending = !WriteKeyboardStateLocked();
                ScheduleHoldTimerLocked();
                return Failure("driver_write_failed");
            }
            ScheduleHoldTimerLocked();
            return Success();
        }
    }

    private InputResult KeyUpCore(KeyInput key)
    {
        if (!KeyParser.TryParse(key, out var stroke) || stroke is { Key: 0, Modifiers: 0 }) return Failure("invalid_key");
        lock (_keyboardStateLock)
        {
            if (!_heldKeys.Remove(stroke, out var previousTiming)) return Success();
            if (!WriteKeyboardStateLocked())
            {
                _heldKeys[stroke] = previousTiming;
                _keyboardWriteRetryPending = !WriteKeyboardStateLocked();
                ScheduleHoldTimerLocked();
                return Failure("driver_write_failed");
            }
            ScheduleHoldTimerLocked();
            return Success();
        }
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

    private async Task<InputResult> SequenceCoreAsync(InputAction[] actions, bool resetBefore, bool resetAfter, CancellationToken cancellationToken)
    {
        if (actions is null) return Failure("unknown_action");
        foreach (var action in actions)
        {
            if (action?.Op?.Equals("key_down", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (action.HoldTimeoutMs is not int holdTimeoutMs || holdTimeoutMs is < 1 or > MaximumHoldTimeoutMs) return Failure("invalid_timeout");
                if (action.Key is not KeyInput key || !KeyParser.TryParse(key, out var stroke) || stroke is { Key: 0, Modifiers: 0 })
                    return Failure("invalid_key");
            }
            else if (action?.Op?.Equals("key_up", StringComparison.OrdinalIgnoreCase) == true &&
                (action.Key is not KeyInput key || !KeyParser.TryParse(key, out var stroke) || stroke is { Key: 0, Modifiers: 0 }))
                return Failure("invalid_key");
        }
        if (resetBefore)
        {
            var resetBeforeResult = ReleaseAllCore();
            if (!resetBeforeResult.Ok) return resetBeforeResult;
        }
        InputResult actionsResult = Success();
        InputResult resetAfterResult = Success();
        try { actionsResult = await RunSequenceActionsAsync(actions, cancellationToken); }
        finally { if (resetAfter) resetAfterResult = ReleaseAllCore(); }
        return resetAfterResult.Ok ? actionsResult : resetAfterResult;
    }

    private async Task<InputResult> RunSequenceActionsAsync(InputAction[] actions, CancellationToken cancellationToken)
    {
        foreach (var action in actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = action?.Op?.ToLowerInvariant() switch
            {
                "click" => await ClickCoreAsync(action.X, action.Y, cancellationToken),
                "double_click" => await DoubleClickCoreAsync(action.X, action.Y, cancellationToken),
                "type" => await TypeCoreAsync(action.Text ?? "", cancellationToken),
                "press" => await PressCoreAsync(action.Key ?? default, cancellationToken),
                "key_down" => KeyDownCore(action.Key ?? default, action.HoldTimeoutMs ?? 0),
                "key_up" => KeyUpCore(action.Key ?? default),
                "hotkey" => await HotkeyCoreAsync(action.Keys ?? default, cancellationToken),
                "scroll" => await ScrollCoreAsync(action.Delta, cancellationToken),
                "drag" => await DragCoreAsync(action.X, action.Y, action.X2, action.Y2, cancellationToken),
                _ => Failure("unknown_action")
            };
            if (!result.Ok) return result;
        }
        return Success();
    }

    private InputResult RawKeyboardCore(byte[] report)
    {
        if (!RawInputReports.IsKeyboard(report)) return Failure("invalid_report");
        lock (_keyboardStateLock)
        {
            if (!device.WriteKeyboard(report[1], report.AsSpan(3, 6))) return Failure("driver_write_failed");
            _heldKeys.Clear();
            _rawKeyboardModifiers = report[1];
            _rawKeyboardKeys = report.AsSpan(3, 6).ToArray();
            _keyboardWriteRetryPending = false;
            ScheduleHoldTimerLocked();
            return Success();
        }
    }

    private InputResult ReleaseAllCore()
    {
        lock (_keyboardStateLock)
        {
            _heldKeys.Clear();
            _rawKeyboardModifiers = 0;
            _rawKeyboardKeys = [];
            ClearTemporaryKeysLocked();
            var keyboardReleased = device.WriteKeyboard(0, []);
            _keyboardWriteRetryPending = !keyboardReleased;
            ScheduleHoldTimerLocked();
            var mouseReleased = device.WriteMouse(new(false, 0, 0, 0));
            return WriteResult(keyboardReleased && mouseReleased);
        }
    }

    private void ClearTemporaryKeysLocked()
    {
        _temporaryModifiers = 0;
        _temporaryKeys = [];
    }

    private bool TryBuildKeyboardReportLocked(out byte modifiers, out byte[] keys)
    {
        modifiers = (byte)(_rawKeyboardModifiers | _temporaryModifiers);
        var usages = new List<byte>(6);
        foreach (var key in _rawKeyboardKeys)
            if (key != 0 && !usages.Contains(key)) usages.Add(key);
        foreach (var stroke in _heldKeys.Keys)
        {
            modifiers |= stroke.Modifiers;
            if (stroke.Key != 0 && !usages.Contains(stroke.Key)) usages.Add(stroke.Key);
        }
        foreach (var key in _temporaryKeys)
            if (key != 0 && !usages.Contains(key)) usages.Add(key);
        keys = usages.ToArray();
        return keys.Length <= 6;
    }

    private bool WriteKeyboardStateLocked() =>
        TryBuildKeyboardReportLocked(out var modifiers, out var keys) && device.WriteKeyboard(modifiers, keys);

    private void OnHoldExpiration(object? _)
    {
        lock (_keyboardStateLock)
        {
            if (_disposed) return;
            var now = _timeProvider.GetTimestamp();
            var expired = _heldKeys.Where(item => _timeProvider.GetElapsedTime(item.Value.PressedAtTimestamp, now) >= TimeSpan.FromMilliseconds(item.Value.HoldTimeoutMs))
                .Select(item => item.Key).ToArray();
            foreach (var key in expired) _heldKeys.Remove(key);
            if (expired.Length > 0 || _keyboardWriteRetryPending)
                _keyboardWriteRetryPending = !WriteKeyboardStateLocked();
            ScheduleHoldTimerLocked();
        }
    }

    private void ScheduleHoldTimerLocked()
    {
        if (_disposed) return;
        var now = _timeProvider.GetTimestamp();
        var due = _keyboardWriteRetryPending ? TimeSpan.FromMilliseconds(250) : Timeout.InfiniteTimeSpan;
        foreach (var hold in _heldKeys.Values)
        {
            var remaining = TimeSpan.FromMilliseconds(hold.HoldTimeoutMs) - _timeProvider.GetElapsedTime(hold.PressedAtTimestamp, now);
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            if (due == Timeout.InfiniteTimeSpan || remaining < due) due = remaining;
        }
        if (_holdExpirationTimer is null)
        {
            if (due != Timeout.InfiniteTimeSpan)
                _holdExpirationTimer = _timeProvider.CreateTimer(OnHoldExpiration, null, due, Timeout.InfiniteTimeSpan);
        }
        else _holdExpirationTimer.Change(due, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        lock (_keyboardStateLock)
        {
            if (_disposed) return;
            _disposed = true;
            _holdExpirationTimer?.Dispose();
            _heldKeys.Clear();
            _rawKeyboardModifiers = 0;
            _rawKeyboardKeys = [];
            ClearTemporaryKeysLocked();
            device.WriteKeyboard(0, []);
            device.WriteMouse(new(false, 0, 0, 0));
        }
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
    private Task DelayAsync(int milliseconds, CancellationToken cancellationToken) => Task.Delay(TimeSpan.FromMilliseconds(milliseconds), _timeProvider, cancellationToken);
    private static InputResult WriteResult(bool success) => success ? Success() : Failure("driver_write_failed");
    private static InputResult Success() => new(true, Backend);
    private static InputResult Failure(string error) => new(false, Backend, error);
}
