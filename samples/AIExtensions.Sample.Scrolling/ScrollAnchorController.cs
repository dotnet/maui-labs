namespace AIExtensions.Sample.Scrolling;

/// <summary>
/// Connects one view's <see cref="ScrollAnchorTracker"/> to the platform: creates the platform adapter whenever the
/// view gets a handler, disposes it when the handler goes, and publishes <see cref="ScrollAnchor.IsAnchoredProperty"/>.
/// </summary>
internal sealed class ScrollAnchorController : IDisposable
{
    private readonly View _view;
    private readonly ScrollAnchorTracker _tracker = new();
    private ScrollAnchorAdapter? _adapter;
    private bool _scrollingToEnd;
    private bool _disposed;

    public ScrollAnchorController(View view)
    {
        _view = view;
        view.HandlerChanging += OnHandlerChanging;
        view.HandlerChanged += OnHandlerChanged;
        if (view is ItemsView itemsView)
            itemsView.ScrollToRequested += OnScrollToRequested;
        else if (view is ScrollView scrollView)
            scrollView.ScrollToRequested += OnScrollToRequested;

        Attach();
        Publish();
    }

    /// <summary>Gets the tracker that decides whether the view follows its end.</summary>
    internal ScrollAnchorTracker Tracker => _tracker;

    /// <summary>Gets the platform adapter, while the view has a handler on a supported platform.</summary>
    internal ScrollAnchorAdapter? Adapter => _adapter;

    /// <summary>Gets whether the anchor supports <paramref name="view"/>'s type.</summary>
    public static bool Supports(View view) => view is StructuredItemsView or ScrollView;

    /// <summary>Gets whether <paramref name="view"/> scrolls vertically, the only axis the anchor follows.</summary>
    public static bool ScrollsVertically(View view) => view switch
    {
        StructuredItemsView items => items.ItemsLayout is not ItemsLayout { Orientation: ItemsLayoutOrientation.Horizontal },
        ScrollView scroll => scroll.Orientation is ScrollOrientation.Vertical or ScrollOrientation.Both,
        _ => false,
    };

    /// <summary>
    /// Observes where the view is, after the platform laid out a change or the view scrolled. Called by the adapter.
    /// </summary>
    /// <returns>The end offset to scroll to without animating, or <see langword="null"/> to stay.</returns>
    /// <seealso cref="ScrollAnchorTracker.Update"/>
    public double? Update(ScrollMetrics metrics, bool isUserScrolling, bool sizeChanged = false)
    {
        var target = _tracker.Update(metrics, isUserScrolling, sizeChanged);
        Publish();
        return target;
    }

    public void ScrollToEnd(bool animate)
    {
        _tracker.ScrollToEnd(animate);
        Publish();
        if (_adapter is { } adapter)
        {
            adapter.ScrollToEnd(animate);
            return;
        }

        _scrollingToEnd = true;
        try
        {
            ScrollToEndWithoutAnchor(_view, animate);
        }
        finally
        {
            _scrollingToEnd = false;
        }
    }

    /// <summary>Scrolls a view without a platform adapter to its end, with the cross-platform scrolling APIs.</summary>
    public static void ScrollToEndWithoutAnchor(VisualElement view, bool animate)
    {
        switch (view)
        {
            case ScrollView scrollView:
                var y = Math.Max(0, scrollView.ContentSize.Height - scrollView.Height);
                _ = scrollView.ScrollToAsync(scrollView.ScrollX, y, animate);
                break;

            case GroupableItemsView { IsGrouped: true } grouped when grouped.ItemsSource is System.Collections.IEnumerable groups:
                var groupIndex = -1;
                var itemIndex = -1;
                var index = 0;
                foreach (var group in groups)
                {
                    var count = group is System.Collections.IEnumerable items ? items.Cast<object?>().Count() : 0;
                    if (count > 0)
                        (groupIndex, itemIndex) = (index, count - 1);
                    index++;
                }

                if (groupIndex >= 0)
                    grouped.ScrollTo(itemIndex, groupIndex, ScrollToPosition.End, animate);
                break;

            case ItemsView itemsView when itemsView.ItemsSource is System.Collections.IEnumerable source:
                var last = source.Cast<object?>().Count() - 1;
                if (last >= 0)
                    itemsView.ScrollTo(last, position: ScrollToPosition.End, animate: animate);
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _view.HandlerChanging -= OnHandlerChanging;
        _view.HandlerChanged -= OnHandlerChanged;
        if (_view is ItemsView itemsView)
            itemsView.ScrollToRequested -= OnScrollToRequested;
        else if (_view is ScrollView scrollView)
            scrollView.ScrollToRequested -= OnScrollToRequested;

        Detach();
    }

    private void Attach()
    {
        Detach();
        if (_view.Handler?.PlatformView is not { } platformView || !ScrollsVertically(_view))
            return;

        // A new platform view starts at its top: follow its end from the start again.
        _tracker.Reset();
        _adapter = ScrollAnchor.AdapterFactory(this, _view, platformView);
        _adapter?.Invalidate();
    }

    private void Detach()
    {
        _adapter?.Dispose();
        _adapter = null;
    }

    private void OnHandlerChanging(object? sender, HandlerChangingEventArgs e) => Detach();

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        Attach();
        Publish();
    }

    // The handler scrolls the platform view for the request (synchronously, or on its next layout pass); the next look
    // judges where it landed.
    private void OnScrollToRequested(object? sender, EventArgs e)
    {
        if (_scrollingToEnd || _adapter is not { } adapter)
            return;

        _tracker.RequestScroll();
        adapter.Invalidate();
    }

    // Only a platform adapter follows the end: without one (no handler yet, a horizontal view, a platform with no
    // adapter such as Windows), the view isn't anchored, whatever the tracker would decide.
    private void Publish()
    {
        if (!_disposed)
            ScrollAnchor.SetIsAnchored(_view, _adapter is not null && _tracker.IsAnchored);
    }
}
