using AndroidX.Compose.Foundation.Lazy;
using AndroidX.Compose.Runtime;
using BindingArrangement = AndroidX.Compose.Foundation.Layout.Arrangement;

namespace AndroidX.Compose;

/// <summary>
/// Foundation <c>LazyRow</c> — horizontal mirror of <see cref="LazyColumn{T}"/>.
/// Use when the item count along the horizontal axis exceeds the screen
/// width (chip rows, image carousels, etc.).
///
/// <code>
/// new LazyRow&lt;Photo&gt;(items: photos, itemContent: p =&gt; new Image(p.Url))
/// </code>
/// </summary>
public sealed class LazyRow<T> : ComposableNode
{
    readonly IReadOnlyList<T> _items;
    readonly Func<T, ComposableNode> _itemContent;

    public LazyRow(IReadOnlyList<T> items, Func<T, ComposableNode> itemContent)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items       = items;
        ArgumentNullException.ThrowIfNull(itemContent);
        _itemContent = itemContent;
    }

    /// <summary>
    /// Optional scroll state. Leave null to let Compose substitute its
    /// own remembered state.
    /// </summary>
    public LazyListState? State { get; set; }

    /// <summary>
    /// Optional horizontal arrangement (e.g. <see cref="Arrangement.SpacedBy(int)"/>)
    /// applied between items. Leave <see langword="null"/> to use Compose's
    /// default (<c>Arrangement.Start</c>). Must wrap a horizontal-capable
    /// Compose <see cref="Arrangement"/>; throws
    /// <see cref="ArgumentException"/> at render time for a
    /// vertical-only value (e.g. <see cref="Arrangement.Top"/>).
    /// </summary>
    public Arrangement? HorizontalArrangement { get; set; }

    /// <summary>
    /// Optional fixed content padding applied inside the row (not as a
    /// modifier on the row frame). Items can scroll behind this area but
    /// the first/last items stay fully reachable.
    /// </summary>
    public PaddingValues? ContentPadding { get; set; }

    public override void Render(IComposer composer)
    {
        var modifier = BuildModifier();
        var content  = new ComposableLambda1(scopeObj =>
        {
            var scope = Android.Runtime.Extensions.JavaCast<ILazyListScope>(scopeObj!);
            scope.Items(
                _items.Count,
                key:         null,
                contentType: new ComposableLambda1(_ => { }),
                itemContent: ComposableLambdas.Instantiate4((_, indexBoxed, comp) =>
                {
                    var i = ((Java.Lang.Integer)indexBoxed!).IntValue();
                    _itemContent(_items[i]).Render(comp);
                }));
        });

        BindingArrangement.IHorizontal? horizontal = null;
        if (HorizontalArrangement is not null)
        {
            horizontal = HorizontalArrangement.Horizontal
                ?? throw new ArgumentException(
                    $"{nameof(HorizontalArrangement)} must wrap a horizontal " +
                    $"or horizontal-or-vertical Compose Arrangement; got a " +
                    $"vertical-only value (e.g. Arrangement.Top / Arrangement.Bottom).",
                    nameof(HorizontalArrangement));
        }

        int defaults = (int)LazyRowDefault.All;
        if (modifier   is not null) defaults &= ~(int)LazyRowDefault.Modifier;
        if (State      is not null) defaults &= ~(int)LazyRowDefault.State;
        if (ContentPadding is not null) defaults &= ~(int)LazyRowDefault.ContentPadding;
        if (horizontal is not null) defaults &= ~(int)LazyRowDefault.HorizontalArrangement;

        LazyDslKt.LazyRow(
            modifier:              modifier,
            state:                 State?.Jvm,
            contentPadding:        ContentPadding?.Jvm,
            reverseLayout:         false,
            horizontalArrangement: horizontal,
            verticalAlignment:     null,
            flingBehavior:         null,
            userScrollEnabled:     true,
            overscrollEffect:      null,
            content:               content,
            _composer:             composer,
            p11:                   0,
            _changed:              defaults);
    }
}
