using AndroidX.Compose.Foundation.Pager;
using AndroidX.Compose.Runtime;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// Foundation <c>HorizontalPager</c> — swipes through a fixed list of
/// items one page at a time along the horizontal axis. Like
/// <see cref="LazyRow{T}"/>, only the visible page (plus a small
/// off-screen buffer) is composed; unlike LazyRow, the pager snaps to
/// page boundaries and exposes a <see cref="PagerState"/> for reading
/// the current page or driving programmatic scroll.
///
/// <code>
/// new HorizontalPager&lt;Story&gt;(
///     items:       stories,
///     itemContent: s =&gt; new Card { new Text(s.Headline) })
/// </code>
///
/// Set <see cref="State"/> to a remembered <see cref="PagerState"/> if
/// other UI needs to react to page changes (e.g. a tab indicator under
/// the pager). When left null the pager allocates and remembers its
/// own state internally.
/// </summary>
public sealed class HorizontalPager<T> : ComposableNode
{
    readonly IReadOnlyList<T> _items;
    readonly Func<T, ComposableNode> _itemContent;
    IFunction0? _pageCountFn;

    /// <summary>
    /// Construct a pager that snaps through <paramref name="items"/>,
    /// rendering each item via <paramref name="itemContent"/>.
    /// </summary>
    public HorizontalPager(IReadOnlyList<T> items, Func<T, ComposableNode> itemContent)
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
    /// Optional fixed content padding applied inside the pager.
    /// Useful for letting pages peek (i.e. visible side gutters that
    /// reveal slivers of the previous and next pages) without
    /// shrinking the page bounds via <see cref="Modifier"/>.
    /// </summary>
    public PaddingValues? ContentPadding { get; set; }

    public override void Render(IComposer composer)
    {
        // When the caller supplies a PagerState wrapper its Jvm is
        // built eagerly in the wrapper's ctor (via the non-@Composable
        // PagerStateKt.PagerState factory), so we just pass it through
        // and reads from C# build code subscribe to snapshot changes
        // regardless of render order — see issue #119.
        //
        // When State == null we fall back to rememberPagerState so the
        // pager owns an internal scroll-position-preserving state
        // across recompositions.
        AndroidX.Compose.Foundation.Pager.PagerState jvmState;
        if (State is not null)
        {
            jvmState = State.Jvm;
        }
        else
        {
            // Compose's rememberPagerState reads the pageCount lambda
            // on every measure pass, so the lambda has to close over
            // the live _items reference (count can change between
            // recompositions when callers swap in a new list).
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

        // 14 user-controllable params for HorizontalPager (state and
        // pageContent are always provided). All bits start set so Kotlin
        // substitutes its real defaults; we clear bits only for the
        // params this v1 facade exposes.
        int defaults = (int)HorizontalPagerDefault.All;
        if (modifier       is not null) defaults &= ~(int)HorizontalPagerDefault.Modifier;
        if (ContentPadding is not null) defaults &= ~(int)HorizontalPagerDefault.ContentPadding;

        PagerKt.HorizontalPager(
            state:                       jvmState,
            modifier:                    modifier,
            contentPadding:              ContentPadding?.Jvm,
            pageSize:                    null,
            p4:                          0,    // beyondViewportPageCount
            pageSpacing:                 0f,
            verticalAlignment:           null,
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
