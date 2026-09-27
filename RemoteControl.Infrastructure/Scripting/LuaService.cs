using System.Collections.Concurrent;
using MoonSharp.Interpreter;

namespace RemoteControl.Infrastructure.Scripting;

public sealed class LuaService : ILuaService, IDisposable
{
    private readonly IExecutionService _execution;
    private readonly IInputService _input;
    private readonly IEnvironmentService _environments;
    private readonly TimeSpan _executionTimeout;
    private readonly ConcurrentDictionary<string, LuaSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    public LuaService(IExecutionService execution, IInputService input, IEnvironmentService environments)
        : this(execution, input, environments, TimeSpan.FromSeconds(30)) { }

    internal LuaService(IExecutionService execution, IInputService input, IEnvironmentService environments, TimeSpan executionTimeout)
    {
        _execution = execution;
        _input = input;
        _environments = environments;
        _executionTimeout = executionTimeout;
    }

    public async Task<LuaResult> RunAsync(string code, string? session, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0) return new(false, "", "session_closed");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        deadline.CancelAfter(_executionTimeout);
        var state = session is null ? new LuaSession() : _sessions.GetOrAdd(session, _ => new LuaSession());
        var acquired = false;
        try
        {
            await state.Gate.WaitAsync(deadline.Token);
            acquired = true;
            deadline.Token.ThrowIfCancellationRequested();
            if (state.Closed) return new(false, "", "session_closed");
            state.Token = deadline.Token;
            Bind(state);
            var coroutine = state.Script.CreateCoroutine(state.Script.LoadString(code)).Coroutine;
            coroutine.AutoYieldCounter = 10000;
            DynValue? result;
            do
            {
                deadline.Token.ThrowIfCancellationRequested();
                result = coroutine.Resume();
                if (coroutine.State != CoroutineState.Dead) await Task.Yield();
            } while (coroutine.State != CoroutineState.Dead);
            deadline.Token.ThrowIfCancellationRequested();
            return new(true, result?.ToString() ?? "");
        }
        catch (Exception) when (deadline.IsCancellationRequested)
        {
            if (acquired)
            {
                state.Closed = true;
                if (session is not null) _sessions.TryRemove(new KeyValuePair<string, LuaSession>(session, state));
            }
            return new(false, "", "execution_timeout");
        }
        catch (Exception ex) { return new(false, "", ex.Message); }
        finally { if (acquired) state.Gate.Release(); }
    }

    private void Bind(LuaSession state)
    {
        var globals = state.Script.Globals;
        globals["click"] = (Action<int, int>)((x, y) => Require(_input.ClickAsync(x, y, state.Token)));
        globals["double_click"] = (Action<int, int>)((x, y) => Require(_input.DoubleClickAsync(x, y, state.Token)));
        globals["type_text"] = (Action<string>)(text => Require(_input.TypeAsync(text, state.Token)));
        globals["press"] = DynValue.NewCallback((_, arguments) =>
        {
            if (arguments.Count != 1) throw new ScriptRuntimeException("press(key): one key required");
            Require(_input.PressAsync(ReadKey(arguments[0]), state.Token));
            return DynValue.Nil;
        });
        globals["hotkey"] = DynValue.NewCallback((_, arguments) =>
        {
            if (arguments.Count != 1) throw new ScriptRuntimeException("hotkey(keys): one key array or shortcut required");
            var value = arguments[0];
            var chord = value.Type switch
            {
                DataType.String => KeyChord.FromShortcut(value.String),
                DataType.Table => KeyChord.FromKeys(Enumerable.Range(1, value.Table.Length)
                    .Select(index => ReadKey(value.Table.Get(index))).ToArray()),
                _ => throw new ScriptRuntimeException("invalid_key")
            };
            Require(_input.HotkeyAsync(chord, state.Token));
            return DynValue.Nil;
        });
        globals["drag"] = (Action<int, int, int, int>)((x1, y1, x2, y2) => Require(_input.DragAsync(x1, y1, x2, y2, state.Token)));
        globals["sleep"] = (Action<int>)(ms => Task.Delay(Math.Clamp(ms, 0, 60000), state.Token).GetAwaiter().GetResult());
        globals["run"] = (Func<string, string, string>)((runtime, code) =>
        {
            var result = _execution.RunAsync(runtime, code, null, null, 30000, state.Token).GetAwaiter().GetResult();
            if (!result.Ok) throw new InvalidOperationException(result.Error ?? "operation_failed");
            return result.Stdout;
        });
        globals["environment"] = (Func<string, string, string[], string>)((runtime, name, packages) =>
        {
            var result = _environments.EnsureAsync(runtime, name, packages, state.Token).GetAwaiter().GetResult();
            if (!result.Ok) throw new InvalidOperationException(result.Error ?? "operation_failed");
            return result.Path;
        });
    }

    private static KeyInput ReadKey(DynValue value) => value.Type switch
    {
        DataType.String => KeyInput.FromText(value.String),
        DataType.Number when value.Number is >= 0 and <= 255 && value.Number == Math.Truncate(value.Number)
            => KeyInput.FromUsage((byte)value.Number),
        _ => throw new ScriptRuntimeException("invalid_key")
    };

    private static void Require(Task<InputResult> operation)
    {
        var result = operation.GetAwaiter().GetResult();
        if (!result.Ok) throw new InvalidOperationException(result.Error ?? "input_failed");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        _sessions.Clear();
    }

    private sealed class LuaSession
    {
        public Script Script { get; } = new(CoreModules.Preset_Complete);
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public CancellationToken Token { get; set; }
        public bool Closed { get; set; }
    }
}
