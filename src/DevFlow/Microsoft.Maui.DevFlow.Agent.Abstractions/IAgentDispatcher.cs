namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Framework-neutral abstraction over a UI thread dispatcher.
/// </summary>
/// <remarks>
/// The shape intentionally mirrors <c>Microsoft.Maui.Dispatching.IDispatcher</c> so the MAUI
/// backend can adapt its dispatcher with a trivial wrapper, while native (non-MAUI) backends
/// supply their own implementation over <c>Handler</c>, <c>UIApplication</c> or <c>NSApplication</c>.
/// </remarks>
public interface IAgentDispatcher
{
    /// <summary>
    /// Gets a value indicating whether the calling thread requires a dispatch to reach the UI thread.
    /// </summary>
    bool IsDispatchRequired { get; }

    /// <summary>
    /// Queues the supplied action to run on the UI thread.
    /// </summary>
    bool Dispatch(Action action);

    /// <summary>
    /// Queues the supplied action to run on the UI thread after <paramref name="delay"/> elapses.
    /// </summary>
    bool DispatchDelayed(TimeSpan delay, Action action)
        => AgentDispatcherDefaults.DispatchDelayed(this, delay, action);
}

// Single source of truth for the default delayed-dispatch behaviour. Both the IAgentDispatcher
// default interface method and the DelegateAgentDispatcher concrete override delegate here so the
// two entry points cannot drift, while DelegateAgentDispatcher keeps a concrete-type
// DispatchDelayed for callers that hold a DelegateAgentDispatcher-typed reference (a default
// interface method is only reachable through the interface). Dispatch resolves virtually against
// the supplied instance, preserving each backend's own Dispatch behaviour.
internal static class AgentDispatcherDefaults
{
    internal static bool DispatchDelayed(IAgentDispatcher dispatcher, TimeSpan delay, Action action)
    {
        _ = Task.Delay(delay).ContinueWith(_ => dispatcher.Dispatch(action), TaskScheduler.Default);
        return true;
    }
}

/// <summary>
/// Adapts a delegate pair into an <see cref="IAgentDispatcher"/>.
/// </summary>
public sealed class DelegateAgentDispatcher(Func<bool> isDispatchRequired, Action<Action> dispatch) : IAgentDispatcher
{
    private readonly Func<bool> _isDispatchRequired = isDispatchRequired ?? throw new ArgumentNullException(nameof(isDispatchRequired));
    private readonly Action<Action> _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));

    /// <inheritdoc />
    public bool IsDispatchRequired
    {
        get
        {
            try { return _isDispatchRequired(); }
            catch { return false; }
        }
    }

    /// <inheritdoc />
    public bool Dispatch(Action action)
    {
        _dispatch(action);
        return true;
    }

    /// <inheritdoc />
    public bool DispatchDelayed(TimeSpan delay, Action action)
        => AgentDispatcherDefaults.DispatchDelayed(this, delay, action);
}
