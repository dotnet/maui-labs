using System.Runtime.InteropServices;
using CoreFoundation;
using CoreGraphics;
using Foundation;
using UIKit;

namespace AIExtensions.Sample.Scrolling;

/// <summary>
/// Anchors a <see cref="UIScrollView"/>: the UICollectionView behind a CollectionView, or a ScrollView's scroll view.
/// </summary>
/// <remarks>
/// <para>
/// It leaves the scroll view's delegate alone (MAUI's handlers own it) and observes the scroll view instead, with
/// key-value observing: <c>contentSize</c> changes as items are added, removed, or resized (including the cells UIKit
/// self-sizes as they appear), <c>frame</c> and <c>bounds</c> as the view resizes or scrolls, <c>contentInset</c> as the
/// insets change (such as for the keyboard), and <c>contentOffset</c> as it scrolls. UIKit sets these one at a time
/// while it lays out, so each change schedules a single look on the main queue, which runs once the layout pass that
/// made the changes is over: the size change and any offset adjustment UIKit made with it are judged together. A size
/// that changed and changed back before the look (UICollectionView re-estimates rows it measured before, then measures
/// them again) still counts as a size change.
/// </para>
/// <para>
/// The user is scrolling while the scroll view is dragging (a touch drag, or a trackpad or mouse-wheel scroll on Mac
/// Catalyst) or decelerating after one. The scroll view's pan gesture recognizer reports when a drag begins and ends,
/// and the content offset keeps changing until the deceleration stops.
/// </para>
/// </remarks>
internal sealed class UIScrollViewAnchorAdapter : ScrollAnchorAdapter
{
    private readonly UIScrollView _scrollView;
    private readonly IDisposable[] _observers;
    private readonly UIPanGestureRecognizer _pan;
    private readonly UIGestureRecognizer.Token _panTarget;
    private CGSize _contentSize;
    private CGSize _size;
    private UIEdgeInsets _contentInset;
    private bool _sizeChanged;
    private bool _updateScheduled;
    private bool _disposed;

    public UIScrollViewAnchorAdapter(ScrollAnchorController controller, UIScrollView scrollView)
        : base(controller)
    {
        _scrollView = scrollView;
        _contentSize = scrollView.ContentSize;
        _size = scrollView.Bounds.Size;
        _contentInset = scrollView.ContentInset;
        _observers =
        [
            scrollView.AddObserver("contentSize", NSKeyValueObservingOptions.New, OnSizeChanged),
            scrollView.AddObserver("contentInset", NSKeyValueObservingOptions.New, OnSizeChanged),
            scrollView.AddObserver("frame", NSKeyValueObservingOptions.New, OnSizeChanged),
            scrollView.AddObserver("bounds", NSKeyValueObservingOptions.New, OnSizeChanged),
            scrollView.AddObserver("contentOffset", NSKeyValueObservingOptions.New, OnOffsetChanged),
        ];
        _pan = scrollView.PanGestureRecognizer;
        _panTarget = _pan.AddTarget(ScheduleUpdate);
    }

    private bool IsUserScrolling => _scrollView.Dragging || _scrollView.Decelerating;

    public override void Invalidate() => ScheduleUpdate();

    public override void ScrollToEnd(bool animate)
    {
        if (_disposed)
            return;

        ScrollTo(ReadMetrics().EndOffset, animate);
        ScheduleUpdate();
    }

    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _pan.RemoveTarget(_panTarget);
        foreach (var observer in _observers)
            observer.Dispose();
    }

    private void OnOffsetChanged(NSObservedChange change) => ScheduleUpdate();

    // KVO reports every assignment, and UICollectionView assigns its content size on every layout pass, scrolling
    // included; the bounds change with every scroll too (their origin is the offset). Only a different size is a resize.
    private void OnSizeChanged(NSObservedChange change)
    {
        var contentSize = _scrollView.ContentSize;
        var size = _scrollView.Bounds.Size;
        var contentInset = _scrollView.ContentInset;
        if (!contentSize.Equals(_contentSize) || !size.Equals(_size) || !contentInset.Equals(_contentInset))
        {
            (_contentSize, _size, _contentInset) = (contentSize, size, contentInset);
            _sizeChanged = true;
        }

        ScheduleUpdate();
    }

    private void ScheduleUpdate()
    {
        if (_updateScheduled || _disposed)
            return;

        _updateScheduled = true;
        DispatchQueue.MainQueue.DispatchAsync(Update);
    }

    private void Update()
    {
        _updateScheduled = false;
        if (_disposed || _scrollView.Window is null)
            return;

        var sizeChanged = _sizeChanged;
        _sizeChanged = false;
        if (Controller.Update(ReadMetrics(), IsUserScrolling, sizeChanged) is { } end)
            ScrollTo(end, animate: false);
    }

    // Offset 0 shows the start of the content, below the top inset; the extent includes both insets.
    private ScrollMetrics ReadMetrics()
    {
        var inset = _scrollView.AdjustedContentInset;
        return new ScrollMetrics(
            Offset: _scrollView.ContentOffset.Y + inset.Top,
            Extent: _scrollView.ContentSize.Height + inset.Top + inset.Bottom,
            Viewport: _scrollView.Bounds.Height);
    }

    private void ScrollTo(double offset, bool animate)
    {
        var point = _scrollView.ContentOffset;
        point.Y = (NFloat)(offset - _scrollView.AdjustedContentInset.Top);
        _scrollView.SetContentOffset(point, animate);
    }
}
