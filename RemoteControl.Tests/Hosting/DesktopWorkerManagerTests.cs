using System.Collections.Concurrent;
using RemoteControl.Service.Hosting;

namespace RemoteControl.Tests.Hosting;

public sealed class DesktopWorkerManagerTests
{
    [Fact]
    public async Task RunningRequiresPipeHandshake()
    {
        var launcher = new FakeLauncher { Session = 1 };
        var ready = 0;
        using var manager = CreateManager(launcher, () => Volatile.Read(ref ready) == 1);
        await manager.StartAsync(default);
        try
        {
            await UntilAsync(() => launcher.Started.Count == 1);
            Assert.Equal("starting", manager.Status.State);
            Volatile.Write(ref ready, 1);
            await UntilAsync(() => manager.Status.State == "running");
            Assert.Single(launcher.Started);
        }
        finally { await manager.StopAsync(default); }
    }

    [Fact]
    public async Task SwitchingSessionStopsOldWorkerAndRequiresNewHandshake()
    {
        var launcher = new FakeLauncher { Session = 1 };
        var ready = 1;
        using var manager = CreateManager(launcher, () => Volatile.Read(ref ready) == 1);
        await manager.StartAsync(default);
        try
        {
            await UntilAsync(() => manager.Status.State == "running");
            var oldWorker = Assert.Single(launcher.Started).Worker;
            Volatile.Write(ref ready, 0);
            launcher.Session = 2;
            await UntilAsync(() => launcher.Started.Count == 2);
            Assert.True(oldWorker.Terminated);
            Assert.True(oldWorker.Disposed);
            Assert.Equal(2, launcher.Started.Last().Session);
            Assert.Equal("starting", manager.Status.State);
            Volatile.Write(ref ready, 1);
            await UntilAsync(() => manager.Status.State == "running");
        }
        finally { await manager.StopAsync(default); }
    }

    [Fact]
    public async Task LogoutStopsWorkerAndReportsUnavailable()
    {
        var launcher = new FakeLauncher { Session = 1 };
        using var manager = CreateManager(launcher, () => true);
        await manager.StartAsync(default);
        try
        {
            await UntilAsync(() => manager.Status.State == "running");
            launcher.Session = null;
            await UntilAsync(() => manager.Status.State == "unavailable");
            Assert.True(Assert.Single(launcher.Started).Worker.Disposed);
            Assert.Equal("interactive_session_unavailable", manager.Status.Error);
        }
        finally { await manager.StopAsync(default); }
    }

    [Fact]
    public async Task ExitedWorkerIsReplacedAndStatusReadsNeverTouchDisposedHandle()
    {
        var launcher = new FakeLauncher { Session = 1 };
        using var manager = CreateManager(launcher, () => true);
        await manager.StartAsync(default);
        try
        {
            await UntilAsync(() => manager.Status.State == "running");
            var first = Assert.Single(launcher.Started).Worker;
            first.Exit();
            await UntilAsync(() => launcher.Started.Count == 2);
            await UntilAsync(() => manager.Status.State == "running");
            Assert.True(first.Disposed);

            var reader = Task.Run(() =>
            {
                for (var i = 0; i < 50_000; i++) Assert.NotNull(manager.Status.State);
            });
            await manager.StopAsync(default);
            await reader;
            Assert.All(launcher.Started, item => Assert.True(item.Worker.Disposed));
            Assert.Equal("unavailable", manager.Status.State);
        }
        finally { await manager.StopAsync(default); }
    }

    private static DesktopWorkerManager CreateManager(FakeLauncher launcher, Func<bool> ready) =>
        new(launcher, $"test-{Guid.NewGuid():N}", (_, _) => Task.FromResult(ready()), TimeSpan.FromMilliseconds(5));

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }

    private sealed class FakeLauncher : IWorkerProcessLauncher
    {
        private int _session = -1;
        public int? Session
        {
            get { var value = Volatile.Read(ref _session); return value < 0 ? null : value; }
            set => Volatile.Write(ref _session, value ?? -1);
        }
        public ConcurrentQueue<(int Session, FakeWorker Worker)> Started { get; } = new();
        public int? GetActiveSessionId() => Session;
        public IWorkerProcess Start(int sessionId, string pipeName)
        {
            var process = new FakeWorker();
            Started.Enqueue((sessionId, process));
            return process;
        }
    }

    private sealed class FakeWorker : IWorkerProcess
    {
        private int _exited, _disposed, _terminated;
        public bool Disposed => Volatile.Read(ref _disposed) == 1;
        public bool Terminated => Volatile.Read(ref _terminated) == 1;
        public bool HasExited
        {
            get
            {
                ObjectDisposedException.ThrowIf(Disposed, this);
                return Volatile.Read(ref _exited) == 1;
            }
        }
        public void Exit() => Volatile.Write(ref _exited, 1);
        public void Terminate() { Volatile.Write(ref _terminated, 1); Exit(); }
        public void Dispose() => Volatile.Write(ref _disposed, 1);
    }
}
