namespace RemoteControl.Infrastructure.Execution;

internal interface IRuntimeSession : IDisposable
{
    Task<RuntimeResult> ExecuteAsync(string code, CancellationToken cancellationToken);
}

internal sealed class SessionRegistry : IAsyncDisposable
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private bool _disposed;

    public async Task<RuntimeResult> RunAsync(string key, string? environment, Func<CancellationToken, Task<IRuntimeSession>> create, string code, CancellationToken cancellationToken)
    {
        Entry entry;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.TryGetValue(key, out entry!))
            {
                if (environment is not null && !StringComparer.OrdinalIgnoreCase.Equals(entry.Environment, environment))
                    return new(false, "", "", -1, "session_environment_mismatch");
            }
            else _entries.Add(key, entry = new Entry(environment, create));
        }
        var result = await entry.RunAsync(code, cancellationToken);
        if (result.SessionClosed)
        {
            lock (_sync)
            {
                if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry)) _entries.Remove(key);
            }
            await entry.CloseAsync();
        }
        return result;
    }

    public async Task<bool> CloseAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Entry? entry;
        lock (_sync)
        {
            if (!_entries.Remove(key, out entry)) return false;
        }
        await entry.CloseAsync();
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        Entry[] entries;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            entries = _entries.Values.ToArray();
            _entries.Clear();
        }
        await Task.WhenAll(entries.Select(x => x.CloseAsync()));
    }

    private sealed class Entry(string? environment, Func<CancellationToken, Task<IRuntimeSession>> create)
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly CancellationTokenSource _lifetime = new();
        private IRuntimeSession? _session;
        private int _closed;
        public string? Environment { get; } = environment;

        public async Task<RuntimeResult> RunAsync(string code, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            try { await _gate.WaitAsync(linked.Token); }
            catch (OperationCanceledException) { return new(false, "", "", -1, "execution_timeout"); }
            try
            {
                if (Volatile.Read(ref _closed) != 0) return new(false, "", "", -1, "session_closed", true);
                linked.Token.ThrowIfCancellationRequested();
                _session ??= await create(linked.Token);
                var result = await _session.ExecuteAsync(code, linked.Token);
                if (result.SessionClosed) Interlocked.Exchange(ref _closed, 1);
                return result;
            }
            catch (OperationCanceledException)
            {
                Interlocked.Exchange(ref _closed, 1);
                return new(false, "", "", -1, "execution_timeout", true);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _closed, 1);
                return new(false, "", "", -1, ex.Message, true);
            }
            finally { _gate.Release(); }
        }

        public async Task CloseAsync()
        {
            Interlocked.Exchange(ref _closed, 1);
            await _lifetime.CancelAsync();
            await _gate.WaitAsync();
            try { _session?.Dispose(); _session = null; }
            finally { _gate.Release(); }
            // Do not dispose a semaphore while a caller holding this entry may await it.
        }
    }
}
