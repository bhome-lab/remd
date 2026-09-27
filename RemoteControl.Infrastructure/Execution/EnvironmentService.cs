using System.Collections.Concurrent;

namespace RemoteControl.Infrastructure.Execution;

public sealed class EnvironmentService : IEnvironmentService, IDisposable
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    internal string Root { get; }

    public EnvironmentService() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteControl", "envs")) { }
    internal EnvironmentService(string root) => Root = root;

    public Task<string[]> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.FromResult(Directory.Exists(Root)
            ? Directory.GetDirectories(Root).Select(Path.GetFileName).OfType<string>().Order().ToArray()
            : []);
    }

    internal string PathFor(string name) => Path.Combine(Root, RuntimeNames.EnvironmentName(name));
    internal string PythonFor(string name) => OperatingSystem.IsWindows()
        ? Path.Combine(PathFor(name), "Scripts", "python.exe")
        : Path.Combine(PathFor(name), "bin", "python");
    internal bool Exists(string runtime, string name) => runtime switch
    {
        "python" => File.Exists(Path.Combine(PathFor(name), "pyvenv.cfg")) && File.Exists(PythonFor(name)),
        "nodejs" => File.Exists(Path.Combine(PathFor(name), "package.json")),
        _ => false
    };

    public async Task<EnvironmentResult> EnsureAsync(string runtime, string name, string[] packages, CancellationToken cancellationToken)
    {
        var path = "";
        runtime = RuntimeNames.Normalize(runtime);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            name = RuntimeNames.EnvironmentName(name);
            path = PathFor(name);
            if (runtime is not ("python" or "nodejs")) return new(false, runtime, name, path, [], "runtime_not_found");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            var gate = _gates.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(linked.Token);
            try
            {
                Directory.CreateDirectory(Root);
                if (runtime == "python")
                {
                    if (!Exists(runtime, name))
                        await RunAsync(RuntimeLocator.Find("python") ?? "python", ["-m", "venv", path], Root, linked.Token);
                    if (packages.Length > 0)
                        await RunAsync(PythonFor(name), ["-m", "pip", "install", "--disable-pip-version-check", .. packages], path, linked.Token);
                }
                else
                {
                    Directory.CreateDirectory(path);
                    var npm = RuntimeLocator.Find(OperatingSystem.IsWindows() ? "npm.cmd" : "npm") ?? "npm";
                    if (!Exists(runtime, name)) await RunAsync(npm, ["init", "-y"], path, linked.Token);
                    if (packages.Length > 0) await RunAsync(npm, ["install", .. packages], path, linked.Token);
                }
                return new(true, runtime, name, path, packages);
            }
            finally { gate.Release(); }
        }
        catch (OperationCanceledException) { return new(false, runtime, name, path, packages, "execution_timeout"); }
        catch (Exception ex) { return new(false, runtime, name, path, packages, ex.Message); }
    }

    private static async Task RunAsync(string file, string[] arguments, string directory, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(ProcessRunner.StartInfo(file, arguments, directory), cancellationToken);
        if (!result.Ok)
        {
            if (result.Error == "execution_timeout") throw new OperationCanceledException(cancellationToken);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Stderr) ? result.Error : result.Stderr.Trim());
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        // Gates and lifetime token can still be observed by requests already in flight.
    }
}
