using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace RemoteControl.Infrastructure.Transport;

/// <summary>Executes one request at a time; a disconnected caller never owns the worker lifetime.</summary>
public sealed class WorkerPipeServer(string pipeName, Func<WorkerRequest, CancellationToken, Task<WorkerResponse>> dispatch)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var connection = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try
            {
                await connection.WaitForConnectionAsync(cancellationToken);
                await ServeConnectionAsync(connection, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (IOException) { /* Client disconnected. The next caller uses the same application sessions. */ }
        }
    }

    private async Task ServeConnectionAsync(Stream connection, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(connection, Encoding.UTF8, false, leaveOpen: true);
        using var writer = new StreamWriter(connection, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        var line = await reader.ReadLineAsync(cancellationToken);
        if (line is null) return;

        WorkerResponse response;
        try
        {
            var request = JsonSerializer.Deserialize(line, WorkerJsonContext.Default.WorkerRequest)
                ?? throw new InvalidOperationException("invalid_worker_request");
            response = await dispatch(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            response = new WorkerResponse(false, Error: exception.Message);
        }

        // A failed write is handled by the accept loop, never by another write to the broken connection.
        var payload = JsonSerializer.Serialize(response, WorkerJsonContext.Default.WorkerResponse);
        await writer.WriteLineAsync(payload.AsMemory(), cancellationToken);
    }
}
