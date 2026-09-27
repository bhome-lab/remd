namespace RemoteControl.Infrastructure.Execution;

internal static class RuntimeLocator
{
    public static string? Find(string name)
    {
        var extensions = OperatingSystem.IsWindows() && !Path.HasExtension(name)
            ? new[] { ".exe", ".cmd", ".bat", "" }
            : new[] { "" };
        var pathValues = new List<string> { Environment.GetEnvironmentVariable("PATH") ?? "" };
        if (OperatingSystem.IsWindows())
        {
            // Refresh both registry-backed PATH values on every call. The worker need not restart
            // when Node.js or Python is installed after it was launched.
            pathValues.Add(Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "");
            pathValues.Add(Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "");
        }
        foreach (var directory in pathValues.SelectMany(value => value.Split(Path.PathSeparator)))
        {
            var root = Environment.ExpandEnvironmentVariables(directory.Trim('"'));
            if (string.IsNullOrWhiteSpace(root)) continue;
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(root, name + extension);
                if (File.Exists(candidate) && !candidate.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
        }

        if (!OperatingSystem.IsWindows()) return null;
        if (name.Equals("python", StringComparison.OrdinalIgnoreCase))
        {
            // Search only the current user's installation and machine installations.
            var roots = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            };
            foreach (var root in roots)
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var directory in Directory.EnumerateDirectories(root, "Python*"))
                    {
                        var candidate = Path.Combine(directory, "python.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }
        else if (name is "node" or "npm.cmd")
        {
            var candidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", name == "node" ? "node.exe" : name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
