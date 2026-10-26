namespace AndroidX.Compose;

/// <summary>
/// Read-only view onto an observable value. The C# parity of
/// Kotlin's <c>androidx.compose.runtime.State&lt;T&gt;</c>: a single
/// <see cref="Value"/> getter whose read participates in the
/// composition's snapshot dependency tracking, so any composable that
/// reads it re-runs when the value changes.
/// </summary>
/// <typeparam name="T">The observed value type.</typeparam>
/// <remarks>
/// Implementations in AndroidX.Compose: <see cref="MutableState{T}"/>,
/// <see cref="MutableNumberState{T}"/>, <see cref="DerivedState{T}"/>,
/// and <see cref="MutableStateFlow{T}"/>. Pass <c>IState&lt;T&gt;</c>
/// when an API needs a read-only handle that a caller can subscribe
/// to from a composition body without exposing a setter.
/// </remarks>
public interface IState<out T>
{
    /// <summary>The current value. Reads inside a composition subscribe the surrounding scope to changes.</summary>
    T Value { get; }
}
