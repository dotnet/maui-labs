
namespace AndroidX.Compose;

/// <summary>
/// Mutable, snapshot-backed observable value — the C# parity of
/// Kotlin's <c>kotlinx.coroutines.flow.MutableStateFlow&lt;T&gt;</c>.
/// Designed to live on a <see cref="ViewModel"/> and be
/// observed from composables via
/// <see cref="StateFlowExtensions.CollectAsStateWithLifecycle{T}(IStateFlow{T})"/>.
/// </summary>
/// <typeparam name="T">The observed value type. Arbitrary CLR
/// references (records, classes, value types) are supported — unlike
/// <see cref="MutableState{T}"/>, the value is held in a plain managed
/// field and never round-tripped through a JVM box.</typeparam>
/// <remarks>
/// <para>
/// <strong>Snapshot-backed, not real Kotlin StateFlow:</strong>
/// internally this couples a managed field with a
/// <see cref="MutableNumberState{T}"/>-based version counter (the
/// same pattern <see cref="MutableStateList{T}"/> uses). Reads inside
/// a composition body touch the counter so the surrounding scope
/// subscribes to changes; writes that flip the value increment the
/// counter and Compose triggers recomposition. Setting the value to
/// something equal to the current one (per
/// <see cref="EqualityComparer{T}.Default"/>) is a no-op, matching
/// <c>MutableStateFlow.value</c>'s distinct-conflation contract.
/// </para>
/// <para>
/// <strong>What is intentionally missing:</strong> no
/// <c>collect</c> coroutine, no <c>SharingStarted</c>, no
/// <c>replayCache</c>, no subscriber count. The only consumption
/// path is from inside a composition; if you need a true
/// asynchronous flow, use <see cref="ComposeExtensions.SnapshotFlow{T}"/> over
/// the underlying state.
/// </para>
/// <para>
/// <strong>Threading:</strong> safe to <em>read</em>
/// <see cref="Value"/> from any thread (the backing snapshot state
/// supports it). Mutating <see cref="Value"/> /
/// <see cref="TryEmit(T)"/> from a background thread is supported in
/// the same sense Kotlin's <c>MutableStateFlow</c> supports it —
/// the change is visible to the next snapshot read. <see cref="Update"/>
/// is serialised under an instance lock to prevent lost updates
/// when concurrent transforms race; it is NOT a CAS loop and does
/// NOT guarantee the transform sees the same value the read above
/// it saw.
/// </para>
/// </remarks>
public sealed class MutableStateFlow<T> : IStateFlow<T>
{
    readonly object _lock = new();
    readonly MutableNumberState<int> _tick = new(0);
    T _value;

    /// <summary>Creates a new flow seeded with <paramref name="initialValue"/>.</summary>
    public MutableStateFlow(T initialValue) => _value = initialValue;

    /// <inheritdoc/>
    public T Value
    {
        get
        {
            // Subscribe the current composition (if any) to changes
            // by reading the tick. Outside a composition, this is a
            // plain field read and the subscription is a no-op.
            _ = _tick.Value;
            return _value;
        }
        set
        {
            // Distinct-conflate: writing the same value is a no-op,
            // matching MutableStateFlow.value's contract. The lock
            // makes the read-compare-write-bump atomic so a concurrent
            // setter can't observe a half-updated state AND so the
            // tick increment can't race past a slower writer (two
            // concurrent setters must each produce a distinct tick
            // value, otherwise Compose can miss an invalidation).
            lock (_lock)
            {
                if (EqualityComparer<T>.Default.Equals(_value, value))
                    return;
                _value = value;
                _tick.Value++;
            }
        }
    }

    /// <summary>
    /// Sets <see cref="Value"/> to <paramref name="value"/> and
    /// returns <c>true</c> if the assignment caused an emission
    /// (i.e. the new value was distinct from the current one) or
    /// <c>false</c> if it was conflated. Parity with Kotlin's
    /// <c>MutableStateFlow.tryEmit(value)</c> — which always
    /// succeeds for <c>MutableStateFlow</c> but conflates equal
    /// values.
    /// </summary>
    public bool TryEmit(T value)
    {
        lock (_lock)
        {
            if (EqualityComparer<T>.Default.Equals(_value, value))
                return false;
            _value = value;
            _tick.Value++;
            return true;
        }
    }

    /// <summary>
    /// Atomically applies <paramref name="transform"/> to the
    /// current value and emits the result, returning the new
    /// value. Parity with Kotlin's
    /// <c>MutableStateFlow&lt;T&gt;.update { … }</c>.
    /// </summary>
    /// <remarks>
    /// The transform runs under an instance-level lock, so two
    /// concurrent <see cref="Update"/> calls are guaranteed to
    /// observe each other's writes (no lost updates). The
    /// transform must be side-effect free and short — long
    /// computations under the lock will block readers that observe
    /// <see cref="Value"/> concurrently. If the transform returns
    /// a value equal to the current one, no emission occurs and
    /// no recomposition is triggered.
    /// </remarks>
    public T Update(Func<T, T> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        lock (_lock)
        {
            var next = transform(_value);
            if (EqualityComparer<T>.Default.Equals(_value, next))
                return _value;
            _value = next;
            _tick.Value++;
            return next;
        }
    }
}
