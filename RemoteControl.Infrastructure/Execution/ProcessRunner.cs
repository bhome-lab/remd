using System.Diagnostics;
using System.Text;

namespace RemoteControl.Infrastructure.Execution;

internal sealed record RuntimeResult(bool Ok, string Stdout, string Stderr, int ExitCode, string? Error = null, bool SessionClosed = false);

internal static class ProcessRunner
{
    public static ProcessStartInfo StartInfo(string executable, IEnumerable<string> arguments, string workingDirectory)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    public static async Task<RuntimeResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var owner = OwnedProcess.Start(startInfo);
        using var stop = cancellationToken.Register(owner.Stop);
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var outputTask = ReadAsync(owner.Process.StandardOutput, stdout, cancellationToken);
        var errorTask = ReadAsync(owner.Process.StandardError, stderr, cancellationToken);
        var waitTask = owner.Process.WaitForExitAsync(cancellationToken);
        try
        {
            await Task.WhenAll(waitTask, outputTask, errorTask);
            cancellationToken.ThrowIfCancellationRequested();
            var exitCode = owner.Process.ExitCode;
            owner.DetachChildren();
            return new(exitCode == 0, stdout.ToString(), stderr.ToString(), exitCode, exitCode == 0 ? null : "operation_failed");
        }
        catch (OperationCanceledException)
        {
            owner.Stop();
            await ObserveAsync(waitTask, outputTask, errorTask);
            return new(false, stdout.ToString(), stderr.ToString(), -1, "execution_timeout");
        }
        catch
        {
            owner.Stop();
            await ObserveAsync(waitTask, outputTask, errorTask);
            throw;
        }
    }

    private static async Task ReadAsync(StreamReader reader, StringBuilder output, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) return;
            output.Append(buffer, 0, read);
        }
    }

    internal static async Task ObserveAsync(params Task[] tasks)
    {
        foreach (var task in tasks)
        {
            try { await task; }
            catch (Exception) { /* Every owned I/O task is observed during shutdown. */ }
        }
    }
}
