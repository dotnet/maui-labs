namespace AndroidX.Compose;

/// <summary>
/// Compose-side bridge helpers for
/// <see cref="IStateFlow{T}"/> — the C# parity of Kotlin's
/// <c>androidx.lifecycle.compose.collectAsStateWithLifecycle</c>
/// extensions on <c>Flow&lt;T&gt;</c> / <c>StateFlow&lt;T&gt;</c>.
/// </summary>
public static class StateFlowExtensions
{
    /// <summary>
    /// Returns a read-only <see cref="IState{T}"/> that reflects
    /// <paramref name="flow"/>'s current value and triggers
    /// recomposition when the flow emits. Call inside a composition
    /// body to subscribe the surrounding scope to the flow's
    /// snapshot version counter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Lifecycle parity caveat:</strong> Kotlin's
    /// <c>collectAsStateWithLifecycle</c> only collects while the
    /// host <c>LifecycleOwner</c> is at least
    /// <c>Lifecycle.State.STARTED</c>, pausing collection on
    /// background. AndroidX.Compose's flow is snapshot-backed and always
    /// "live" — there is no separate coroutine to start or stop —
    /// so this method is effectively
    /// <c>collectAsState</c> with a name that signals intent. The
    /// behavioural difference is invisible to the typical UDF
    /// view-model pattern (the flow's value is just a field; the
    /// host doesn't pay for background updates because there are
    /// none to pay for).
    /// </para>
    /// <para>
    /// The returned <see cref="IState{T}"/> is the same instance
    /// as the flow itself, narrowed to its read-only interface —
    /// no extra allocation, no extra subscription bookkeeping.
    /// </para>
    /// </remarks>
    public static IState<T> CollectAsStateWithLifecycle<T>(this IStateFlow<T> flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        // Reading Value here registers the surrounding composition
        // scope with the underlying tick counter, mirroring the
        // dependency-tracking effect of Kotlin's
        // `flow.collectAsStateWithLifecycle()`. Subsequent reads
        // through the returned IState<T> also subscribe — first
        // read is just the eager one.
        _ = flow.Value;
        return flow;
    }

    /// <summary>
    /// Overload accepting an <paramref name="initialValue"/> for
    /// API parity with Kotlin's <c>Flow&lt;T&gt;</c>-typed
    /// <c>collectAsStateWithLifecycle(initialValue)</c>. AndroidX.Compose
    /// <see cref="IStateFlow{T}"/> always has a current value, so
    /// the initial value is ignored — the flow's actual current
    /// value wins on first read.
    /// </summary>
    public static IState<T> CollectAsStateWithLifecycle<T>(this IStateFlow<T> flow, T initialValue)
    {
        _ = initialValue;
        return CollectAsStateWithLifecycle(flow);
    }
}
