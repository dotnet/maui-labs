using System;

namespace Comet.Backend;

internal sealed class RootCallbackLifetime : IDisposable
{
    readonly object _gate = new();
    bool _closed;

    public bool Run(Action callback)
    {
        lock (_gate)
        {
            if (_closed)
                return false;
            callback();
            return true;
        }
    }

    public void ThrowIfClosed()
    {
        lock (_gate)
            ObjectDisposedException.ThrowIf(_closed, this);
    }

    public void Dispose()
    {
        // Wait for an in-flight flush before the host disposes its logical/native roots.
        lock (_gate)
            _closed = true;
    }
}
