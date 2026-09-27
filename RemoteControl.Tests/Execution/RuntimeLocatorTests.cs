using RemoteControl.Infrastructure.Execution;

namespace RemoteControl.Tests.Execution;

public sealed class RuntimeLocatorTests
{
    [Fact]
    public void Finds_runtime_installed_after_first_lookup_without_restarting()
    {
        var directory = Path.Combine(Path.GetTempPath(), "remd-runtime-" + Guid.NewGuid().ToString("N"));
        var name = "runtime-" + Guid.NewGuid().ToString("N");
        var executable = Path.Combine(directory, name + (OperatingSystem.IsWindows() ? ".exe" : ""));
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        Directory.CreateDirectory(directory);
        try
        {
            Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + originalPath);
            Assert.Null(RuntimeLocator.Find(name));
            File.WriteAllBytes(executable, []);
            Assert.Equal(executable, RuntimeLocator.Find(name));
            File.Delete(executable);
            Assert.Null(RuntimeLocator.Find(name));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Directory.Delete(directory);
        }
    }
}
