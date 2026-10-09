using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Read-only typed adapter around the read-only Compose
/// <c>androidx.compose.runtime.State&lt;T&gt;</c> returned by
/// <c>androidx.lifecycle.compose.collectAsStateWithLifecycle(...)</c>.
/// The wrapped state is itself a snapshot-aware Compose state — reading
/// <see cref="Value"/> from inside a composition subscribes the calling
/// scope, and the lifecycle-aware Kotlin collector publishes a new value
/// every time the source <c>Flow</c>/<c>StateFlow</c> emits while the
/// hosting <c>LifecycleOwner</c> is at least at the requested minimum
/// active state.
/// </summary>
/// <typeparam name="T">
/// Caller-asserted element type of the original Kotlin
/// <c>Flow&lt;T&gt;</c>/<c>StateFlow&lt;T&gt;</c>. Because the bound
/// <c>IFlow</c>/<c>IStateFlow</c> interfaces are non-generic on the
/// .NET side (Kotlin generic type parameters are erased through JNI),
/// this is an unchecked assertion — if the underlying flow actually
/// emits a different boxed type, the <see cref="Value"/> getter will
/// throw when it tries to unwrap it.
/// </typeparam>
/// <remarks>
/// Use the
/// <see cref="ComposeExtensions.CollectAsStateWithLifecycle{T}(Xamarin.KotlinX.Coroutines.Flow.IStateFlow, Runtime.IComposer)"/>
/// or
/// <see cref="ComposeExtensions.CollectAsStateWithLifecycle{T}(Xamarin.KotlinX.Coroutines.Flow.IFlow, T, Runtime.IComposer)"/>
/// extension methods to obtain instances. The ctor is internal because
/// the wrapped <see cref="IState"/> must come from the lifecycle-aware
/// Kotlin collector to be subscribed to its <see cref="ILifecycleOwner"/>
/// correctly.
/// </remarks>
public sealed class CollectedState<T> : IState<T>
{
    readonly IState _state;

    internal CollectedState(IState state) => _state = state;

    /// <summary>
    /// The latest value collected from the underlying Kotlin flow.
    /// Reading from a composition scope subscribes that scope so it
    /// recomposes when a new value is emitted (while the
    /// <see cref="ILifecycleOwner"/> is in the configured active state).
    /// </summary>
    public T Value => MutableState<T>.FromJava(_state.Value);

    /// <summary>
    /// Returns the underlying value's string representation so
    /// <c>$"...{state}..."</c> interpolation reads as Kotlin would
    /// (a null value renders as <c>"null"</c>).
    /// </summary>
    public override string ToString() => Value?.ToString() ?? "null";
}
