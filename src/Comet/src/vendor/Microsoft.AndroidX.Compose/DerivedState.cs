using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Read-only state computed from other state, the C# parity for
/// Kotlin's <c>derivedStateOf { … }</c>. Wraps Compose's
/// <c>IState</c> (returned by
/// <c>SnapshotStateKt.DerivedStateOf(IFunction0)</c>) so the value
/// can be read from any composition that holds the instance —
/// reading <see cref="Value"/> subscribes the calling composition
/// scope, and any change to the underlying state read inside the
/// calculation triggers a recomposition of those scopes.
///
/// <code>
/// var name   = Remember(() =&gt; new MutableState&lt;string&gt;("Ada"));
/// var greet  = Remember(() =&gt; ComposeExtensions.DerivedStateOf(() =&gt; $"Hi, {name.Value}!"));
/// // In a Render body:
/// new Text(greet.Value); // recomposes when name.Value changes
/// </code>
///
/// The calculation runs lazily and is memoised by Compose between
/// reads — only re-runs when one of the state values it read changes.
/// </summary>
/// <remarks>
/// Use <see cref="ComposeExtensions.DerivedStateOf{T}(Func{T})"/> to
/// build instances; the constructor is internal because the wrapped
/// <c>IState</c> must come from a Kotlin <c>derivedStateOf</c> call
/// to participate correctly in Compose's snapshot system.
/// </remarks>
public sealed class DerivedState<T> : IState<T>
{
    readonly IState _state;

    internal DerivedState(IState state) => _state = state;

    /// <summary>
    /// The current derived value, recomputed lazily by Compose when
    /// any of the state values read inside the calculation change.
    /// Reading from a composition scope subscribes that scope so it
    /// recomposes when this value changes.
    /// </summary>
    public T Value => MutableState<T>.FromJava(_state.Value);

    /// <summary>
    /// Returns the underlying value's string representation so
    /// <c>$"...{state}..."</c> interpolation reads as Kotlin would
    /// (a null value renders as <c>"null"</c>).
    /// </summary>
    public override string ToString() => Value?.ToString() ?? "null";
}
