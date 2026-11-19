
namespace AndroidX.Compose;

/// <summary>
/// Caller-supplied state holder for <see cref="LazyColumn{T}"/> and
/// <see cref="LazyRow{T}"/>. Wraps the bound
/// <c>androidx.compose.foundation.lazy.LazyListState</c> — Kotlin's
/// class is plain enough for the binder to expose its parameterless
/// and two-int constructors directly, so no JNI bridge is needed to
/// construct one. The wrapper exists so the facade can grow async
/// scroll APIs (<see cref="ScrollToItemAsync"/> /
/// <see cref="AnimateScrollToItemAsync"/>) without polluting the
/// binding surface, and so the Remember helper
/// (<see cref="ComposeExtensions.RememberLazyListState(AndroidX.Compose.Runtime.IComposer, int, int, int, string)"/>)
/// can hand back a typed managed object.
/// </summary>
/// <remarks>
/// <para>
/// Obtain one via
/// <see cref="ComposeExtensions.RememberLazyListState(AndroidX.Compose.Runtime.IComposer, int, int, int, string)"/>
/// so the scroll position survives recompositions:
/// <code>
/// var listState = composer.RememberLazyListState();
///
/// new LazyColumn&lt;int&gt;(items, i =&gt; new Text($"{i}"))
/// {
///     State = listState,
/// };
/// </code>
/// </para>
/// <para>
/// Unlike Kotlin's <c>rememberLazyListState()</c> (which uses
/// <c>rememberSaveable</c>), this state survives recompositions but
/// not process death / configuration changes. For most apps that's
/// fine; if you need saved scroll state, track it yourself.
/// </para>
/// <para>
/// Programmatic scrolling is exposed via the <c>Async</c> methods —
/// <see cref="ScrollToItemAsync"/> snaps instantly,
/// <see cref="AnimateScrollToItemAsync"/> runs the default spring
/// animation. Both bridge the underlying Kotlin <c>suspend</c>
/// function through <see cref="Task"/> so they integrate with C#
/// <c>async</c>/<c>await</c>.
/// </para>
/// </remarks>
public sealed class LazyListState
{
    internal AndroidX.Compose.Foundation.Lazy.LazyListState Jvm { get; }

    /// <summary>
    /// Create a new <see cref="LazyListState"/> seeded with the given
    /// initial first-visible-item index and scroll offset. Both
    /// default to <c>0</c> (scrolled to the very start).
    /// </summary>
    /// <param name="initialFirstVisibleItemIndex">
    /// The item index that should be the first visible item when the
    /// list first composes. Mirrors Kotlin's
    /// <c>initialFirstVisibleItemIndex</c>.
    /// </param>
    /// <param name="initialFirstVisibleItemScrollOffset">
    /// Initial scroll offset of the first visible item, in pixels.
    /// Mirrors Kotlin's <c>initialFirstVisibleItemScrollOffset</c>.
    /// </param>
    public LazyListState(
        int initialFirstVisibleItemIndex = 0,
        int initialFirstVisibleItemScrollOffset = 0)
    {
        Jvm = new AndroidX.Compose.Foundation.Lazy.LazyListState(
            initialFirstVisibleItemIndex,
            initialFirstVisibleItemScrollOffset);
    }

    // Internal wrap-around-existing constructor used by
    // ComposeExtensions.RememberLazyListState to surface the Kotlin
    // remember-cached binding instance.
    internal LazyListState(AndroidX.Compose.Foundation.Lazy.LazyListState jvm)
    {
        Jvm = jvm;
    }

    /// <summary>
    /// Index of the first item that's currently visible. Mirrors
    /// Kotlin's <c>LazyListState.firstVisibleItemIndex</c>.
    /// </summary>
    public int FirstVisibleItemIndex => Jvm.FirstVisibleItemIndex;

    /// <summary>
    /// Scroll offset of the first visible item, in pixels. Mirrors
    /// Kotlin's <c>LazyListState.firstVisibleItemScrollOffset</c>.
    /// </summary>
    public int FirstVisibleItemScrollOffset => Jvm.FirstVisibleItemScrollOffset;

    /// <summary>
    /// <c>true</c> while a fling or programmatic scroll is in flight.
    /// Mirrors Kotlin's <c>LazyListState.isScrollInProgress</c>.
    /// </summary>
    public bool IsScrollInProgress => Jvm.IsScrollInProgress;

    /// <summary>
    /// <c>true</c> when the content can scroll further backward
    /// (toward index 0 in normal layout, away from index 0 in
    /// <c>reverseLayout</c>). Mirrors Kotlin's
    /// <c>LazyListState.canScrollBackward</c>.
    /// </summary>
    public bool CanScrollBackward => Jvm.CanScrollBackward;

    /// <summary>
    /// <c>true</c> when the content can scroll further forward (away
    /// from index 0 in normal layout, toward index 0 in
    /// <c>reverseLayout</c>). Mirrors Kotlin's
    /// <c>LazyListState.canScrollForward</c>.
    /// </summary>
    public bool CanScrollForward => Jvm.CanScrollForward;

    /// <summary>
    /// <c>true</c> when the most recent scroll motion was backward
    /// (toward lower offsets). Mirrors Kotlin's
    /// <c>LazyListState.lastScrolledBackward</c>.
    /// </summary>
    public bool LastScrolledBackward => Jvm.LastScrolledBackward;

    /// <summary>
    /// <c>true</c> when the most recent scroll motion was forward
    /// (toward higher offsets). Mirrors Kotlin's
    /// <c>LazyListState.lastScrolledForward</c>.
    /// </summary>
    public bool LastScrolledForward => Jvm.LastScrolledForward;

    /// <summary>
    /// Snap instantly to the given item <paramref name="index"/>
    /// (no animation), positioning it at the start of the viewport.
    /// Mirrors Kotlin's
    /// <c>LazyListState.scrollToItem(index, scrollOffset)</c>.
    /// </summary>
    /// <param name="index">Target item index.</param>
    /// <param name="scrollOffset">
    /// Additional pixel offset added to the item's position once it's
    /// the first visible item. Defaults to <c>0</c>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the returned task with
    /// <see cref="OperationCanceledException"/>. See
    /// <see cref="SuspendBridge"/> remarks for the current (C#-only)
    /// cancellation semantics.
    /// </param>
    public Task ScrollToItemAsync(
        int index,
        int scrollOffset = 0,
        CancellationToken cancellationToken = default) =>
        SuspendBridge.Invoke(cont =>
            ComposeBridges.LazyListStateScrollToItem(
                ((Java.Lang.Object)Jvm).Handle, index, scrollOffset, cont),
            cancellationToken);

    /// <summary>
    /// Animate to the given item <paramref name="index"/> using the
    /// default Compose spring animation, positioning it at the start
    /// of the viewport. Mirrors Kotlin's
    /// <c>LazyListState.animateScrollToItem(index, scrollOffset)</c>;
    /// the returned task completes when the animation lands.
    /// </summary>
    /// <param name="index">Target item index.</param>
    /// <param name="scrollOffset">
    /// Additional pixel offset added to the item's position once it's
    /// the first visible item. Defaults to <c>0</c>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the returned task with
    /// <see cref="OperationCanceledException"/>. The underlying
    /// Kotlin animation keeps running to its natural completion — see
    /// <see cref="SuspendBridge"/> remarks for the current (C#-only)
    /// cancellation semantics.
    /// </param>
    public Task AnimateScrollToItemAsync(
        int index,
        int scrollOffset = 0,
        CancellationToken cancellationToken = default) =>
        SuspendBridge.Invoke(cont =>
            ComposeBridges.LazyListStateAnimateScrollToItem(
                ((Java.Lang.Object)Jvm).Handle, index, scrollOffset, cont),
            cancellationToken);
}
