namespace AndroidX.Compose;

/// <summary>
/// Read-only observable value that always has a current value —
/// AndroidX.Compose's C# parity for Kotlin's
/// <c>kotlinx.coroutines.flow.StateFlow&lt;T&gt;</c>. Exposes
/// <see cref="IState{T}.Value"/> (always populated) and participates
/// in Compose's snapshot dependency tracking when read inside a
/// composition body.
/// </summary>
/// <typeparam name="T">The observed value type.</typeparam>
/// <remarks>
/// <para>
/// <strong>Implementation surface is closed.</strong> The only
/// supported implementation is the snapshot-backed
/// <see cref="MutableStateFlow{T}"/>. AndroidX.Compose does not bridge to
/// real Kotlin <c>StateFlow</c> instances yet — a custom managed
/// implementation cannot participate in Compose's snapshot tracking
/// without going through <see cref="MutableState{T}"/> /
/// <see cref="MutableNumberState{T}"/>, which
/// <see cref="MutableStateFlow{T}"/> already does. Treat this
/// interface as a documentation marker that "this read-only state
/// originated from a view-model flow"; collect it via
/// <see cref="StateFlowExtensions.CollectAsStateWithLifecycle{T}(IStateFlow{T})"/>.
/// </para>
/// </remarks>
public interface IStateFlow<out T> : IState<T>
{
}
