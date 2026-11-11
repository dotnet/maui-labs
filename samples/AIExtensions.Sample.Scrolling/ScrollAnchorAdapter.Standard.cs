namespace AIExtensions.Sample.Scrolling;

internal abstract partial class ScrollAnchorAdapter
{
    // No platform scrolling view to observe: setting the mode has no effect.
    internal static partial ScrollAnchorAdapter? Create(ScrollAnchorController controller, View view, object platformView) => null;
}
