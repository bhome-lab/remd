using System.Diagnostics;

namespace RemoteControl.Infrastructure.Execution;

public sealed class ExecutionService(EnvironmentService environments) : IExecutionService, IDisposable, IAsyncDisposable
{
    private readonly SessionRegistry _sessions = new();
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    public async Task<ShellStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return new(new Dictionary<string, bool>
        {
            ["powershell"] = true,
            ["nodejs"] = RuntimeLocator.Find("node") is not null,
            ["python"] = RuntimeLocator.Find("python") is not null
        }, await environments.ListAsync(cancellationToken));
    }

    public async Task<ExecutionResult> RunAsync(string runtime, string code, string? environment, string? session, int timeoutMs, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        runtime = RuntimeNames.Normalize(runtime);
        RuntimeResult result;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (runtime is not ("powershell" or "python" or "nodejs")) return Result(new(false, "", "", -1, "runtime_not_found"));
            if (runtime == "powershell" && environment is not null) return Result(new(false, "", "", -1, "environment_not_supported"));
            if (environment is not null)
            {
                environment = RuntimeNames.EnvironmentName(environment);
                if (!environments.Exists(runtime, environment)) return Result(new(false, "", "", -1, "environment_not_found"));
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            deadline.CancelAfter(Math.Max(1, timeoutMs));
            var directory = environment is null ? Environment.CurrentDirectory : environments.PathFor(environment);
            var executable = runtime == "python"
                ? environment is null ? RuntimeLocator.Find("python") ?? "python" : environments.PythonFor(environment)
                : RuntimeLocator.Find("node") ?? "node";

            if (session is not null)
            {
                result = await _sessions.RunAsync($"{runtime}:{session}", environment,
                    token => runtime == "powershell"
                        ? Task.FromResult<IRuntimeSession>(new PowerShellSession())
                        : ScriptSession.StartAsync(runtime, executable, directory, token), code, deadline.Token);
            }
            else if (runtime == "powershell")
            {
                using var powershell = new PowerShellSession();
                result = await powershell.ExecuteAsync(code, deadline.Token);
            }
            else
            {
                var info = ProcessRunner.StartInfo(executable, [runtime == "python" ? "-c" : "-e", code], directory);
                result = await ProcessRunner.RunAsync(info, deadline.Token);
            }
        }
        catch (OperationCanceledException) { result = new(false, "", "", -1, "execution_timeout"); }
        catch (Exception ex) { result = new(false, "", "", -1, ex.Message); }
        return Result(result);

        ExecutionResult Result(RuntimeResult value) => new(value.Ok, runtime, session, value.Stdout, value.Stderr, value.ExitCode,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, value.Error);
    }

    public Task<bool> CloseSessionAsync(string runtime, string session, CancellationToken cancellationToken) =>
        _sessions.CloseAsync($"{RuntimeNames.Normalize(runtime)}:{session}", cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _lifetime.CancelAsync();
        await _sessions.DisposeAsync();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
