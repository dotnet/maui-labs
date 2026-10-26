using AndroidX.Compose.Material3.Carousel;
using AndroidX.Compose.Runtime;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>HorizontalMultiBrowseCarousel</c> — a horizontal
/// carousel that lays out a large "preferred" item plus one or more
/// adjacent smaller items in the keylines, so the user can preview
/// the next/previous entries while browsing. The carousel snaps so a
/// preferred-width item is always centered.
///
/// <code>
/// new HorizontalMultiBrowseCarousel&lt;Photo&gt;(
///     items: photos,
///     preferredItemWidth: 240f,
///     itemContent: p =&gt; new Card { new Text(p.Title) })
/// </code>
///
/// Material 3 1.4 only ships horizontal carousels — there is no
/// vertical multi-browse variant in the bound bindings.
/// </summary>
public sealed class HorizontalMultiBrowseCarousel<T> : ComposableNode
{
    readonly IReadOnlyList<T> _items;
    readonly float _preferredItemWidth;
    readonly Func<T, ComposableNode> _itemContent;
    IFunction0? _itemCountFn;

    public HorizontalMultiBrowseCarousel(IReadOnlyList<T> items, float preferredItemWidth, Func<T, ComposableNode> itemContent)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items              = items;
        ArgumentNullException.ThrowIfNull(itemContent);
        _itemContent        = itemContent;
        _preferredItemWidth = preferredItemWidth;
    }

    /// <summary>
    /// Optional carousel state. Leave null to let the facade allocate
    /// one via Compose's <c>rememberCarouselState</c>.
    /// </summary>
    public CarouselState? State { get; set; }

    /// <summary>Item spacing in dp. <see cref="float.NaN"/> uses Compose's default (0.dp).</summary>
    public float ItemSpacing { get; set; } = float.NaN;

    /// <summary>User scroll gesture toggle. <c>null</c> uses Compose's default (<c>true</c>).</summary>
    public bool? UserScrollEnabled { get; set; }

    public override void Render(IComposer composer)
    {
        _itemCountFn ??= new ComposableLambda0Int(() => _items.Count);

        var rememberedState = CarouselStateKt.RememberCarouselState(
            p0:          0,
            itemCount:   _itemCountFn,
            _composer:   composer,
            initialItem: 0,
            _changed:    1);
        var state = State ?? rememberedState;

        var content = ComposableLambdas.Wrap4(composer,
            (_, indexBoxed, comp) =>
            {
                var i = ((Java.Lang.Integer)indexBoxed!).IntValue();
                _itemContent(_items[i]).Render(comp);
            });

        var modifier = BuildModifier();

        // $default bit positions for HorizontalMultiBrowseCarousel-3tcCNu0:
        //   0 = state              (required)
        //   1 = preferredItemWidth (required)
        //   2 = modifier
        //   3 = itemSpacing
        //   4 = flingBehavior      (always defaulted)
        //   5 = userScrollEnabled
        //   6 = minSmallItemWidth  (always defaulted)
        //   7 = maxSmallItemWidth  (always defaulted)
        //   8 = contentPadding     (always defaulted)
        //   9 = content            (required)
        int defaults = 1 << 4 | 1 << 6 | 1 << 7 | 1 << 8;
        if (modifier              is null) defaults |= 1 << 2;
        if (float.IsNaN(ItemSpacing))      defaults |= 1 << 3;
        if (UserScrollEnabled     is null) defaults |= 1 << 5;

        CarouselKt.HorizontalMultiBrowseCarousel(
            state:              state,
            preferredItemWidth: _preferredItemWidth,
            modifier:           modifier,
            itemSpacing:        float.IsNaN(ItemSpacing) ? 0f : ItemSpacing,
            flingBehavior:      null,
            userScrollEnabled:  UserScrollEnabled ?? true,
            minSmallItemWidth:  0f,
            maxSmallItemWidth:  0f,
            contentPadding:     null,
            content:            content,
            _composer:          composer,
            p11:                0,
            _changed:           defaults);
    }
}
