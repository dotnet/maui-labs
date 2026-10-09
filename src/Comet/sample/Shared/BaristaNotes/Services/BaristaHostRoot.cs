using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace CometBaristaNotes.Services;

public sealed class BaristaHostRoot<T>(
    Action detachNative,
    Func<Task> shutdownPlatform,
    Func<Task>? waitForIdle = null) : IAsyncDisposable where T : class, IAsyncDisposable
{
    readonly object _gate = new();
    Task? _teardown;
    bool _releaseStarted;

    public T? Root { get; private set; }

    public T Create(Func<T> create)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_releaseStarted, this);
            if (Root is not null)
                throw new InvalidOperationException("This host already owns a Barista root.");
            return Root = create();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _releaseStarted = true;
            return new ValueTask(_teardown ??= ReleaseAsync());
        }
    }

    async Task ReleaseAsync()
    {
        var failures = new List<Exception>();
        if (waitForIdle is not null)
        {
            try { await waitForIdle(); }
            catch (Exception error) { failures.Add(error); }
        }

        try { detachNative(); }
        catch (Exception error) { failures.Add(error); }

        var root = Root;
        Root = null;
        if (root is not null)
        {
            try { await root.DisposeAsync(); }
            catch (Exception error) { failures.Add(error); }
        }

        try { await shutdownPlatform(); }
        catch (Exception error) { failures.Add(error); }

        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1)
            throw new AggregateException("Barista host teardown failed.", failures);
    }
}
