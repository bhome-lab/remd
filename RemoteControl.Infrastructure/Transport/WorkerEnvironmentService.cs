namespace RemoteControl.Infrastructure.Transport;

public sealed class WorkerEnvironmentService(IWorkerConnection connection) : IEnvironmentService
{
    public async Task<string[]> ListAsync(CancellationToken cancellationToken)
    {
        var response = await connection.CallAsync(new("environments", new()), cancellationToken);
        return response.Environments ?? [];
    }

    public async Task<EnvironmentResult> EnsureAsync(string runtime, string name, string[] packages, CancellationToken cancellationToken)
    {
        var response = await connection.CallAsync(new("environment", new(Runtime: runtime, Name: name, Packages: packages)), cancellationToken);
        return response.Environment ?? new(false, runtime, name, "", packages, response.Error ?? "invalid_worker_response");
    }
}

