namespace RemoteControl.Infrastructure.Transport;

public sealed class WorkerExecutionService(IWorkerConnection connection) : IExecutionService
{
    public async Task<ShellStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var response = await connection.CallAsync(new("shell_status", new()), cancellationToken);
        return response.Shell ?? new(new() { ["powershell"] = false, ["nodejs"] = false, ["python"] = false }, []);
    }

    public async Task<ExecutionResult> RunAsync(string runtime, string code, string? environment, string? session, int timeoutMs, CancellationToken cancellationToken)
    {
        var response = await connection.CallAsync(new("run", new(Runtime: runtime, Code: code, Environment: environment, Session: session, TimeoutMs: timeoutMs)), cancellationToken);
        return response.Execution ?? new(false, runtime, session, "", "", -1, 0, response.Error ?? "invalid_worker_response");
    }

    public async Task<bool> CloseSessionAsync(string runtime, string session, CancellationToken cancellationToken)
    {
        var response = await connection.CallAsync(new("close_session", new(Runtime: runtime, Session: session)), cancellationToken);
        return response.Closed ?? false;
    }
}

