using AndroidX.RecyclerView.Widget;
using AView = Android.Views.View;

namespace AIExtensions.Sample.Scrolling;

/// <summary>
/// Anchors the RecyclerView behind a CollectionView on Android, grouped or not, with or without a header and footer
/// (MAUI lays those out as items too).
/// </summary>
/// <remarks>
/// <para>
/// RecyclerView reports its scroll offset and range as estimates: LinearLayoutManager extrapolates them from the average
/// size of the rows on screen, so they jump whenever rows of another size scroll in. The adapter measures from the
/// layout instead. The offset is exact while the first row is laid out, and otherwise moves by exactly as much as a row
/// that stayed laid out moved since the last look. The distance to the end is exact whenever the last row is laid out:
/// the end of the lowest row against the end of the padded viewport, as LinearLayoutManager aligns it.
/// </para>
/// <para>
/// The user is scrolling while the RecyclerView's scroll state isn't idle: dragging, or settling after a fling (or
/// after a smooth scroll the app requested, which the anchor judges like a fling).
/// </para>
/// </remarks>
internal sealed class RecyclerViewAnchorAdapter : AndroidScrollAnchorAdapter
{
    private readonly RecyclerView _recyclerView;
    private RecyclerView.LayoutManager? _layoutManager;
    private OrientationHelper? _helper;
    private AView? _reference;
    private int _referenceStart;
    private int _estimatedOffset;
    private double _offset;
    private bool _hasOffset;

    public RecyclerViewAnchorAdapter(ScrollAnchorController controller, RecyclerView recyclerView)
        : base(controller, recyclerView) => _recyclerView = recyclerView;

    protected override bool IsUserScrolling => _recyclerView.ScrollState != RecyclerView.ScrollStateIdle;

    protected override ScrollMetrics? ReadMetrics()
    {
        if (GetLayout() is not { } layout)
            return null;

        var (layoutManager, helper) = layout;
        var viewport = helper.TotalSpace;
        var itemCount = layoutManager.ItemCount;
        var childCount = layoutManager.ChildCount;
        if (itemCount == 0)
        {
            _hasOffset = false;
            _reference = null;
            return new ScrollMetrics(0, 0, viewport / Density);
        }

        if (childCount == 0)
            return null;

        AView? first = null;
        var firstStart = 0;
        var referenceStart = 0;
        var referenceLaidOut = false;
        int? startOffset = null;
        var end = int.MinValue;
        var lastLaidOut = false;
        for (var index = 0; index < childCount; index++)
        {
            if (layoutManager.GetChildAt(index) is not { } child)
                continue;

            var start = helper.GetDecoratedStart(child);
            if (first is null || start < firstStart)
                (first, firstStart) = (child, start);

            if (ReferenceEquals(child, _reference))
                (referenceLaidOut, referenceStart) = (true, start);

            var position = layoutManager.GetPosition(child);
            if (position == 0)
                startOffset = helper.StartAfterPadding - start;
            if (position == itemCount - 1)
                lastLaidOut = true;

            // The lowest row ends the content: in a grid the last item can sit beside a taller one.
            end = Math.Max(end, helper.GetDecoratedEnd(child));
        }

        var estimatedOffset = _recyclerView.ComputeVerticalScrollOffset();
        if (startOffset is { } exact)
            _offset = exact;
        else if (_hasOffset && referenceLaidOut)
            _offset += _referenceStart - referenceStart;
        else if (_hasOffset)
            _offset += estimatedOffset - _estimatedOffset;
        else
            _offset = estimatedOffset;

        _hasOffset = true;
        _estimatedOffset = estimatedOffset;
        _reference = first;
        _referenceStart = firstStart;

        // Before the last row is laid out, all that's certain is that the end is beyond the viewport.
        double distance = lastLaidOut
            ? end - helper.EndAfterPadding
            : Math.Max(
                _recyclerView.ComputeVerticalScrollRange() - estimatedOffset - _recyclerView.ComputeVerticalScrollExtent(),
                viewport);

        var offset = _offset / Density;
        var viewportUnits = viewport / (double)Density;
        return new ScrollMetrics(offset, offset + viewportUnits + distance / Density, viewportUnits);
    }

    protected override void ScrollToEndCore(bool animate)
    {
        if (GetLayout() is not { } layout)
            return;

        var (layoutManager, helper) = layout;
        var last = layoutManager.ItemCount - 1;
        if (last < 0)
            return;

        if (layoutManager.FindViewByPosition(last) is null)
        {
            // Lay the last row out first; the next look scrolls the rest of the way.
            if (animate)
                _recyclerView.SmoothScrollToPosition(last);
            else
                _recyclerView.ScrollToPosition(last);
            return;
        }

        var end = int.MinValue;
        for (var index = 0; index < layoutManager.ChildCount; index++)
        {
            if (layoutManager.GetChildAt(index) is { } child)
                end = Math.Max(end, helper.GetDecoratedEnd(child));
        }

        var distance = end - helper.EndAfterPadding;
        if (distance <= 0)
            return;

        if (animate)
            _recyclerView.SmoothScrollBy(0, distance);
        else
            _recyclerView.ScrollBy(0, distance);
    }

    private (RecyclerView.LayoutManager LayoutManager, OrientationHelper Helper)? GetLayout()
    {
        var layoutManager = _recyclerView.GetLayoutManager();
        if (layoutManager is null || !layoutManager.CanScrollVertically() || _recyclerView.Height <= 0)
            return null;

        if (!ReferenceEquals(layoutManager, _layoutManager) || _helper is not { } helper)
        {
            // MAUI replaces the layout manager when ItemsLayout changes: start measuring again.
            helper = OrientationHelper.CreateVerticalHelper(layoutManager)
                ?? throw new InvalidOperationException("RecyclerView has no vertical orientation helper.");
            _layoutManager = layoutManager;
            _helper = helper;
            _hasOffset = false;
            _reference = null;
        }

        return (layoutManager, helper);
    }
}
