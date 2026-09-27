using RemoteControl.Core;
using RemoteControl.Infrastructure.Scripting;
using Xunit;

namespace RemoteControl.Tests.Scripting;

public sealed class LuaServiceTests
{
    [Theory]
    [InlineData("while true do end")]
    [InlineData("sleep(60000)")]
    public async Task Deadline_interrupts_execution_and_next_call_works(string code)
    {
        var services = new FakeServices();
        using var lua = new LuaService(services, services, services, TimeSpan.FromMilliseconds(100));
        var stopped = await lua.RunAsync(code, "state", default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stopped.Ok);
        Assert.Equal("execution_timeout", stopped.Error);
        var next = await lua.RunAsync("return 42", "state", default);
        Assert.True(next.Ok, next.Error);
        Assert.Equal("42", next.Output);
    }

    [Fact]
    public async Task Caller_cancellation_interrupts_instruction_loop()
    {
        var services = new FakeServices();
        using var lua = new LuaService(services, services, services);
        using var stop = new CancellationTokenSource(50);
        var result = await lua.RunAsync("while true do end", null, stop.Token).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("execution_timeout", result.Error);
    }

    [Theory]
    [InlineData("return run('python', 'raise Exception()')", "shell_failed")]
    [InlineData("return environment('python', 'broken', {})", "package_failed")]
    [InlineData("click(1,2)", "input_failed")]
    public async Task Bindings_propagate_operation_failures(string code, string error)
    {
        var services = new FakeServices();
        using var lua = new LuaService(services, services, services);
        var result = await lua.RunAsync(code, null, default);
        Assert.False(result.Ok);
        Assert.Contains(error, result.Error);
    }

    [Fact]
    public async Task Named_session_serializes_calls_and_preserves_globals()
    {
        var services = new FakeServices();
        using var lua = new LuaService(services, services, services);
        await lua.RunAsync("n=0", "state", default);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => lua.RunAsync("n=n+1; return n", "state", default)));
        Assert.All(results, result => Assert.True(result.Ok, result.Error));
        Assert.Equal(Enumerable.Range(1, 8), results.Select(x => int.Parse(x.Output)).Order());
    }

    private sealed class FakeServices : IExecutionService, IEnvironmentService, IInputService
    {
        public Task<ExecutionResult> RunAsync(string runtime, string code, string? environment, string? session, int timeoutMs, CancellationToken cancellationToken) => Task.FromResult(new ExecutionResult(false, runtime, session, "", "", 1, 0, "shell_failed"));
        public Task<bool> CloseSessionAsync(string runtime, string session, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<ShellStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(new ShellStatus([], []));
        public Task<string[]> ListAsync(CancellationToken cancellationToken) => Task.FromResult(Array.Empty<string>());
        public Task<EnvironmentResult> EnsureAsync(string runtime, string name, string[] packages, CancellationToken cancellationToken) => Task.FromResult(new EnvironmentResult(false, runtime, name, "", packages, "package_failed"));
        Task<InputStatus> IInputService.GetStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        private static Task<InputResult> Fail() => Task.FromResult(new InputResult(false, "test", "input_failed"));
        public Task<InputResult> ClickAsync(int x, int y, CancellationToken cancellationToken) => Fail();
        public Task<InputResult> DoubleClickAsync(int x, int y, CancellationToken cancellationToken) => Fail();
        public Task<InputResult> TypeAsync(string text, CancellationToken cancellationToken) => Fail();
        public Task<InputResult> PressAsync(KeyInput key, CancellationToken cancellationToken) => Fail();
        public Task<InputResult> HotkeyAsync(KeyChord keys, CancellationToken cancellationToken) => Fail();
        public Task<InputResult> ScrollAsync(int delta, CancellationToken cancellationToken) => Fail();
        public Task<InputResult> DragAsync(int x1, int y1, int x2, int y2, CancellationToken cancellationToken) => Fail();
        public Task<InputResult> SequenceAsync(InputAction[] actions, CancellationToken cancellationToken) => Fail();
        public Task<InputResult> RawMouseAsync(byte[] report, CancellationToken cancellationToken) => Fail();
        public Task<InputResult> RawKeyboardAsync(byte[] report, CancellationToken cancellationToken) => Fail();
        public Task<InputResult> ReleaseAllAsync(CancellationToken cancellationToken) => Fail();
    }
}
