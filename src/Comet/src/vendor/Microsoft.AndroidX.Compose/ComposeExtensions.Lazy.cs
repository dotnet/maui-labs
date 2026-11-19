using System.Runtime.CompilerServices;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

public static partial class ComposeExtensions
{
    /// <summary>
    /// Compose's <c>rememberDraggableState(onDelta)</c>: build a
    /// <see cref="DraggableState"/> whose underlying Kotlin handle is
    /// cached in the active composer's slot table for the lifetime of
    /// this call site. Pair with
    /// <see cref="Modifier.Draggable(DraggableState, Orientation, bool)"/>
    /// — the returned state is the value to hand to that modifier.
    /// </summary>
    public static DraggableState RememberDraggableState(
        this IComposer composer,
        Action<float> onDelta,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
    {
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(onDelta);

        composer.StartReplaceableGroup(SourceLocationKey.Compute(line, file));
        try
        {
            var jcw = new ComposableLambda1(boxed =>
            {
                var f = boxed as Java.Lang.Float
                    ?? throw new InvalidCastException(
                        $"Expected java.lang.Float in DraggableState.onDelta; got '{boxed?.Class?.Name ?? "null"}'.");
                onDelta(f.FloatValue());
            });
            var jvm = AndroidX.Compose.Foundation.Gestures.DraggableKt.RememberDraggableState(jcw, composer, 0)
                ?? throw new InvalidOperationException(
                    "DraggableKt.RememberDraggableState returned null.");
            return new DraggableState(jvm);
        }
        finally
        {
            composer.EndReplaceableGroup();
        }
    }

    /// <summary>
    /// Compose's <c>rememberLazyListState(initialFirstVisibleItemIndex,
    /// initialFirstVisibleItemScrollOffset)</c>: returns a
    /// <see cref="LazyListState"/> that survives recompositions, cached
    /// in the active composer's slot table for the lifetime of this
    /// call site.
    /// </summary>
    public static LazyListState RememberLazyListState(
        this IComposer composer,
        int initialFirstVisibleItemIndex = 0,
        int initialFirstVisibleItemScrollOffset = 0,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
    {
        ArgumentNullException.ThrowIfNull(composer);

        composer.StartReplaceableGroup(SourceLocationKey.Compute(line, file));
        try
        {
            var jvm = AndroidX.Compose.Foundation.Lazy.LazyListStateKt.RememberLazyListState(
                p0:                                  0,
                initialFirstVisibleItemIndex:        initialFirstVisibleItemIndex,
                _composer:                           composer,
                initialFirstVisibleItemScrollOffset: initialFirstVisibleItemScrollOffset,
                _changed:                            0)
                ?? throw new InvalidOperationException(
                    "LazyListStateKt.RememberLazyListState returned null.");
            return new LazyListState(jvm);
        }
        finally
        {
            composer.EndReplaceableGroup();
        }
    }
}
