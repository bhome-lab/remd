using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RemoteControl.Core;
using RemoteControl.Infrastructure.Transport;

namespace RemoteControl.Tests.Transport;

public sealed class WorkerPipeClientTests
{
    [Fact]
    public async Task DoesNotReplayACommandAfterLosingItsResponse()
    {
        var name = "remd-test-" + Guid.NewGuid().ToString("N");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var executed = 0;
        var serving = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(timeout.Token);
            using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
            var request = await reader.ReadLineAsync(timeout.Token);
            Assert.NotNull(request);
            Interlocked.Increment(ref executed);
            server.Disconnect();
        });
        using var client = new WorkerPipeClient(new Endpoint(name, "running"));
        var response = await client.CallAsync(new("click", new(X: 42, Y: 24)), timeout.Token);
        await serving;
        Assert.False(response.Ok);
        Assert.Equal("worker_response_lost", response.Error);
        Assert.Equal(1, executed);
    }

    [Fact]
    public async Task DoesNotSendBeforeReadinessHandshake()
    {
        using var client = new WorkerPipeClient(new Endpoint("unused", "starting"));
        var response = await client.CallAsync(new("click", new()), CancellationToken.None);
        Assert.False(response.Ok);
        Assert.Equal("interactive_session_unavailable", response.Error);
    }

    [Fact]
    public async Task CarriesLargeTypedPayloadWithoutNestedJsonEncoding()
    {
        var name = "remd-test-" + Guid.NewGuid().ToString("N");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var bytes = Enumerable.Range(0, 131072).Select(i => (byte)i).ToArray();
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serving = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(timeout.Token);
            using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            var line = await reader.ReadLineAsync(timeout.Token);
            var request = JsonSerializer.Deserialize(line!, WorkerJsonContext.Default.WorkerRequest);
            Assert.Equal(CaptureKind.Region, request!.Args.Capture!.Kind);
            await writer.WriteLineAsync(JsonSerializer.Serialize(
                new WorkerResponse(true, Capture: new(true, bytes)),
                WorkerJsonContext.Default.WorkerResponse).AsMemory(), timeout.Token);
        });
        using var client = new WorkerPipeClient(new Endpoint(name, "running"));
        var result = await client.CallAsync(new("capture", new(Capture: new(CaptureKind.Region, Width: 320, Height: 200))), timeout.Token);
        await serving;
        Assert.True(result.Ok);
        Assert.Equal("image/jpeg", result.Capture!.MimeType);
        Assert.Equal(bytes, result.Capture.Data);
    }

    private sealed class Endpoint(string pipeName, string state) : IWorkerEndpoint
    {
        public string PipeName => pipeName;
        public WorkerStatus Status => new(state);
    }
}

