using System.Diagnostics;

namespace RemoteControl.Infrastructure.Input;

internal static class FakerInputDiscovery
{
    public static bool DevicePresent()
    {
        var output = RunPnPUtil("/enum-devices", "/connected", "/deviceid", "ROOT\\FakerInput");
        return output is not null && output.Contains("FakerInput", StringComparison.OrdinalIgnoreCase)
            && !output.Contains("No devices were found", StringComparison.OrdinalIgnoreCase);
    }

    public static bool DriverPackageSigned()
    {
        var output = RunPnPUtil("/enum-drivers");
        if (output is null) return false;
        foreach (var block in output.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!block.Contains("FakerInput", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var line in block.Split('\n'))
            {
                if (!line.Contains("Signer Name", StringComparison.OrdinalIgnoreCase)) continue;
                var value = line[(line.IndexOf(':') + 1)..].Trim();
                return value.Length > 0 && !value.Equals("None", StringComparison.OrdinalIgnoreCase)
                    && !value.Contains("unsigned", StringComparison.OrdinalIgnoreCase);
            }
        }
        return false;
    }

    public static IEnumerable<string> WrapperCandidates(string? configuredPath)
    {
        var configured = configuredPath ?? Environment.GetEnvironmentVariable("FAKERINPUT_WRAPPER_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) yield return Path.GetFullPath(configured);
        yield return Path.Combine(AppContext.BaseDirectory, "FakerInputWrapper.dll");
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData })
        {
            var root = Environment.GetFolderPath(folder);
            if (root.Length > 0) yield return Path.Combine(root, "DS4Windows", "libs", "x64", "FakerInputWrapper", "FakerInputWrapper.dll");
        }
    }

    private static string? RunPnPUtil(params string[] arguments)
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "pnputil.exe"),
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try
        {
            if (!process.Start()) return null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
            Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
            return process.ExitCode == 0 ? stdout.Result + stderr.Result : null;
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            return null;
        }
    }
}
