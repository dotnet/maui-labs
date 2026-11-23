using System.Collections.Specialized;
#if IOS || MACCATALYST
using System.Runtime.InteropServices;
using CoreFoundation;
using CoreGraphics;
using Foundation;
using UIKit;
#elif ANDROID
using Android.Views;
using AndroidX.RecyclerView.Widget;
using AView = Android.Views.View;
#endif

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Shows the conversation of the chat tab, and keeps the end of its <see cref="CollectionView"/> in view while turns
/// are added or stream in (standing in for <c>AnchorMode = ScrollAnchorMode.End</c>,
/// <see href="https://github.com/dotnet/maui/pull/40211">dotnet/maui#40211</see>).
/// </summary>
public partial class ChatArea : ContentView
{
    private readonly ScrollAnchorTracker _tracker = new();
    private IScrollAnchorAdapter? _adapter;
    private ChatViewModel? _viewModel;
    private bool _scrollToNewTurn;
    private bool _scrollingToEnd;

    /// <summary>Initializes the conversation view.</summary>
    public ChatArea()
    {
        InitializeComponent();
        ConversationView.HandlerChanging += OnConversationHandlerChanging;
        ConversationView.HandlerChanged += OnConversationHandlerChanged;
        ConversationView.ScrollToRequested += OnConversationScrollToRequested;
    }

    /// <inheritdoc />
    protected override void OnBindingContextChanged()
    {
        Unsubscribe();
        base.OnBindingContextChanged();

        _viewModel = BindingContext as ChatViewModel;
        if (_viewModel is not null)
        {
            ((INotifyCollectionChanged)_viewModel.Session.Turns).CollectionChanged += OnTurnsChanged;
            _viewModel.Items.CollectionChanged += OnItemsChanged;
        }

        _scrollToNewTurn = false;
        _tracker.Reset();
        _adapter?.Invalidate();
    }

    private void Unsubscribe()
    {
        if (_viewModel is null)
            return;

        ((INotifyCollectionChanged)_viewModel.Session.Turns).CollectionChanged -= OnTurnsChanged;
        _viewModel.Items.CollectionChanged -= OnItemsChanged;
        _viewModel = null;
    }

    private void OnTurnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            // Sending a new turn brings it into view and follows its response, even if the user had scrolled up.
            // ConversationItems may have already added the prompt before this handler runs, or may add it next.
            _scrollToNewTurn = true;
            ScrollToEnd(animate: false);
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _scrollToNewTurn = false;
            _tracker.Reset();
            _adapter?.Invalidate();
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_scrollToNewTurn && e.Action == NotifyCollectionChangedAction.Add)
        {
            _scrollToNewTurn = false;
            ScrollToEnd(animate: false);
            return;
        }

        if (_tracker.IsAnchored)
            _adapter?.Invalidate();
    }

    private void ScrollToEnd(bool animate)
    {
        _tracker.ScrollToEnd(animate);
        if (_adapter is { } adapter)
        {
            adapter.ScrollToEnd(animate);
            return;
        }

        if (_viewModel is not { Items.Count: > 0 })
            return;

        _scrollingToEnd = true;
        try
        {
            ConversationView.ScrollTo(_viewModel.Items.Count - 1, position: ScrollToPosition.End, animate: animate);
        }
        finally
        {
            _scrollingToEnd = false;
        }
    }

    private void OnConversationHandlerChanging(object? sender, HandlerChangingEventArgs e)
    {
        _adapter?.Dispose();
        _adapter = null;
    }

    private void OnConversationHandlerChanged(object? sender, EventArgs e)
    {
        _adapter?.Dispose();
        _adapter = null;
        if (ConversationView.Handler?.PlatformView is not { } platformView)
            return;

        _tracker.Reset();
        _adapter = CreateAdapter(_tracker, platformView);
        _adapter?.Invalidate();
    }

    private void OnConversationScrollToRequested(object? sender, ScrollToRequestEventArgs e)
    {
        if (_scrollingToEnd || _adapter is not { } adapter)
            return;

        _tracker.RequestScroll();
        adapter.Invalidate();
    }

    private interface IScrollAnchorAdapter : IDisposable
    {
        void Invalidate();

        void ScrollToEnd(bool animate);
    }

#if IOS || MACCATALYST
    private static IScrollAnchorAdapter? CreateAdapter(ScrollAnchorTracker tracker, object platformView) =>
        FindScrollView(platformView as UIView, depth: 3) is { } scrollView
            ? new UIScrollViewAnchorAdapter(tracker, scrollView)
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

    private sealed class UIScrollViewAnchorAdapter : IScrollAnchorAdapter
    {
        private readonly ScrollAnchorTracker _tracker;
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

        public UIScrollViewAnchorAdapter(ScrollAnchorTracker tracker, UIScrollView scrollView)
        {
            _tracker = tracker;
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

        public void Invalidate() => ScheduleUpdate();

        public void ScrollToEnd(bool animate)
        {
            if (_disposed)
                return;

            ScrollTo(ReadMetrics().EndOffset, animate);
            ScheduleUpdate();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _pan.RemoveTarget(_panTarget);
            foreach (var observer in _observers)
                observer.Dispose();
        }

        private void OnOffsetChanged(NSObservedChange change) => ScheduleUpdate();

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
            if (_tracker.Update(ReadMetrics(), IsUserScrolling, sizeChanged) is { } end)
                ScrollTo(end, animate: false);
        }

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
#elif ANDROID
    private static IScrollAnchorAdapter? CreateAdapter(ScrollAnchorTracker tracker, object platformView) =>
        platformView is RecyclerView recyclerView
            ? new RecyclerViewAnchorAdapter(tracker, recyclerView)
            : null;

    private sealed class RecyclerViewAnchorAdapter : IScrollAnchorAdapter
    {
        private readonly ScrollAnchorTracker _tracker;
        private readonly RecyclerView _recyclerView;
        private readonly float _density;
        private readonly Listener _listener;
        private ViewTreeObserver? _treeObserver;
        private RecyclerView.LayoutManager? _layoutManager;
        private OrientationHelper? _helper;
        private AView? _reference;
        private int _referenceStart;
        private int _estimatedOffset;
        private double _offset;
        private bool _hasOffset;
        private bool _disposed;

        public RecyclerViewAnchorAdapter(ScrollAnchorTracker tracker, RecyclerView recyclerView)
        {
            _tracker = tracker;
            _recyclerView = recyclerView;
            var density = recyclerView.Resources?.DisplayMetrics?.Density ?? 1f;
            _density = density > 0 ? density : 1f;
            _listener = new Listener(this);
            recyclerView.AddOnAttachStateChangeListener(_listener);
            if (recyclerView.IsAttachedToWindow)
                StartObserving();
        }

        private bool IsUserScrolling => _recyclerView.ScrollState != RecyclerView.ScrollStateIdle;

        public void Invalidate()
        {
            if (!_disposed)
                _recyclerView.PostInvalidateOnAnimation();
        }

        public void ScrollToEnd(bool animate)
        {
            if (_disposed)
                return;

            ScrollToEndCore(animate);
            _recyclerView.PostInvalidateOnAnimation();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            StopObserving();
            _recyclerView.RemoveOnAttachStateChangeListener(_listener);
            _listener.Dispose();
        }

        private void Update()
        {
            if (!_disposed && ReadMetrics() is { } metrics && _tracker.Update(metrics, IsUserScrolling) is not null)
                ScrollToEndCore(animate: false);
        }

        private ScrollMetrics? ReadMetrics()
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
                return new ScrollMetrics(0, 0, viewport / _density);
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

            double distance = lastLaidOut
                ? end - helper.EndAfterPadding
                : Math.Max(
                    _recyclerView.ComputeVerticalScrollRange() - estimatedOffset - _recyclerView.ComputeVerticalScrollExtent(),
                    viewport);

            var offset = _offset / _density;
            var viewportUnits = viewport / (double)_density;
            return new ScrollMetrics(offset, offset + viewportUnits + distance / _density, viewportUnits);
        }

        private void ScrollToEndCore(bool animate)
        {
            if (GetLayout() is not { } layout)
                return;

            var (layoutManager, helper) = layout;
            var last = layoutManager.ItemCount - 1;
            if (last < 0)
                return;

            if (layoutManager.FindViewByPosition(last) is null)
            {
                if (animate)
                {
                    _recyclerView.SmoothScrollToPosition(last);
                }
                else
                {
                    _tracker.ScrollToEnd(animated: false);
                    _recyclerView.ScrollToPosition(last);
                    _recyclerView.PostInvalidateOnAnimation();
                }

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
                helper = OrientationHelper.CreateVerticalHelper(layoutManager)
                    ?? throw new InvalidOperationException("RecyclerView has no vertical orientation helper.");
                _layoutManager = layoutManager;
                _helper = helper;
                _hasOffset = false;
                _reference = null;
            }

            return (layoutManager, helper);
        }

        private void StartObserving()
        {
            StopObserving();
            _treeObserver = _recyclerView.ViewTreeObserver;
            _treeObserver?.AddOnPreDrawListener(_listener);
            _recyclerView.PostInvalidateOnAnimation();
        }

        private void StopObserving()
        {
            if (_treeObserver is { } observer)
            {
                (observer.IsAlive ? observer : _recyclerView.ViewTreeObserver)?.RemoveOnPreDrawListener(_listener);
                _treeObserver = null;
            }
        }

        private sealed class Listener(RecyclerViewAnchorAdapter owner) : Java.Lang.Object, ViewTreeObserver.IOnPreDrawListener, AView.IOnAttachStateChangeListener
        {
            private readonly WeakReference<RecyclerViewAnchorAdapter> _owner = new(owner);

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
#else
    private static IScrollAnchorAdapter? CreateAdapter(ScrollAnchorTracker tracker, object platformView) => null;
#endif
}
