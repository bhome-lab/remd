using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RemoteControl.Core;
using RemoteControl.Infrastructure.Transport;

namespace RemoteControl.Tests.Transport;

public sealed class WorkerPipeServerTests
{
    [Fact]
    public async Task DisconnectedCallerDoesNotKillWorkerOrSessionState()
    {
        var pipeName = $"remd-test-{Guid.NewGuid():N}";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessionValue = 0;
        var server = new WorkerPipeServer(pipeName, async (request, token) =>
        {
            if (request.Op == "seed")
            {
                sessionValue = 42;
                entered.SetResult();
                await finish.Task.WaitAsync(token);
                return new WorkerResponse(true, Lua: new LuaResult(true, new string('x', 128 * 1024)));
            }
            return new WorkerResponse(true, Lua: new LuaResult(true, sessionValue.ToString()));
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var running = server.RunAsync(timeout.Token);
        try
        {
            using (var disconnected = NewClient(pipeName))
            {
                await disconnected.ConnectAsync(timeout.Token);
                await WriteAsync(disconnected, "seed", timeout.Token);
                await entered.Task.WaitAsync(timeout.Token);
            }
            finish.SetResult();

            var result = await CallAsync(pipeName, "read", timeout.Token);
            Assert.True(result.Ok);
            Assert.Equal("42", result.Lua?.Output);
            Assert.False(running.IsCompleted);
        }
        finally { timeout.Cancel(); await running; }
    }

    [Fact]
    public async Task InvalidRequestFailsOnlyItsConnection()
    {
        var pipeName = $"remd-test-{Guid.NewGuid():N}";
        var server = new WorkerPipeServer(pipeName, (_, _) => Task.FromResult(new WorkerResponse(true)));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var running = server.RunAsync(timeout.Token);
        try
        {
            using (var invalid = NewClient(pipeName))
            {
                await invalid.ConnectAsync(timeout.Token);
                using var writer = new StreamWriter(invalid, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync("not JSON".AsMemory(), timeout.Token);
                var failure = await ReadAsync(invalid, timeout.Token);
                Assert.False(failure.Ok);
            }
            Assert.True((await CallAsync(pipeName, "ping", timeout.Token)).Ok);
        }
        finally { timeout.Cancel(); await running; }
    }

    [Fact]
    public async Task OperationsRemainSequential()
    {
        var pipeName = $"remd-test-{Guid.NewGuid():N}";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maximumActive = 0;
        var server = new WorkerPipeServer(pipeName, async (request, token) =>
        {
            maximumActive = Math.Max(maximumActive, Interlocked.Increment(ref active));
            try
            {
                if (request.Op == "first") { entered.SetResult(); await finish.Task.WaitAsync(token); }
                return new WorkerResponse(true);
            }
            finally { Interlocked.Decrement(ref active); }
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var running = server.RunAsync(timeout.Token);
        try
        {
            var first = CallAsync(pipeName, "first", timeout.Token);
            await entered.Task.WaitAsync(timeout.Token);
            var second = CallAsync(pipeName, "second", timeout.Token);
            finish.SetResult();
            Assert.All(await Task.WhenAll(first, second), result => Assert.True(result.Ok));
            Assert.Equal(1, maximumActive);
        }
        finally { timeout.Cancel(); await running; }
    }

    private static NamedPipeClientStream NewClient(string name) =>
        new(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);

    private static async Task<WorkerResponse> CallAsync(string pipeName, string operation, CancellationToken token)
    {
        using var pipe = NewClient(pipeName);
        await pipe.ConnectAsync(token);
        await WriteAsync(pipe, operation, token);
        return await ReadAsync(pipe, token);
    }

    private static async Task WriteAsync(Stream connection, string operation, CancellationToken token)
    {
        using var writer = new StreamWriter(connection, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        var json = JsonSerializer.Serialize(new WorkerRequest(operation, new WorkerArgs()), WorkerJsonContext.Default.WorkerRequest);
        await writer.WriteLineAsync(json.AsMemory(), token);
    }

    private static async Task<WorkerResponse> ReadAsync(Stream connection, CancellationToken token)
    {
        using var reader = new StreamReader(connection, Encoding.UTF8, false, leaveOpen: true);
        var json = await reader.ReadLineAsync(token);
        return JsonSerializer.Deserialize(json!, WorkerJsonContext.Default.WorkerResponse)!;
    }
}
