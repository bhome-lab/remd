namespace RemoteControl.Core;

public interface IInputService
{
    Task<InputStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task<InputResult> ClickAsync(int x, int y, CancellationToken cancellationToken);
    Task<InputResult> DoubleClickAsync(int x, int y, CancellationToken cancellationToken);
    Task<InputResult> TypeAsync(string text, CancellationToken cancellationToken);
    Task<InputResult> PressAsync(KeyInput key, CancellationToken cancellationToken);
    Task<InputResult> KeyDownAsync(KeyInput key, int holdTimeoutMs, CancellationToken cancellationToken);
    Task<InputResult> KeyUpAsync(KeyInput key, CancellationToken cancellationToken);
    Task<InputResult> HotkeyAsync(KeyChord keys, CancellationToken cancellationToken);
    Task<InputResult> ScrollAsync(int delta, CancellationToken cancellationToken);
    Task<InputResult> DragAsync(int x1, int y1, int x2, int y2, CancellationToken cancellationToken);
    Task<InputResult> SequenceAsync(InputAction[] actions, bool resetBefore, bool resetAfter, CancellationToken cancellationToken);
    Task<InputResult> RawMouseAsync(byte[] report, CancellationToken cancellationToken);
    Task<InputResult> RawKeyboardAsync(byte[] report, CancellationToken cancellationToken);
    Task<InputResult> ReleaseAllAsync(CancellationToken cancellationToken);
}

public interface IScreenService
{
    Task<DesktopStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task<CaptureResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken);
    Task<WindowInfo[]> GetWindowsAsync(CancellationToken cancellationToken);
}

public interface IExecutionService
{
    Task<ShellStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task<ExecutionResult> RunAsync(string runtime, string code, string? environment, string? session, int timeoutMs, CancellationToken cancellationToken);
    Task<bool> CloseSessionAsync(string runtime, string session, CancellationToken cancellationToken);
}

public interface IEnvironmentService
{
    Task<string[]> ListAsync(CancellationToken cancellationToken);
    Task<EnvironmentResult> EnsureAsync(string runtime, string name, string[] packages, CancellationToken cancellationToken);
}

public interface ILuaService
{
    Task<LuaResult> RunAsync(string code, string? session, CancellationToken cancellationToken);
}
