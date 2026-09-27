using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace RemoteControl.Infrastructure.Transport;

public sealed class WorkerPipeClient(IWorkerEndpoint endpoint) : IWorkerConnection, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<WorkerResponse> CallAsync(WorkerRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (endpoint.Status.State != "running")
                return new(false, Error: "interactive_session_unavailable");

            // Retry only connection establishment. Once a command may have reached the
            // worker, replay could duplicate mouse input or arbitrary shell execution.
            for (var attempt = 0; attempt < 5; attempt++)
            {
                using var pipe = NewPipe(endpoint.PipeName);
                try { await pipe.ConnectAsync(1000, cancellationToken); }
                catch (Exception ex) when (ex is IOException or TimeoutException)
                {
                    if (attempt == 4) return new(false, Error: "interactive_session_unavailable");
                    await Task.Delay(100, cancellationToken);
                    continue;
                }
                try { return await ExchangeAsync(pipe, request, cancellationToken); }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException)
                {
                    return new(false, Error: "worker_response_lost");
                }
            }
            return new(false, Error: "interactive_session_unavailable");
        }
        finally { _gate.Release(); }
    }

    public static async Task<bool> PingAsync(string pipeName, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(1));
        using var pipe = NewPipe(pipeName);
        try
        {
            await pipe.ConnectAsync(deadline.Token);
            var response = await ExchangeAsync(pipe, new("ping", new()), deadline.Token);
            return response.Ok;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException or JsonException)
        {
            return false;
        }
    }

    private static NamedPipeClientStream NewPipe(string pipeName) =>
        new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

    private static async Task<WorkerResponse> ExchangeAsync(NamedPipeClientStream pipe, WorkerRequest request, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        var json = JsonSerializer.Serialize(request, WorkerJsonContext.Default.WorkerRequest);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
        var response = await reader.ReadLineAsync(cancellationToken) ?? throw new IOException("desktop_worker_closed");
        return JsonSerializer.Deserialize(response, WorkerJsonContext.Default.WorkerResponse)
            ?? throw new JsonException("invalid_worker_response");
    }

    public void Dispose() => _gate.Dispose();
}

