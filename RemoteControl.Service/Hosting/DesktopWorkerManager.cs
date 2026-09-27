using RemoteControl.Core;
using RemoteControl.Infrastructure.Transport;

namespace RemoteControl.Service.Hosting;

public interface IWorkerProcess : IDisposable
{
    bool HasExited { get; }
    void Terminate();
}

public interface IWorkerProcessLauncher
{
    int? GetActiveSessionId();
    IWorkerProcess Start(int sessionId, string pipeName);
}

/// <summary>Owns the worker process; callers observe immutable status, never a Process handle.</summary>
public sealed class DesktopWorkerManager : BackgroundService, IWorkerEndpoint
{
    private readonly IWorkerProcessLauncher _launcher;
    private readonly Func<string, CancellationToken, Task<bool>> _readinessProbe;
    private readonly TimeSpan _pollingInterval;
    private WorkerStatus _status = new("starting");

    public DesktopWorkerManager(
        IWorkerProcessLauncher launcher,
        string? pipeName = null,
        Func<string, CancellationToken, Task<bool>>? readinessProbe = null,
        TimeSpan? pollingInterval = null)
    {
        _launcher = launcher;
        PipeName = pipeName ?? $"RemoteControl.DesktopWorker.{Environment.ProcessId}";
        _readinessProbe = readinessProbe ?? WorkerPipeClient.PingAsync;
        _pollingInterval = pollingInterval ?? TimeSpan.FromMilliseconds(250);
    }

    public string PipeName { get; }
    public WorkerStatus Status => Volatile.Read(ref _status);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IWorkerProcess? worker = null;
        int? workerSession = null;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var activeSession = _launcher.GetActiveSessionId();
                    if (worker is not null && (worker.HasExited || activeSession != workerSession))
                    {
                        SetStatus("starting");
                        StopWorker(ref worker);
                        workerSession = null;
                    }

                    if (activeSession is null)
                    {
                        SetStatus("unavailable", "interactive_session_unavailable");
                    }
                    else
                    {
                        if (worker is null)
                        {
                            SetStatus("starting");
                            worker = _launcher.Start(activeSession.Value, PipeName);
                            workerSession = activeSession;
                        }

                        if (Status.State != "running" && await _readinessProbe(PipeName, stoppingToken))
                            SetStatus("running");
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    SetStatus("unavailable", exception.Message);
                    StopWorker(ref worker);
                    workerSession = null;
                }

                await Task.Delay(_pollingInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            SetStatus("unavailable", "worker_stopped");
            StopWorker(ref worker);
        }
    }

    private void SetStatus(string state, string? error = null) =>
        Volatile.Write(ref _status, new WorkerStatus(state, error));

    private static void StopWorker(ref IWorkerProcess? worker)
    {
        var owned = worker;
        worker = null;
        if (owned is null) return;
        try { owned.Terminate(); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally { owned.Dispose(); }
    }
}
