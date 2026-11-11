using Android.Views;
using AndroidX.Core.Widget;

namespace AIExtensions.Sample.Scrolling;

/// <summary>Anchors the NestedScrollView behind a vertical ScrollView on Android.</summary>
/// <remarks>
/// <para>
/// The metrics are exact: the scroll offset, the content's height with its margins, and the padded viewport, which is
/// how NestedScrollView computes its own scroll range.
/// </para>
/// <para>
/// NestedScrollView doesn't expose whether the user is dragging or flinging it (it keeps that state private, and the
/// view's touch listener belongs to MAUI's gesture handling), so this adapter never reports a user scroll. Scrolls
/// still disengage the anchor by moving away from the end, but one in the same frame as a size change counts as the
/// layout's: while the content grows every frame, a drag that hasn't yet left the end is pulled back to it.
/// </para>
/// </remarks>
internal sealed class NestedScrollViewAnchorAdapter(ScrollAnchorController controller, NestedScrollView scrollView)
    : AndroidScrollAnchorAdapter(controller, scrollView)
{
    protected override bool IsUserScrolling => false;

    protected override ScrollMetrics? ReadMetrics()
    {
        if (Measure() is not { } layout)
            return null;

        return new ScrollMetrics(layout.Offset / Density, layout.Extent / Density, layout.Viewport / Density);
    }

    protected override void ScrollToEndCore(bool animate)
    {
        if (Measure() is not { } layout)
            return;

        var end = (int)Math.Max(0, layout.Extent - layout.Viewport);
        if (animate)
            scrollView.SmoothScrollTo(scrollView.ScrollX, end);
        else
            scrollView.ScrollTo(scrollView.ScrollX, end);
    }

    private (double Offset, double Extent, double Viewport)? Measure()
    {
        if (scrollView.Height <= 0 || scrollView.ChildCount == 0 || scrollView.GetChildAt(0) is not { } content)
            return null;

        var margins = content.LayoutParameters is ViewGroup.MarginLayoutParams layout ? layout.TopMargin + layout.BottomMargin : 0;
        return (scrollView.ScrollY, content.Height + margins, scrollView.Height - scrollView.PaddingTop - scrollView.PaddingBottom);
    }
}
