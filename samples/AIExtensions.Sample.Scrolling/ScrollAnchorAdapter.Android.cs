using AndroidX.Core.Widget;
using AndroidX.RecyclerView.Widget;

namespace AIExtensions.Sample.Scrolling;

internal abstract partial class ScrollAnchorAdapter
{
    // CollectionView's platform view is a RecyclerView (MauiRecyclerView); ScrollView's is a NestedScrollView (MauiScrollView).
    internal static partial ScrollAnchorAdapter? Create(ScrollAnchorController controller, View view, object platformView) =>
        platformView switch
        {
            RecyclerView recyclerView => new RecyclerViewAnchorAdapter(controller, recyclerView),
            NestedScrollView scrollView => new NestedScrollViewAnchorAdapter(controller, scrollView),
            _ => null,
        };
}
