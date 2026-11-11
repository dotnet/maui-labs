namespace AIExtensions.Sample.Scrolling;

/// <summary>
/// Feeds a platform scrolling view's position to its <see cref="ScrollAnchorController"/> and applies the scrolls it
/// asks for. One per platform view, created when the view gets a handler and disposed when it loses it.
/// </summary>
/// <remarks>
/// An adapter reports <see cref="ScrollMetrics"/> in device-independent units once the platform has laid out a change
/// (so a size change and the offset adjustments the platform makes with it arrive together), and says whether the user
/// is scrolling. It must not replace anything the platform handler set up, such as the scroll view's delegate.
/// </remarks>
internal abstract partial class ScrollAnchorAdapter : IDisposable
{
    protected ScrollAnchorAdapter(ScrollAnchorController controller) => Controller = controller;

    protected ScrollAnchorController Controller { get; }

    /// <summary>Creates the adapter for <paramref name="platformView"/>, or returns <see langword="null"/> where there is none.</summary>
    internal static partial ScrollAnchorAdapter? Create(ScrollAnchorController controller, View view, object platformView);

    /// <summary>Reports the view's position again soon, even if nothing changed.</summary>
    public abstract void Invalidate();

    /// <summary>Scrolls to the end of the content.</summary>
    public abstract void ScrollToEnd(bool animate);

    /// <summary>Stops observing the platform view and releases everything registered on it.</summary>
    public abstract void Dispose();
}
