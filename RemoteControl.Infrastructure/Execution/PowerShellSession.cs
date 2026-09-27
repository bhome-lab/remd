using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace RemoteControl.Infrastructure.Execution;

internal sealed class PowerShellSession : IRuntimeSession
{
    private readonly Runspace _runspace = RunspaceFactory.CreateRunspace();

    public PowerShellSession() => _runspace.Open();

    public async Task<RuntimeResult> ExecuteAsync(string code, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var powershell = PowerShell.Create();
        powershell.Runspace = _runspace;
        powershell.AddScript(code);
        var output = new List<PSObject>();
        using var stop = cancellationToken.Register(() =>
        {
            try { powershell.Stop(); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        });
        string? error = null;
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                powershell.Invoke(null, output);
            }, CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
            if (powershell.HadErrors) error = "operation_failed";
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { error = "execution_timeout"; }
        catch (RuntimeException ex) { error = ex.Message; }
        var stdout = string.Join(Environment.NewLine, output.Select(x => x?.ToString())) + (output.Count == 0 ? "" : Environment.NewLine);
        var stderr = string.Join(Environment.NewLine, powershell.Streams.Error.Select(x => x.ToString()));
        return new(error is null, stdout, stderr, error is null ? 0 : -1, error);
    }

    public void Dispose() => _runspace.Dispose();
}
