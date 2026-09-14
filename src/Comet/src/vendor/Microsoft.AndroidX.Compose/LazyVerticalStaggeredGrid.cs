using AndroidX.Compose.Foundation.Lazy.Staggeredgrid;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Foundation <c>LazyVerticalStaggeredGrid</c> — a 2D grid that scrolls
/// vertically, where each cell is sized to its own content (so columns
/// drift independently and Pinterest-style staggered layouts fall out
/// for free). Cell strategies live on
/// <see cref="StaggeredGridCells"/> (<c>Fixed(count)</c>,
/// <c>Adaptive(minDp)</c>, <c>FixedSize(dp)</c>).
///
/// <code>
/// new LazyVerticalStaggeredGrid&lt;Photo&gt;(
///     columns:     StaggeredGridCells.Adaptive(120f),
///     items:       photos,
///     itemContent: p =&gt; new Image(p.Url))
/// </code>
/// </summary>
public sealed class LazyVerticalStaggeredGrid<T> : ComposableNode
{
    readonly IStaggeredGridCells _columns;
    readonly IReadOnlyList<T> _items;
    readonly Func<T, ComposableNode> _itemContent;

    /// <summary>
    /// Construct a vertical staggered grid that scrolls a column count
    /// strategy (<paramref name="columns"/>) over <paramref name="items"/>,
    /// rendering each via <paramref name="itemContent"/>.
    /// </summary>
    public LazyVerticalStaggeredGrid(IStaggeredGridCells columns, IReadOnlyList<T> items, Func<T, ComposableNode> itemContent)
    {
        ArgumentNullException.ThrowIfNull(columns);
        _columns     = columns;
        ArgumentNullException.ThrowIfNull(items);
        _items       = items;
        ArgumentNullException.ThrowIfNull(itemContent);
        _itemContent = itemContent;
    }

    /// <summary>
    /// Optional scroll state. Leave null to let Compose substitute its
    /// own remembered state.
    /// </summary>
    public LazyStaggeredGridState? State { get; set; }

    /// <summary>
    /// Optional fixed content padding applied inside the grid (not as a
    /// modifier on the grid frame).
    /// </summary>
    public PaddingValues? ContentPadding { get; set; }

    public override void Render(IComposer composer)
    {
        var modifier = BuildModifier();
        var content  = new ComposableLambda1(scopeObj =>
        {
            var scope = Android.Runtime.Extensions.JavaCast<ILazyStaggeredGridScope>(scopeObj!);
            // span: null = 1 cell per item (the Compose default); itemContent
            // runs at measure time inside the staggered grid's
            // SubcomposeLayout, so route through Instantiate4 (the
            // composer-less ComposableLambdaInstance) — Wrap4 would crash
            // because there's no active composer once the DSL builder runs.
            scope.Items(
                _items.Count,
                key:         null,
                contentType: new ComposableLambda1(_ => { }),
                span:        null,
                p4:          ComposableLambdas.Instantiate4((_, indexBoxed, comp) =>
                {
                    var i = ((Java.Lang.Integer)indexBoxed!).IntValue();
                    _itemContent(_items[i]).Render(comp);
                }));
        });

        // Bit positions for LazyVerticalStaggeredGrid (Kotlin source order):
        //   0 = columns               (always provided)
        //   1 = modifier
        //   2 = state
        //   3 = contentPadding
        //   4 = reverseLayout
        //   5 = verticalItemSpacing
        //   6 = horizontalArrangement
        //   7 = flingBehavior
        //   8 = userScrollEnabled
        //   9 = overscrollEffect
        //  10 = content               (always provided)
        int defaults = (int)LazyVerticalStaggeredGridDefault.All & ~(int)LazyVerticalStaggeredGridDefault.Columns;
        if (modifier       is not null) defaults &= ~(int)LazyVerticalStaggeredGridDefault.Modifier;
        if (State          is not null) defaults &= ~(int)LazyVerticalStaggeredGridDefault.State;
        if (ContentPadding is not null) defaults &= ~(int)LazyVerticalStaggeredGridDefault.ContentPadding;

        // 11 user params → Compose splits $changed into two ints
        // (p12, _changed) and $default lives in the trailing _changed1.
        LazyStaggeredGridDslKt.LazyVerticalStaggeredGrid(
            columns:               _columns,
            modifier:              modifier,
            state:                 State,
            contentPadding:        ContentPadding?.Jvm,
            reverseLayout:         false,
            verticalItemSpacing:   0f,
            horizontalArrangement: null,
            flingBehavior:         null,
            userScrollEnabled:     true,
            overscrollEffect:      null,
            content:               content,
            _composer:             composer,
            p12:                   0,
            _changed:              0,
            _changed1:             defaults);
    }
}
