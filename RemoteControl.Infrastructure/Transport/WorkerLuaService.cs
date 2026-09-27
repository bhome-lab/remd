namespace RemoteControl.Infrastructure.Transport;

public sealed class WorkerLuaService(IWorkerConnection connection) : ILuaService
{
    public async Task<LuaResult> RunAsync(string code, string? session, CancellationToken cancellationToken)
    {
        var response = await connection.CallAsync(new("lua", new(Code: code, Session: session)), cancellationToken);
        return response.Lua ?? new(false, "", response.Error ?? "invalid_worker_response");
    }
}

