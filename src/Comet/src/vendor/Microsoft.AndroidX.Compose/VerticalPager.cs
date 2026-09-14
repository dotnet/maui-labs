using AndroidX.Compose.Foundation.Pager;
using AndroidX.Compose.Runtime;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// Foundation <c>VerticalPager</c> — vertical mirror of
/// <see cref="HorizontalPager{T}"/>. Swipes through pages along the
/// vertical axis and snaps to page boundaries.
///
/// <code>
/// new VerticalPager&lt;Photo&gt;(
///     items:       photos,
///     itemContent: p =&gt; new Image(p.Url))
/// {
///     Modifier = Modifier.FillMaxSize(),
/// }
/// </code>
/// </summary>
public sealed class VerticalPager<T> : ComposableNode
{
    readonly IReadOnlyList<T> _items;
    readonly Func<T, ComposableNode> _itemContent;
    IFunction0? _pageCountFn;

    /// <summary>
    /// Construct a vertical pager that snaps through
    /// <paramref name="items"/>, rendering each via
    /// <paramref name="itemContent"/>.
    /// </summary>
    public VerticalPager(IReadOnlyList<T> items, Func<T, ComposableNode> itemContent)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items       = items;
        ArgumentNullException.ThrowIfNull(itemContent);
        _itemContent = itemContent;
    }

    /// <summary>
    /// Optional caller-supplied state holder. Leave null to let the
    /// facade allocate one via <c>rememberPagerState</c> internally.
    /// </summary>
    public PagerState? State { get; set; }

    /// <summary>
    /// Optional fixed content padding applied inside the pager (lets
    /// adjacent pages peek without shrinking the page bounds via
    /// <see cref="Modifier"/>).
    /// </summary>
    public PaddingValues? ContentPadding { get; set; }

    public override void Render(IComposer composer)
    {
        // See HorizontalPager.Render — same eager-vs-remember path.
        AndroidX.Compose.Foundation.Pager.PagerState jvmState;
        if (State is not null)
        {
            jvmState = State.Jvm;
        }
        else
        {
            _pageCountFn ??= new ComposableLambda0Int(() => _items.Count);
            jvmState = PagerStateKt.RememberPagerState(
                p0:                        0,
                initialPageOffsetFraction: 0f,
                pageCount:                 _pageCountFn,
                _composer:                 composer,
                initialPage:               0,
                _changed:                  0);
        }

        var modifier = BuildModifier();
        var content  = ComposableLambdas.Wrap4(composer, (_, indexBoxed, comp) =>
        {
            var i = ((Java.Lang.Integer)indexBoxed!).IntValue();
            _itemContent(_items[i]).Render(comp);
        });

        int defaults = (int)VerticalPagerDefault.All;
        if (modifier       is not null) defaults &= ~(int)VerticalPagerDefault.Modifier;
        if (ContentPadding is not null) defaults &= ~(int)VerticalPagerDefault.ContentPadding;

        PagerKt.VerticalPager(
            state:                       jvmState,
            modifier:                    modifier,
            contentPadding:              ContentPadding?.Jvm,
            pageSize:                    null,
            p4:                          0,    // beyondViewportPageCount
            pageSpacing:                 0f,
            horizontalAlignment:         null,
            flingBehavior:               null,
            userScrollEnabled:           true,
            reverseLayout:               false,
            key:                         null,
            pageNestedScrollConnection:  null,
            snapPosition:                null,
            overscrollEffect:            null,
            pageContent:                 content,
            _composer:                   composer,
            beyondViewportPageCount:     0,    // $changed slot 1
            _changed:                    0,    // $changed slot 2
            _changed1:                   defaults);
    }
}
