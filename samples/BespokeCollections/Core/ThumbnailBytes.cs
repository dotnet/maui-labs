namespace BespokeCollections.Core;

public readonly record struct ThumbnailKey(long Id, int Revision, int Width);

public sealed class ThumbnailBytes : IDisposable
{
    sealed record Entry(byte[] Bytes, long Access);
    sealed class Pending(CancellationToken lifetime)
    {
        public TaskCompletionSource<byte[]> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        public int Consumers { get; set; }
        public bool Complete { get; set; }
    }
    readonly object _gate = new();
    readonly PhotoRepository _repository;
    readonly long _budget;
    readonly SemaphoreSlim _slots;
    readonly CancellationTokenSource _lifetime = new();
    readonly Dictionary<ThumbnailKey, Entry> _cache = [];
    readonly Dictionary<ThumbnailKey, Pending> _pending = [];
    long _bytes;
    long _access;
    int _requests;
    int _hits;
    int _loads;
    bool _disposed;
    bool _resourcesDisposed;

    public ThumbnailBytes(PhotoRepository repository, long budget = 4 * 1024 * 1024, int concurrency = 2)
    {
        if (budget < 512 * 1024 || concurrency is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(budget));
        _repository = repository;
        _budget = budget;
        _slots = new(concurrency);
    }

    public (long Bytes, int Entries, int Pending, int Requests, int Hits) Snapshot
    {
        get { lock (_gate) return (_bytes, _cache.Count, _pending.Count, _requests, _hits); }
    }

    public async Task<byte[]> GetAsync(PhotoItem item, int width, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startLoad = false;
        Pending pending;
        var key = new ThumbnailKey(item.Id, item.Revision, width);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cache.TryGetValue(key, out var entry))
            {
                _hits++;
                _cache[key] = entry with { Access = ++_access };
                return entry.Bytes;
            }
            if (!_pending.TryGetValue(key, out pending!) || pending.Cancellation.IsCancellationRequested)
            {
                if (_pending.Count >= 64 && !_pending.ContainsKey(key))
                    throw new InvalidOperationException("The thumbnail demand queue is full.");
                pending = new(_lifetime.Token);
                _pending[key] = pending;
                _loads++;
                startLoad = true;
            }
            pending.Consumers++;
        }
        if (startLoad)
            _ = LoadAsync(item, key, pending);
        try
        {
            return await pending.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
                if (--pending.Consumers == 0 && !pending.Complete)
                    pending.Cancellation.Cancel();
        }
    }

    async Task LoadAsync(PhotoItem item, ThumbnailKey key, Pending pending)
    {
        var entered = false;
        try
        {
            await _slots.WaitAsync(pending.Cancellation.Token).ConfigureAwait(false);
            entered = true;
            lock (_gate) _requests++;
            var bytes = await _repository.LoadThumbnailAsync(item, key.Width, pending.Cancellation.Token).ConfigureAwait(false);
            pending.Cancellation.Token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                while (_bytes + bytes.Length > _budget && _cache.Count > 0)
                {
                    var oldest = _cache.MinBy(entry => entry.Value.Access);
                    _cache.Remove(oldest.Key);
                    _bytes -= oldest.Value.Bytes.Length;
                }
                _cache[key] = new(bytes, ++_access);
                _bytes += bytes.Length;
            }
            pending.Completion.TrySetResult(bytes);
        }
        catch (OperationCanceledException) when (pending.Cancellation.IsCancellationRequested)
        {
            pending.Completion.TrySetCanceled();
        }
        catch (Exception exception)
        {
            pending.Completion.TrySetException(exception);
            _ = pending.Completion.Task.Exception;
        }
        finally
        {
            if (entered)
                _slots.Release();
            lock (_gate)
            {
                pending.Complete = true;
                if (_pending.TryGetValue(key, out var current) && ReferenceEquals(current, pending))
                    _pending.Remove(key);
                pending.Cancellation.Dispose();
                _loads--;
                if (_disposed && _loads == 0)
                    DisposeResources();
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
            _lifetime.Cancel();
            _cache.Clear();
            _bytes = 0;
            if (_loads == 0)
                DisposeResources();
        }
    }

    void DisposeResources()
    {
        if (_resourcesDisposed)
            return;
        _resourcesDisposed = true;
        _slots.Dispose();
        _lifetime.Dispose();
    }
}
