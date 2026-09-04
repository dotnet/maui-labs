using AndroidX.Compose.Foundation.Gestures;

namespace AndroidX.Compose;

/// <summary>
/// Caller-supplied state holder for <see cref="Modifier.Draggable(DraggableState, Orientation, bool)"/>.
/// Wraps the bound <c>androidx.compose.foundation.gestures.DraggableState</c>
/// interface. The constructor calls the bound
/// <c>DraggableKt.DraggableState(onDelta)</c> factory under the hood.
/// </summary>
/// <remarks>
/// <para>
/// Construct one inside a <see cref="ComposeExtensions.Remember{T}(Func{T}, int, string)"/> callback so
/// the same state survives recompositions:
/// <code>
/// var offset = RememberSaveable(() =&gt; new MutableNumberState&lt;float&gt;(0f));
/// var drag   = Remember(() =&gt; new DraggableState(delta =&gt; offset.Value += delta));
///
/// new Box
/// {
///     Modifier.Draggable(drag, Orientation.Horizontal),
///     // ... draggable content ...
/// }
/// </code>
/// </para>
/// <para>
/// For per-recomposition delegate identity, prefer
/// <see cref="ComposeExtensions.RememberDraggableState(Action{float}, int, string)"/>
/// — Kotlin's <c>rememberDraggableState</c> wraps your callback in a
/// <c>rememberUpdatedState</c> cell so the underlying Java
/// <c>DraggableState</c> stays stable while the lambda can change.
/// </para>
/// </remarks>
public sealed class DraggableState
{
    internal IDraggableState Jvm { get; }

    /// <summary>
    /// Build a new <see cref="DraggableState"/> backed by a fresh Kotlin
    /// <c>DraggableState</c>. The <paramref name="onDelta"/> callback
    /// receives the raw drag delta in pixels along the chosen
    /// <see cref="Orientation"/>; mutate whatever offset state your
    /// composables read so the dragged content moves on the next
    /// recomposition.
    /// </summary>
    public DraggableState(Action<float> onDelta)
    {
        ArgumentNullException.ThrowIfNull(onDelta);
        var jcw = new ComposableLambda1(boxed =>
        {
            var f = boxed as Java.Lang.Float
                ?? throw new InvalidCastException(
                    $"Expected java.lang.Float in DraggableState.onDelta; got '{boxed?.Class?.Name ?? "null"}'.");
            onDelta(f.FloatValue());
        });
        Jvm = DraggableKt.DraggableState(jcw)
            ?? throw new InvalidOperationException(
                "DraggableKt.DraggableState returned null.");
    }

    // Constructor used by ComposeExtensions.RememberDraggableState to wrap the
    // remember-cached IDraggableState handle that Kotlin built.
    internal DraggableState(IDraggableState jvm)
    {
        Jvm = jvm;
    }

    /// <summary>
    /// Dispatch a raw drag delta to this state without going through the
    /// gesture pipeline. Mirrors Kotlin's
    /// <c>DraggableState.dispatchRawDelta(delta)</c>; useful for
    /// programmatically nudging a draggable from outside a touch
    /// gesture (e.g. animation, keyboard handler).
    /// </summary>
    public void DispatchRawDelta(float delta) => Jvm.DispatchRawDelta(delta);
}
