namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>
/// Per-agent reader/writer coordination for connection lifetime.
///
/// Inspector requests take the SHARED side and hold it for the whole proxied call, so a
/// same-ID reconnect or a disconnect cleanup — both EXCLUSIVE — cannot swap or dispose the
/// agent underneath an in-flight request.
///
/// The shared side admits any number of concurrent holders, which is the point: a single long
/// request (a flow replay executes inline for up to two minutes) no longer serializes state
/// reads, screenshots and heartbeats behind it, and a concurrent mutation still reaches
/// <c>InspectorServer.RouteAsync</c> so its replay-in-progress 409 actually applies instead of
/// silently blocking on a mutex.
/// </summary>
internal sealed class AgentLifetimeGate
{
    private readonly SemaphoreSlim _exclusive = new(1, 1);
    private readonly SemaphoreSlim _countGate = new(1, 1);
    private int _shared;

    /// <summary>Admits concurrent request traffic while excluding lifetime changes.</summary>
    public async Task<IAsyncDisposable> EnterSharedAsync(CancellationToken ct)
    {
        await _countGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // The first holder takes the exclusive side on behalf of the whole group; the last
            // one out gives it back.
            if (_shared == 0)
                await _exclusive.WaitAsync(ct).ConfigureAwait(false);
            _shared++;
        }
        finally
        {
            _countGate.Release();
        }

        return new Scope(this, shared: true);
    }

    /// <summary>Waits for all in-flight requests, then blocks new ones for the duration.</summary>
    public async Task<IAsyncDisposable> EnterExclusiveAsync(CancellationToken ct = default)
    {
        await _exclusive.WaitAsync(ct).ConfigureAwait(false);
        return new Scope(this, shared: false);
    }

    private async Task ExitSharedAsync()
    {
        await _countGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (--_shared == 0)
                _exclusive.Release();
        }
        finally
        {
            _countGate.Release();
        }
    }

    private sealed class Scope(AgentLifetimeGate gate, bool shared) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            if (shared)
                await gate.ExitSharedAsync().ConfigureAwait(false);
            else
                gate._exclusive.Release();
        }
    }
}
