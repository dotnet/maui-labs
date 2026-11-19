using AndroidX.Compose.Foundation.Lazy.Staggeredgrid;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Foundation <c>LazyHorizontalStaggeredGrid</c> — horizontal mirror of
/// <see cref="LazyVerticalStaggeredGrid{T}"/>. Items flow top-to-bottom
/// in each row, and rows scroll horizontally with row-level
/// independent positions producing the staggered look. Cell strategies
/// live on <see cref="StaggeredGridCells"/>.
///
/// <code>
/// new LazyHorizontalStaggeredGrid&lt;Photo&gt;(
///     rows:        StaggeredGridCells.Fixed(2),
///     items:       photos,
///     itemContent: p =&gt; new Image(p.Url))
/// </code>
/// </summary>
public sealed class LazyHorizontalStaggeredGrid<T> : ComposableNode
{
    readonly IStaggeredGridCells _rows;
    readonly IReadOnlyList<T> _items;
    readonly Func<T, ComposableNode> _itemContent;

    /// <summary>
    /// Construct a horizontal staggered grid that scrolls a row count
    /// strategy (<paramref name="rows"/>) over <paramref name="items"/>,
    /// rendering each via <paramref name="itemContent"/>.
    /// </summary>
    public LazyHorizontalStaggeredGrid(IStaggeredGridCells rows, IReadOnlyList<T> items, Func<T, ComposableNode> itemContent)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows        = rows;
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

        // Bit positions for LazyHorizontalStaggeredGrid:
        //   0 = rows                  (always provided)
        //   1 = modifier
        //   2 = state
        //   3 = contentPadding
        //   4 = reverseLayout
        //   5 = verticalArrangement
        //   6 = horizontalItemSpacing
        //   7 = flingBehavior
        //   8 = userScrollEnabled
        //   9 = overscrollEffect
        //  10 = content               (always provided)
        int defaults = (int)LazyHorizontalStaggeredGridDefault.All & ~(int)LazyHorizontalStaggeredGridDefault.Rows;
        if (modifier       is not null) defaults &= ~(int)LazyHorizontalStaggeredGridDefault.Modifier;
        if (State          is not null) defaults &= ~(int)LazyHorizontalStaggeredGridDefault.State;
        if (ContentPadding is not null) defaults &= ~(int)LazyHorizontalStaggeredGridDefault.ContentPadding;

        LazyStaggeredGridDslKt.LazyHorizontalStaggeredGrid(
            rows:                  _rows,
            modifier:              modifier,
            state:                 State,
            contentPadding:        ContentPadding?.Jvm,
            reverseLayout:         false,
            verticalArrangement:   null,
            horizontalItemSpacing: 0f,
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
