using AndroidX.Compose.Foundation.Pager;

namespace AndroidX.Compose;

/// <summary>
/// Caller-supplied state holder for <see cref="HorizontalPager{T}"/> /
/// <see cref="VerticalPager{T}"/>. Wraps the bound
/// <c>androidx.compose.foundation.pager.PagerState</c> — the
/// <c>PagerStateKt.PagerState(currentPage, currentPageOffsetFraction,
/// pageCount)</c> factory is bound and not <c>@Composable</c>, so the
/// JVM peer is built eagerly in the constructor and reads like
/// <see cref="CurrentPage"/> hit Compose's snapshot system from the
/// first composition pass — no render-order dependency.
/// </summary>
/// <remarks>
/// <para>
/// Construct one inside a <c>composer.Remember</c>
/// callback so the page position survives recompositions. The
/// <c>pageCount</c> lambda must be supplied (the Kotlin
/// runtime calls it on every measure pass) and should close over a
/// stable source that matches the <see cref="HorizontalPager{T}"/> /
/// <see cref="VerticalPager{T}"/> items list — otherwise the indicator
/// and the rendered pages can drift.
/// </para>
/// <code>
/// var items      = new[] { 0, 1, 2 };
/// var pagerState = Remember(() =&gt; new PagerState(pageCount: () =&gt; items.Length));
///
/// new Column
/// {
///     new HorizontalPager&lt;int&gt;(
///         items:        items,
///         itemContent:  i =&gt; new Text($"Page {i}"))
///     {
///         State = pagerState,
///     },
///     // Reactive — snapshot read on PagerState.currentPage is
///     // recorded for this composition scope regardless of order.
///     new Text($"Page {pagerState.CurrentPage + 1} of {pagerState.PageCount}"),
/// }
/// </code>
/// <para>
/// Unlike Kotlin's <c>rememberPagerState()</c> (which uses
/// <c>rememberSaveable</c>), this state is held in the
/// <c>ComposeExtensions</c>-scoped Remember cache, so it survives
/// recompositions but not process death / configuration changes. For
/// most apps that's fine; if you need savable scroll position, track
/// it yourself with the rest of your view-model state.
/// </para>
/// </remarks>
public sealed class PagerState
{
    // Kept on the wrapper so the JVM-side reference to the lambda
    // outlives any temporary parameter slot — matches the pattern
    // other wrappers use for stored callbacks.
    readonly ComposableLambda0Int _pageCountFn;

    /// <summary>
    /// Create a new <see cref="PagerState"/>. The underlying Kotlin
    /// <c>androidx.compose.foundation.pager.PagerState</c> is built
    /// immediately via <c>PagerStateKt.PagerState(...)</c>, so reads
    /// like <see cref="CurrentPage"/> can be subscribed to from the
    /// first composition pass — even when read from C# build code
    /// rendered <em>before</em> the pager.
    /// </summary>
    /// <param name="pageCount">
    /// Lambda invoked by the pager on every measure pass to determine
    /// the total page count. Must close over a stable source that
    /// matches the <see cref="HorizontalPager{T}"/> /
    /// <see cref="VerticalPager{T}"/> items list passed alongside this
    /// state — recomposition-local list rebuilds will be missed.
    /// </param>
    /// <param name="initialPage">
    /// Index of the page to start on (default <c>0</c>).
    /// </param>
    /// <param name="initialPageOffsetFraction">
    /// Initial fractional offset in <c>[-0.5, 0.5)</c> (default
    /// <c>0f</c>).
    /// </param>
    public PagerState(Func<int> pageCount, int initialPage = 0, float initialPageOffsetFraction = 0f)
    {
        ArgumentNullException.ThrowIfNull(pageCount);
        _pageCountFn = new ComposableLambda0Int(pageCount);
        Jvm = PagerStateKt.PagerState(
            currentPage:               initialPage,
            currentPageOffsetFraction: initialPageOffsetFraction,
            pageCount:                 _pageCountFn);
    }

    /// <summary>
    /// Underlying <c>androidx.compose.foundation.pager.PagerState</c>
    /// built by <c>PagerStateKt.PagerState(...)</c>. Non-null from
    /// construction.
    /// </summary>
    internal AndroidX.Compose.Foundation.Pager.PagerState Jvm { get; }

    /// <summary>
    /// Index of the page closest to the snapped position. Mirrors
    /// Kotlin's <c>PagerState.currentPage</c>. Snapshot-tracked — reads
    /// from a live composition scope subscribe to page changes.
    /// </summary>
    public int CurrentPage => Jvm.CurrentPage;

    /// <summary>
    /// Index of the page the pager has settled on after a fling or
    /// programmatic scroll completes. Mirrors Kotlin's
    /// <c>PagerState.settledPage</c>.
    /// </summary>
    public int SettledPage => Jvm.SettledPage;

    /// <summary>
    /// Index of the page the pager is currently animating toward
    /// (== <see cref="CurrentPage"/> when no scroll is in flight).
    /// Mirrors Kotlin's <c>PagerState.targetPage</c>.
    /// </summary>
    public int TargetPage => Jvm.TargetPage;

    /// <summary>
    /// Fractional offset of the current page in <c>[-0.5, 0.5)</c>,
    /// where <c>0</c> means the page is fully snapped. Mirrors Kotlin's
    /// <c>PagerState.currentPageOffsetFraction</c>.
    /// </summary>
    public float CurrentPageOffsetFraction => Jvm.CurrentPageOffsetFraction;

    /// <summary>
    /// Total number of pages reported by the <c>pageCount</c> lambda
    /// supplied at construction. Mirrors Kotlin's
    /// <c>PagerState.pageCount</c>.
    /// </summary>
    public int PageCount => Jvm.PageCount;
}
