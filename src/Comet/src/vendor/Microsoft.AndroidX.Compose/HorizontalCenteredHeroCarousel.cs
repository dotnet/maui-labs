using AndroidX.Compose.Material3.Carousel;
using AndroidX.Compose.Runtime;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>HorizontalCenteredHeroCarousel</c> — a horizontal
/// carousel that centers a single "hero" item and trims the
/// surrounding items to small previews on either side. Use for
/// editorial or featured content where one item should dominate at
/// any given scroll position.
///
/// <code>
/// new HorizontalCenteredHeroCarousel&lt;Story&gt;(
///     items: stories,
///     itemContent: s =&gt; new Card { new Text(s.Headline) })
/// </code>
///
/// Material 3 1.4 only ships horizontal carousels — there is no
/// vertical hero variant in the bound bindings.
/// </summary>
public sealed class HorizontalCenteredHeroCarousel<T> : ComposableNode
{
    readonly IReadOnlyList<T> _items;
    readonly Func<T, ComposableNode> _itemContent;
    IFunction0? _itemCountFn;

    public HorizontalCenteredHeroCarousel(IReadOnlyList<T> items, Func<T, ComposableNode> itemContent)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items       = items;
        ArgumentNullException.ThrowIfNull(itemContent);
        _itemContent = itemContent;
    }

    /// <summary>
    /// Optional carousel state. Leave null to let the facade allocate
    /// one via Compose's <c>rememberCarouselState</c>.
    /// </summary>
    public CarouselState? State { get; set; }

    /// <summary>
    /// Max width of the centered hero item in dp.
    /// <see cref="float.NaN"/> uses Compose's default
    /// (<c>Dp.Unspecified</c>) which lets the hero expand to fill the
    /// viewport minus the small-item keylines.
    /// </summary>
    public float MaxItemWidth { get; set; } = float.NaN;

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

        // $default bit positions for HorizontalCenteredHeroCarousel-p2lB3Bg:
        //   0 = state             (required)
        //   1 = modifier
        //   2 = maxItemWidth
        //   3 = itemSpacing
        //   4 = flingBehavior     (always defaulted)
        //   5 = userScrollEnabled
        //   6 = minSmallItemWidth (always defaulted)
        //   7 = maxSmallItemWidth (always defaulted)
        //   8 = contentPadding    (always defaulted)
        //   9 = content           (required)
        int defaults = 1 << 4 | 1 << 6 | 1 << 7 | 1 << 8;
        if (modifier              is null) defaults |= 1 << 1;
        if (float.IsNaN(MaxItemWidth))     defaults |= 1 << 2;
        if (float.IsNaN(ItemSpacing))      defaults |= 1 << 3;
        if (UserScrollEnabled     is null) defaults |= 1 << 5;

        CarouselKt.HorizontalCenteredHeroCarousel(
            state:             state,
            modifier:          modifier,
            maxItemWidth:      float.IsNaN(MaxItemWidth) ? 0f : MaxItemWidth,
            itemSpacing:       float.IsNaN(ItemSpacing)  ? 0f : ItemSpacing,
            flingBehavior:     null,
            userScrollEnabled: UserScrollEnabled ?? true,
            minSmallItemWidth: 0f,
            maxSmallItemWidth: 0f,
            contentPadding:    null,
            content:           content,
            _composer:         composer,
            p11:               0,
            _changed:          defaults);
    }
}
