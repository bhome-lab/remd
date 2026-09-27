namespace RemoteControl.Infrastructure.Transport;

public sealed class WorkerInputService(IWorkerConnection connection) : IInputService
{
    public async Task<InputStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var response = await connection.CallAsync(new("input_status", new()), cancellationToken);
        return response.InputStatus ?? new("fakerinput", new(false, "missing", false, false, response.Error ?? "invalid_worker_response"));
    }

    private async Task<InputResult> SendAsync(string operation, WorkerArgs args, CancellationToken cancellationToken)
    {
        var response = await connection.CallAsync(new(operation, args), cancellationToken);
        return response.Input ?? new(false, "fakerinput", response.Error ?? "invalid_worker_response");
    }

    public Task<InputResult> ClickAsync(int x, int y, CancellationToken ct) => SendAsync("click", new(X: x, Y: y), ct);
    public Task<InputResult> DoubleClickAsync(int x, int y, CancellationToken ct) => SendAsync("double_click", new(X: x, Y: y), ct);
    public Task<InputResult> TypeAsync(string text, CancellationToken ct) => SendAsync("type", new(Text: text), ct);
    public Task<InputResult> PressAsync(KeyInput key, CancellationToken ct) => SendAsync("press", new(Key: key), ct);
    public Task<InputResult> KeyDownAsync(KeyInput key, int holdTimeoutMs, CancellationToken ct) => SendAsync("key_down", new(Key: key, HoldTimeoutMs: holdTimeoutMs), ct);
    public Task<InputResult> KeyUpAsync(KeyInput key, CancellationToken ct) => SendAsync("key_up", new(Key: key), ct);
    public Task<InputResult> HotkeyAsync(KeyChord keys, CancellationToken ct) => SendAsync("hotkey", new(Keys: keys), ct);
    public Task<InputResult> ScrollAsync(int delta, CancellationToken ct) => SendAsync("scroll", new(Delta: delta), ct);
    public Task<InputResult> DragAsync(int x1, int y1, int x2, int y2, CancellationToken ct) => SendAsync("drag", new(X1: x1, Y1: y1, X2: x2, Y2: y2), ct);
    public Task<InputResult> SequenceAsync(InputAction[] actions, bool resetBefore, bool resetAfter, CancellationToken ct) =>
        SendAsync("sequence", new(Actions: actions, ResetBefore: resetBefore, ResetAfter: resetAfter), ct);
    public Task<InputResult> RawMouseAsync(byte[] report, CancellationToken ct) => SendAsync("raw_mouse", new(Report: report), ct);
    public Task<InputResult> RawKeyboardAsync(byte[] report, CancellationToken ct) => SendAsync("raw_keyboard", new(Report: report), ct);
    public Task<InputResult> ReleaseAllAsync(CancellationToken ct) => SendAsync("release_all", new(), ct);
}
