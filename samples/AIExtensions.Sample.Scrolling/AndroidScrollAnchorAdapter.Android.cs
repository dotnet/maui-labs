using Android.Views;
using AView = Android.Views.View;

namespace AIExtensions.Sample.Scrolling;

/// <summary>
/// The Android side shared by the RecyclerView and NestedScrollView adapters: looks at the view once per frame, after
/// layout and before drawing, so a change is followed in the frame that shows it.
/// </summary>
/// <remarks>
/// It registers a pre-draw listener on the window's view tree while the view is attached to a window (the view tree
/// belongs to the window, not the view, so the listener is removed on detach), and an attach-state listener on the view.
/// It leaves the view's own listeners alone: MAUI's handlers own them.
/// </remarks>
internal abstract class AndroidScrollAnchorAdapter : ScrollAnchorAdapter
{
    private readonly Listener _listener;
    private ViewTreeObserver? _treeObserver;
    private bool _disposed;

    protected AndroidScrollAnchorAdapter(ScrollAnchorController controller, AView view)
        : base(controller)
    {
        PlatformView = view;
        var density = view.Resources?.DisplayMetrics?.Density ?? 1f;
        Density = density > 0 ? density : 1f;
        _listener = new Listener(this);
        view.AddOnAttachStateChangeListener(_listener);
        if (view.IsAttachedToWindow)
            StartObserving();
    }

    protected AView PlatformView { get; }

    /// <summary>Gets how many device pixels make one device-independent unit.</summary>
    protected float Density { get; }

    /// <summary>Gets whether the user is scrolling the view: dragging it, or flinging it.</summary>
    protected abstract bool IsUserScrolling { get; }

    // A frame runs layout, then the pre-draw listeners: the next look sees whatever changed.
    public override void Invalidate()
    {
        if (!_disposed)
            PlatformView.PostInvalidateOnAnimation();
    }

    public override void ScrollToEnd(bool animate)
    {
        if (!_disposed)
            ScrollToEndCore(animate);
    }

    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopObserving();
        PlatformView.RemoveOnAttachStateChangeListener(_listener);
        _listener.Dispose();
    }

    /// <summary>Reads where the view is, in device-independent units, or returns <see langword="null"/> before its first layout.</summary>
    protected abstract ScrollMetrics? ReadMetrics();

    /// <summary>Scrolls to the end of the content, from the layout as it is now.</summary>
    protected abstract void ScrollToEndCore(bool animate);

    private void Update()
    {
        if (!_disposed && ReadMetrics() is { } metrics && Controller.Update(metrics, IsUserScrolling) is not null)
            ScrollToEndCore(animate: false);
    }

    private void StartObserving()
    {
        StopObserving();
        _treeObserver = PlatformView.ViewTreeObserver;
        _treeObserver?.AddOnPreDrawListener(_listener);
        PlatformView.PostInvalidateOnAnimation();
    }

    private void StopObserving()
    {
        if (_treeObserver is { } observer)
        {
            // A view tree observer that merged into the window's is dead; the live one is the view's current one.
            (observer.IsAlive ? observer : PlatformView.ViewTreeObserver)?.RemoveOnPreDrawListener(_listener);
            _treeObserver = null;
        }
    }

    private sealed class Listener(AndroidScrollAnchorAdapter owner) : Java.Lang.Object, ViewTreeObserver.IOnPreDrawListener, AView.IOnAttachStateChangeListener
    {
        // The window's view tree outlives the view: hold the adapter weakly from there.
        private readonly WeakReference<AndroidScrollAnchorAdapter> _owner = new(owner);

        public bool OnPreDraw()
        {
            if (_owner.TryGetTarget(out var adapter))
                adapter.Update();

            return true;
        }

        public void OnViewAttachedToWindow(AView attachedView)
        {
            if (_owner.TryGetTarget(out var adapter) && !adapter._disposed)
                adapter.StartObserving();
        }

        public void OnViewDetachedFromWindow(AView detachedView)
        {
            if (_owner.TryGetTarget(out var adapter))
                adapter.StopObserving();
        }
    }
}
