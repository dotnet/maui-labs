using UIKit;

namespace AIExtensions.Sample.Scrolling;

internal abstract partial class ScrollAnchorAdapter
{
    // CollectionView's platform view is its UICollectionView (Items2, the default on iOS and Mac Catalyst, and the older
    // Items handler alike); ScrollView's is a UIScrollView. Look a little deeper in case a handler wraps it.
    internal static partial ScrollAnchorAdapter? Create(ScrollAnchorController controller, View view, object platformView) =>
        FindScrollView(platformView as UIView, depth: 3) is { } scrollView
            ? new UIScrollViewAnchorAdapter(controller, scrollView)
            : null;

    private static UIScrollView? FindScrollView(UIView? view, int depth)
    {
        if (view is UIScrollView scrollView)
            return scrollView;

        if (view is null || depth == 0)
            return null;

        foreach (var subview in view.Subviews)
        {
            if (FindScrollView(subview, depth - 1) is { } found)
                return found;
        }

        return null;
    }
}
