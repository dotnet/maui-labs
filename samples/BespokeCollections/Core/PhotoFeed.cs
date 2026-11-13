namespace BespokeCollections.Core;

public sealed class PhotoFeed : IDisposable
{
    readonly object _gate = new();
    readonly PhotoRepository _repository;
    readonly Func<Action, Task> _publish;
    readonly int _maximumItems;
    CancellationTokenSource _generation = new();
    TaskCompletionSource? _operation;
    PhotoFeedState _state = PhotoFeedState.Empty;
    long _epoch;
    bool _disposed;

    public PhotoFeed(PhotoRepository repository, Func<Action, Task> publish, int maximumItems = 500)
    {
        if (maximumItems < repository.PageSize)
            throw new ArgumentOutOfRangeException(nameof(maximumItems));
        _repository = repository;
        _publish = publish;
        _maximumItems = maximumItems;
    }

    public event EventHandler? Changed;
    public PhotoFeedState State { get { lock (_gate) return _state; } }

    public Task RefreshAsync(string query = "0") => StartLoad(query, true);
    public Task LoadNextAsync() => StartLoad(null, false);

    Task StartLoad(string? query, bool refresh)
    {
        TaskCompletionSource completion;
        CancellationToken token;
        PhotoFeedState start;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!refresh && _operation is not null && !_operation.Task.IsCompleted)
                return _operation.Task;
            if (!refresh && !_state.HasMore)
                return Task.CompletedTask;
            if (refresh)
            {
                _generation.Cancel();
                _generation.Dispose();
                _generation = new();
                _epoch++;
                start = PhotoFeedState.Empty with { Epoch = _epoch, Query = query ?? "0" };
            }
            else
                start = _state;
            token = _generation.Token;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _operation = completion;
        }
        _ = RunLoadAsync(start, token, completion);
        return completion.Task;
    }

    async Task RunLoadAsync(PhotoFeedState start, CancellationToken token, TaskCompletionSource completion)
    {
        try
        {
            await PublishAsync(start.Epoch, start with { IsLoading = true, Error = null }).ConfigureAwait(false);
            var page = await _repository.LoadPageAsync(start.Query, start.NextCursor, token).ConfigureAwait(false);
            var currentKeys = start.Items.Select(item => item.Id).ToHashSet();
            if (page.Items.Any(item => currentKeys.Contains(item.Id)))
                throw new InvalidDataException("A page repeats a retained photo key.");
            var all = start.Items.Concat(page.Items).ToArray();
            var evicted = Math.Max(0, all.Length - _maximumItems);
            var retained = Array.AsReadOnly(all.Skip(evicted).ToArray());
            await PublishAsync(start.Epoch, start with
            {
                Items = retained,
                NextCursor = page.NextCursor,
                IsLoading = false,
                HasMore = page.NextCursor is not null,
                Error = null,
                PagesLoaded = start.PagesLoaded + 1,
                EvictedItems = start.EvictedItems + evicted
            }).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            completion.TrySetResult();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            completion.TrySetCanceled(token);
        }
        catch (Exception exception)
        {
            try
            {
                await PublishAsync(start.Epoch, start with { IsLoading = false, Error = exception.Message }).ConfigureAwait(false);
            }
            catch (Exception publishException)
            {
                completion.TrySetException(new AggregateException(exception, publishException));
                return;
            }
            completion.TrySetException(exception);
        }
        finally
        {
            lock (_gate)
                if (ReferenceEquals(_operation, completion))
                    _operation = null;
        }
    }

    Task PublishAsync(long epoch, PhotoFeedState state) => _publish(() =>
    {
        lock (_gate)
        {
            if (_disposed || _epoch != epoch)
                return;
            _state = state;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    });

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _epoch++;
            _generation.Cancel();
            _generation.Dispose();
            Changed = null;
        }
    }
}
