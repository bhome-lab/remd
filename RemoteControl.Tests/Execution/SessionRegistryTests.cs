using RemoteControl.Infrastructure.Execution;
using Xunit;

namespace RemoteControl.Tests.Execution;

public sealed class SessionRegistryTests
{
    [Fact]
    public async Task Concurrent_first_calls_create_exactly_one_owned_session()
    {
        await using var registry = new SessionRegistry();
        var created = 0;
        var session = new FakeSession();
        Task<IRuntimeSession> Create(CancellationToken _) { Interlocked.Increment(ref created); return Task.FromResult<IRuntimeSession>(session); }
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => registry.RunAsync("python:x", null, Create, "", default)));
        Assert.Equal(1, created);
        Assert.Equal(1, session.MaximumConcurrentCalls);
        Assert.True(await registry.CloseAsync("python:x", default));
        Assert.Equal(1, session.Disposals);
    }

    [Fact]
    public async Task Close_cancels_active_call_and_disposes_after_it_finishes()
    {
        await using var registry = new SessionRegistry();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new FakeSession(entered);
        var running = registry.RunAsync("python:x", null, _ => Task.FromResult<IRuntimeSession>(session), "wait", default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var close = registry.CloseAsync("python:x", default);
        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("execution_timeout", result.Error);
        Assert.True(await close);
        Assert.Equal(1, session.Disposals);
        Assert.False(session.DisposedWhileRunning);
    }

    [Fact]
    public async Task Expired_queued_call_does_not_destroy_the_running_session()
    {
        await using var registry = new SessionRegistry();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new FakeSession(entered);
        var active = registry.RunAsync("python:x", null, _ => Task.FromResult<IRuntimeSession>(session), "wait", default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var timeout = new CancellationTokenSource(30);
        var queued = await registry.RunAsync("python:x", null, _ => throw new InvalidOperationException(), "", timeout.Token);
        Assert.Equal("execution_timeout", queued.Error);
        Assert.False(queued.SessionClosed);
        Assert.Equal(0, session.Disposals);
        await registry.CloseAsync("python:x", default);
        await active;
    }

    private sealed class FakeSession(TaskCompletionSource? entered = null) : IRuntimeSession
    {
        private int _active;
        public int MaximumConcurrentCalls, Disposals;
        public bool DisposedWhileRunning;
        public async Task<RuntimeResult> ExecuteAsync(string code, CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            MaximumConcurrentCalls = Math.Max(MaximumConcurrentCalls, active);
            try
            {
                if (code == "wait") { entered!.SetResult(); await Task.Delay(Timeout.Infinite, cancellationToken); }
                else await Task.Delay(2, cancellationToken);
                return new(true, "", "", 0);
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public void Dispose() { DisposedWhileRunning |= _active != 0; Interlocked.Increment(ref Disposals); }
    }
}
