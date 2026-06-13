using AndroidX.Compose.Material3.Carousel;
using AndroidX.Compose.Runtime;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>HorizontalUncontainedCarousel</c> — a single-row
/// carousel where every item has the same fixed width and the carousel
/// scrolls horizontally without snapping a "small" item into the
/// keylines. Use when the items are uniform and you want the simplest
/// keyline strategy.
///
/// The facade is data-driven (same shape as <see cref="LazyRow{T}"/>):
/// you pass the items list, the item width in dp, and a per-item
/// content callback at construction time. The underlying
/// <see cref="CarouselState"/> is auto-allocated via Compose's
/// <c>rememberCarouselState</c> unless you supply one in
/// <see cref="State"/>.
///
/// <code>
/// new HorizontalUncontainedCarousel&lt;Photo&gt;(
///     items: photos,
///     itemWidth: 200f,
///     itemContent: p =&gt; new Card { new Text(p.Title) })
/// </code>
///
/// Material 3 1.4 only ships horizontal carousels — there is no
/// <c>VerticalUncontainedCarousel</c> in the bound bindings.
/// </summary>
public sealed class HorizontalUncontainedCarousel<T> : ComposableNode
{
    readonly IReadOnlyList<T> _items;
    readonly float _itemWidth;
    readonly Func<T, ComposableNode> _itemContent;
    IFunction0? _itemCountFn;

    public HorizontalUncontainedCarousel(IReadOnlyList<T> items, float itemWidth, Func<T, ComposableNode> itemContent)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items       = items;
        ArgumentNullException.ThrowIfNull(itemContent);
        _itemContent = itemContent;
        _itemWidth   = itemWidth;
    }

    /// <summary>
    /// Optional carousel state. Leave null to let the facade allocate
    /// one via Compose's <c>rememberCarouselState</c>. Set this when
    /// you want to drive scroll position programmatically or observe
    /// <see cref="CarouselState.CurrentItem"/>.
    /// </summary>
    public CarouselState? State { get; set; }

    /// <summary>Item spacing in dp. <see cref="float.NaN"/> uses Compose's default (0.dp).</summary>
    public float ItemSpacing { get; set; } = float.NaN;

    /// <summary>User scroll gesture toggle. <c>null</c> uses Compose's default (<c>true</c>).</summary>
    public bool? UserScrollEnabled { get; set; }

    public override void Render(IComposer composer)
    {
        // Stable Function0 reference: the underlying CarouselState
        // captures this lambda once and queries it on each measure
        // pass, so re-allocating per render is wasteful — and
        // unhelpful, since the closure already reads _items.Count
        // lazily.
        _itemCountFn ??= new ComposableLambda0Int(() => _items.Count);

        // Always call RememberCarouselState — both for slot-table
        // stability (so the carousel call below sees a consistent
        // sequence of remember slots across recompositions) and so
        // the M3 carousel internally picks up the latest itemCount
        // lambda. Binding parameter names are confusingly mapped:
        //   p0           = initialItem (the actual int)
        //   itemCount    = the Function0
        //   _composer    = composer
        //   initialItem  = $changed (pass 0)
        //   _changed     = $default (bit 0 = use Kotlin default for initialItem)
        var rememberedState = CarouselStateKt.RememberCarouselState(
            p0:          0,
            itemCount:   _itemCountFn,
            _composer:   composer,
            initialItem: 0,
            _changed:    1);
        var state = State ?? rememberedState;

        // Wrap4 (not Instantiate4): the content lambda is a direct
        // @Composable parameter on the carousel call — its identity
        // belongs to the OUTER composition's slot table even though
        // the body runs inside the carousel's pager subcomposition.
        var content = ComposableLambdas.Wrap4(composer,
            (_, indexBoxed, comp) =>
            {
                var i = ((Java.Lang.Integer)indexBoxed!).IntValue();
                _itemContent(_items[i]).Render(comp);
            });

        var modifier = BuildModifier();

        // $default bit positions for HorizontalUncontainedCarousel-VUP9l70:
        //   0 = state           (required, never defaulted)
        //   1 = itemWidth       (required, never defaulted)
        //   2 = modifier
        //   3 = itemSpacing
        //   4 = flingBehavior   (always defaulted)
        //   5 = userScrollEnabled
        //   6 = contentPadding  (always defaulted)
        //   7 = content         (required, never defaulted)
        int defaults = 1 << 4 | 1 << 6;
        if (modifier              is null) defaults |= 1 << 2;
        if (float.IsNaN(ItemSpacing))      defaults |= 1 << 3;
        if (UserScrollEnabled     is null) defaults |= 1 << 5;

        CarouselKt.HorizontalUncontainedCarousel(
            state:             state,
            itemWidth:         _itemWidth,
            modifier:          modifier,
            itemSpacing:       float.IsNaN(ItemSpacing) ? 0f : ItemSpacing,
            flingBehavior:     null,
            userScrollEnabled: UserScrollEnabled ?? true,
            contentPadding:    null,
            content:           content,
            _composer:         composer,
            p9:                0,
            _changed:          defaults);
    }
}
