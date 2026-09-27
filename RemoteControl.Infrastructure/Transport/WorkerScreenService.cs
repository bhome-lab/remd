namespace RemoteControl.Infrastructure.Transport;

public sealed class WorkerScreenService(IWorkerConnection connection, IWorkerEndpoint endpoint) : IScreenService
{
    public async Task<DesktopStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var response = await connection.CallAsync(new("desktop_status", new()), cancellationToken);
        return response.Desktop ?? new(false, endpoint.Status.State, "desktop-worker");
    }

    public async Task<CaptureResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        var response = await connection.CallAsync(new("capture", new(Capture: request)), cancellationToken);
        return response.Capture ?? new(false, Error: response.Error ?? "invalid_worker_response");
    }

    public async Task<WindowInfo[]> GetWindowsAsync(CancellationToken cancellationToken)
    {
        var response = await connection.CallAsync(new("windows", new()), cancellationToken);
        return response.Windows ?? [];
    }
}

