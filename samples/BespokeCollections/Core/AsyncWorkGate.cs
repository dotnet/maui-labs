namespace BespokeCollections.Core;

public sealed class AsyncWorkGate : IDisposable
{
    readonly object _gate = new();
    readonly SemaphoreSlim _slots;
    int _active;
    bool _disposed;

    public AsyncWorkGate(int concurrency = 2)
    {
        if (concurrency is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(concurrency));
        _slots = new(concurrency);
    }

    public Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken) =>
        RunAsync<T>(() => Task.Run(work, cancellationToken), cancellationToken);

    public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _active++;
        }
        var entered = false;
        try
        {
            await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            return await work().ConfigureAwait(false);
        }
        finally
        {
            if (entered)
                _slots.Release();
            lock (_gate)
            {
                _active--;
                if (_disposed && _active == 0)
                    _slots.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_active == 0)
                _slots.Dispose();
        }
    }
}
