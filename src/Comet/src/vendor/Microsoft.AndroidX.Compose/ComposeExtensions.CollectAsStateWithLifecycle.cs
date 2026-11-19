using AndroidX.Compose.Runtime;
using AndroidX.Lifecycle.Compose;
using Xamarin.KotlinX.Coroutines.Flow;

namespace AndroidX.Compose;

public static partial class ComposeExtensions
{
    // ---- Flow<T>.collectAsStateWithLifecycle ----
    //
    // C# parity of androidx.lifecycle.compose.collectAsStateWithLifecycle —
    // the standard bridge from a Kotlin StateFlow<T>/Flow<T> (typically
    // exposed by a ViewModel) into a Compose State<T> that pauses
    // collection while the hosting LifecycleOwner is below the requested
    // minimum active state (default: STARTED — so collection pauses while
    // the activity is stopped/destroyed).
    //
    // We call the bound FlowExtKt overloads directly (no [ComposeBridge]
    // needed). The trailing two ints on each binding are Kotlin's
    // ($changed, $default) convention; we pass 0 for $changed (no info)
    // and set the bits for the parameters we omit so Kotlin substitutes
    // its source-level defaults — Lifecycle.State.STARTED for
    // minActiveState and EmptyCoroutineContext for context. Bits index
    // Kotlin source parameters only; the extension receiver is invisible
    // to the bitmask.
    //
    // The bound IFlow / IStateFlow interfaces are non-generic on the .NET
    // side (Kotlin's <T> type parameter is erased through JNI), so the
    // managed <T> is a caller-asserted contract. CollectedState<T> uses
    // MutableState<T>.FromJava to unbox the latest value — it will throw
    // on a mismatch.

    /// <summary>
    /// C# parity of Kotlin's
    /// <c>StateFlow&lt;T&gt;.collectAsStateWithLifecycle(...)</c>:
    /// returns a read-only Compose <see cref="IState{T}"/> that mirrors
    /// the latest value emitted by <paramref name="stateFlow"/> while the
    /// composition's <see cref="ILifecycleOwner"/> (resolved via
    /// <see cref="LocalLifecycleOwner.Current"/>) is at least at
    /// <c>Lifecycle.State.STARTED</c>. Reading the returned state's
    /// <see cref="IState{T}.Value"/> inside a composition subscribes the
    /// surrounding scope so it recomposes when a new value is published.
    /// </summary>
    /// <typeparam name="T">
    /// Caller-asserted element type of the underlying Kotlin
    /// <c>StateFlow&lt;T&gt;</c>. Mismatch (e.g. asking for
    /// <c>&lt;int&gt;</c> on a flow that actually emits strings) is
    /// caught lazily inside the <see cref="IState{T}.Value"/> getter.
    /// </typeparam>
    /// <param name="stateFlow">
    /// The source Kotlin <c>kotlinx.coroutines.flow.StateFlow</c>.
    /// </param>
    /// <param name="composer">
    /// The active composer — supplied automatically when call sites use
    /// the standard <c>Render(IComposer composer)</c> entry point.
    /// </param>
    /// <remarks>
    /// Defaults to <c>Lifecycle.State.STARTED</c> +
    /// <c>EmptyCoroutineContext</c>. To opt into a different active state
    /// or coroutine context, call
    /// <see cref="FlowExtKt.CollectAsStateWithLifecycle(IStateFlow, AndroidX.Lifecycle.ILifecycleOwner?, AndroidX.Lifecycle.Lifecycle.State?, Kotlin.Coroutines.ICoroutineContext?, IComposer?, int, int)"/>
    /// directly.
    /// </remarks>
    public static CollectedState<T> CollectAsStateWithLifecycle<T>(
        this IStateFlow stateFlow,
        IComposer composer)
    {
        ArgumentNullException.ThrowIfNull(stateFlow);
        ArgumentNullException.ThrowIfNull(composer);

        // $default bits index Kotlin source params only (the receiver is
        // invisible to the bitmask). Source params are
        // (lifecycleOwner, minActiveState, context) = bits 0, 1, 2.
        // We supply lifecycleOwner (bit 0=0) and let Kotlin substitute
        // the defaults for minActiveState and context (bits 1+2=1).
        const int defaults = 0b110; // = 6

        var state = FlowExtKt.CollectAsStateWithLifecycle(
            stateFlow,
            LocalLifecycleOwner.Current(composer),
            minActiveState: null,
            context:        null,
            composer,
            p5:       0,        // $changed: no info
            _changed: defaults);
        return new CollectedState<T>(state);
    }

    /// <summary>
    /// C# parity of Kotlin's
    /// <c>Flow&lt;T&gt;.collectAsStateWithLifecycle(initialValue, ...)</c>:
    /// returns a read-only Compose <see cref="IState{T}"/> seeded with
    /// <paramref name="initialValue"/> that mirrors every value emitted
    /// by <paramref name="flow"/> while the composition's
    /// <see cref="ILifecycleOwner"/> (resolved via
    /// <see cref="LocalLifecycleOwner.Current"/>) is at least at
    /// <c>Lifecycle.State.STARTED</c>.
    /// </summary>
    /// <typeparam name="T">
    /// Caller-asserted element type of the underlying Kotlin
    /// <c>Flow&lt;T&gt;</c>. Mismatch (e.g. asking for <c>&lt;int&gt;</c>
    /// on a flow that actually emits strings) is caught lazily inside
    /// the <see cref="IState{T}.Value"/> getter.
    /// </typeparam>
    /// <param name="flow">
    /// The source Kotlin <c>kotlinx.coroutines.flow.Flow</c>.
    /// </param>
    /// <param name="initialValue">
    /// Initial value observed before the flow emits its first item.
    /// </param>
    /// <param name="composer">
    /// The active composer — supplied automatically when call sites use
    /// the standard <c>Render(IComposer composer)</c> entry point.
    /// </param>
    /// <remarks>
    /// Defaults to <c>Lifecycle.State.STARTED</c> +
    /// <c>EmptyCoroutineContext</c>. To opt into a different active state
    /// or coroutine context, call
    /// <see cref="FlowExtKt.CollectAsStateWithLifecycle(IFlow, Java.Lang.Object?, AndroidX.Lifecycle.ILifecycleOwner?, AndroidX.Lifecycle.Lifecycle.State?, Kotlin.Coroutines.ICoroutineContext?, IComposer?, int, int)"/>
    /// directly.
    /// </remarks>
    public static CollectedState<T> CollectAsStateWithLifecycle<T>(
        this IFlow flow,
        T initialValue,
        IComposer composer)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(composer);

        // $default bits index Kotlin source params only. Source params are
        // (initialValue, lifecycleOwner, minActiveState, context) =
        // bits 0, 1, 2, 3. We supply initialValue + lifecycleOwner
        // (bits 0+1=0) and let Kotlin substitute the defaults for
        // minActiveState and context (bits 2+3=1).
        const int defaults = 0b1100; // = 12

        var state = FlowExtKt.CollectAsStateWithLifecycle(
            flow,
            MutableState<T>.ToJava(initialValue),
            LocalLifecycleOwner.Current(composer),
            minActiveState: null,
            context:        null,
            composer,
            p6:       0,        // $changed: no info
            _changed: defaults);
        return new CollectedState<T>(state);
    }
}
