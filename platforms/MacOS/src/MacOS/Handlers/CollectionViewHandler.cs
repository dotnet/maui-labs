using System.Collections;
using System.Collections.Specialized;
using CoreGraphics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using AppKit;
using Foundation;
using ObjCRuntime;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class CollectionViewHandler : MacOSViewHandler<CollectionView, NSScrollView>
{
    public static readonly IPropertyMapper<CollectionView, CollectionViewHandler> Mapper =
        new PropertyMapper<CollectionView, CollectionViewHandler>(ViewMapper)
        {
            [nameof(ItemsView.ItemsSource)] = MapItemsSource,
            [nameof(ItemsView.ItemTemplate)] = MapItemTemplate,
            [nameof(ItemsView.EmptyView)] = MapEmptyView,
            [nameof(ItemsView.EmptyViewTemplate)] = MapEmptyView,
            [nameof(StructuredItemsView.ItemsLayout)] = MapItemsLayout,
            [nameof(StructuredItemsView.Header)] = MapHeaderFooter,
            [nameof(StructuredItemsView.Footer)] = MapHeaderFooter,
            [nameof(StructuredItemsView.HeaderTemplate)] = MapHeaderFooter,
            [nameof(StructuredItemsView.FooterTemplate)] = MapHeaderFooter,
            [nameof(SelectableItemsView.SelectionMode)] = MapSelectionMode,
            [nameof(SelectableItemsView.SelectedItem)] = MapSelectedItem,
            [nameof(GroupableItemsView.IsGrouped)] = MapIsGrouped,
            [nameof(GroupableItemsView.GroupHeaderTemplate)] = MapGroupHeaderTemplate,
            [nameof(GroupableItemsView.GroupFooterTemplate)] = MapGroupFooterTemplate,
        };

    public static readonly CommandMapper<CollectionView, CollectionViewHandler> CvCommandMapper =
        new(ViewCommandMapper)
        {
            [nameof(ItemsView.ScrollTo)] = MapScrollTo,
        };

    FlippedDocumentView? _documentView;
    MacOSContainerView? _itemsContainer;
    INotifyCollectionChanged? _observableSource;
    NSObject? _scrollObserver;

    // Virtualization state
    readonly List<ItemInfo> _flatItems = new();
    readonly Dictionary<int, (IView mauiView, NSView platformView)> _visibleViews = new();
    readonly Dictionary<DataTemplate, Queue<IView>> _recyclePool = new();
    nfloat _estimatedItemHeight = 44;
    bool _positionsCalculated;
    readonly HashSet<int> _selectedIndices = new();

    // Header/Footer/EmptyView
    NSView? _emptyView;
    IView? _emptyMauiView;
    bool _remainingThresholdFired;
    bool _isInLayout;
    bool _isReloading;

    // Threshold: render items this far beyond the visible rect
    static readonly nfloat OverScanPixels = 200;

    public CollectionViewHandler() : base(Mapper, CvCommandMapper)
    {
    }

    protected override NSScrollView CreatePlatformView()
    {
        var scrollView = new NSScrollView
        {
            HasVerticalScroller = true,
            HasHorizontalScroller = true,
            AutohidesScrollers = true,
            DrawsBackground = false,
        };

        _documentView = new FlippedDocumentView();
        _itemsContainer = new MacOSContainerView();
        _documentView.AddSubview(_itemsContainer);
        scrollView.DocumentView = _documentView;

        return scrollView;
    }

    protected override void ConnectHandler(NSScrollView platformView)
    {
        base.ConnectHandler(platformView);
        SubscribeScroll();
        if (VirtualView is ItemsView itemsView)
            itemsView.ScrollToRequested += OnScrollToRequested;
    }

    protected override void DisconnectHandler(NSScrollView platformView)
    {
        UnsubscribeScroll();
        UnsubscribeCollection();
        if (VirtualView is ItemsView itemsView)
            itemsView.ScrollToRequested -= OnScrollToRequested;
        base.DisconnectHandler(platformView);
    }

    void OnScrollToRequested(object? sender, ScrollToRequestEventArgs args)
        => HandleScrollTo(args);

    public override void PlatformArrange(Rect rect)
    {
        base.PlatformArrange(rect);
        if (rect.Width > 0 && rect.Height > 0 && !_isInLayout)
        {
            _isInLayout = true;
            try
            {
                CalculatePositions(rect);
                UpdateVisibleItems(rect);
            }
            finally
            {
                _isInLayout = false;
            }
        }
    }

    #region Scroll Observation

    void SubscribeScroll()
    {
        var clipView = PlatformView.ContentView;
        clipView.PostsBoundsChangedNotifications = true;
        _scrollObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            NSView.BoundsChangedNotification,
            OnScrollChanged,
            clipView);
    }

    void UnsubscribeScroll()
    {
        if (_scrollObserver != null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(_scrollObserver);
            _scrollObserver = null;
        }
    }

    void OnScrollChanged(NSNotification notification)
    {
        UpdateVisibleItems();
        CheckRemainingItemsThreshold();
    }

    #endregion

    #region Virtualization

    record ItemInfo
    {
        public object DataItem { get; init; } = null!;
        public DataTemplate? Template { get; init; }
        public bool IsGroupHeader { get; init; }
        public bool IsGroupFooter { get; init; }
        public bool IsHeader { get; init; }
        public bool IsFooter { get; init; }
        public nfloat Position { get; set; }
        public nfloat Size { get; set; }
        public bool Measured { get; set; }
    }

    void CalculatePositions(Rect rect)
    {
        if (_flatItems.Count == 0)
        {
            // Ensure document view fills the scroll view for empty state
            if (_documentView != null)
                _documentView.Frame = new CGRect(0, 0, rect.Width, rect.Height);
            if (_itemsContainer != null)
                _itemsContainer.Frame = new CGRect(0, 0, rect.Width, rect.Height);
            UpdateEmptyView(rect);
            return;
        }

        RemoveEmptyView();

        var layout = (VirtualView as StructuredItemsView)?.ItemsLayout;
        var isHorizontal = GetOrientation(layout) == ItemsLayoutOrientation.Horizontal;
        var span = GetSpan(layout);
        var itemSpacing = (nfloat)GetItemSpacing(layout);
        var lineSpacing = (nfloat)GetLineSpacing(layout);

        if (span > 1)
            CalculateGridPositions(rect, span, itemSpacing, lineSpacing, isHorizontal);
        else if (isHorizontal)
            CalculateLinearPositions(rect, itemSpacing, isHorizontal: true);
        else
            CalculateLinearPositions(rect, itemSpacing, isHorizontal: false);

        // Resize document/container
        var totalSize = _flatItems.Count > 0
            ? _flatItems[^1].Position + _flatItems[^1].Size
            : 0;

        if (isHorizontal)
        {
            _itemsContainer!.Frame = new CGRect(0, 0, totalSize, rect.Height);
            _documentView!.Frame = new CGRect(0, 0, totalSize, rect.Height);
        }
        else
        {
            _itemsContainer!.Frame = new CGRect(0, 0, rect.Width, totalSize);
            _documentView!.Frame = new CGRect(0, 0, rect.Width, totalSize);
        }

        _positionsCalculated = true;
    }

    void CalculateLinearPositions(Rect rect, nfloat spacing, bool isHorizontal)
    {
        nfloat offset = 0;
        foreach (var info in _flatItems)
        {
            info.Position = offset;
            if (!info.Measured)
                info.Size = _estimatedItemHeight;
            offset += info.Size + spacing;
        }
    }

    void CalculateGridPositions(Rect rect, int span, nfloat hSpacing, nfloat vSpacing, bool isHorizontal)
    {
        if (isHorizontal)
        {
            var rowHeight = ((nfloat)rect.Height - vSpacing * (span - 1)) / span;
            if (rowHeight < 20) rowHeight = 20;

            nfloat x = 0;
            nfloat maxColWidth = _estimatedItemHeight;
            for (int i = 0; i < _flatItems.Count; i++)
            {
                int row = i % span;
                if (row == 0 && i > 0)
                {
                    x += maxColWidth + hSpacing;
                    maxColWidth = _estimatedItemHeight;
                }
                _flatItems[i].Position = x;
                _flatItems[i].Size = _flatItems[i].Measured ? _flatItems[i].Size : _estimatedItemHeight;
                if (_flatItems[i].Size > maxColWidth) maxColWidth = _flatItems[i].Size;
            }
        }
        else
        {
            var totalHSpacing = hSpacing * (span - 1);
            var colWidth = ((nfloat)rect.Width - totalHSpacing) / span;
            if (colWidth < 20) colWidth = 20;

            var colTops = new nfloat[span];
            for (int i = 0; i < _flatItems.Count; i++)
            {
                int col = i % span;
                _flatItems[i].Position = colTops[col];
                if (!_flatItems[i].Measured)
                    _flatItems[i].Size = _estimatedItemHeight;
                colTops[col] += _flatItems[i].Size + vSpacing;
            }
        }
    }

    void UpdateVisibleItems() => UpdateVisibleItems(null);

    void UpdateVisibleItems(Rect? viewportOverride)
    {
        if (_itemsContainer == null || _documentView == null || !_positionsCalculated || _flatItems.Count == 0)
            return;

        var visibleRect = PlatformView.ContentView.Bounds;
        var layout = (VirtualView as StructuredItemsView)?.ItemsLayout;
        var isHorizontal = GetOrientation(layout) == ItemsLayoutOrientation.Horizontal;
        var span = GetSpan(layout);

        // During initial layout, ContentView.Bounds may not yet reflect the final size.
        // Use the viewport override (from PlatformArrange rect) if it's larger.
        if (viewportOverride is Rect vp)
        {
            if (isHorizontal && vp.Width > visibleRect.Width)
                visibleRect = new CGRect(visibleRect.X, visibleRect.Y, vp.Width, visibleRect.Height);
            else if (!isHorizontal && vp.Height > visibleRect.Height)
                visibleRect = new CGRect(visibleRect.X, visibleRect.Y, visibleRect.Width, vp.Height);
        }

        nfloat viewStart, viewEnd;
        if (isHorizontal)
        {
            viewStart = (nfloat)visibleRect.X - OverScanPixels;
            viewEnd = (nfloat)(visibleRect.X + visibleRect.Width) + OverScanPixels;
        }
        else
        {
            viewStart = (nfloat)visibleRect.Y - OverScanPixels;
            viewEnd = (nfloat)(visibleRect.Y + visibleRect.Height) + OverScanPixels;
        }

        if (viewStart < 0) viewStart = 0;

        // Find which items should be visible
        var shouldBeVisible = new HashSet<int>();
        for (int i = 0; i < _flatItems.Count; i++)
        {
            var info = _flatItems[i];
            var itemEnd = info.Position + info.Size;
            if (itemEnd >= viewStart && info.Position <= viewEnd)
                shouldBeVisible.Add(i);
        }

        // Remove items that are no longer visible
        var toRemove = new List<int>();
        foreach (var kvp in _visibleViews)
        {
            if (!shouldBeVisible.Contains(kvp.Key))
                toRemove.Add(kvp.Key);
        }
        foreach (var idx in toRemove)
        {
            var (mauiView, platformView) = _visibleViews[idx];
            platformView.RemoveFromSuperview();
            if (mauiView is Element elem && VirtualView is Element parent)
                parent.RemoveLogicalChild(elem);
            RecycleView(idx, mauiView);
            _visibleViews.Remove(idx);
        }

        // Add items that should be visible but aren't yet
        var containerWidth = (nfloat)_itemsContainer.Frame.Width;
        var containerHeight = (nfloat)_itemsContainer.Frame.Height;
        var itemSpacing = (nfloat)GetItemSpacing(layout);
        var lineSpacing = (nfloat)GetLineSpacing(layout);
        bool needsRecalc = false;

        foreach (var idx in shouldBeVisible)
        {
            if (_visibleViews.ContainsKey(idx))
                continue;

            var info = _flatItems[idx];
            var (mauiView, platformView) = CreateOrReuseView(idx, info);

            _itemsContainer.AddSubview(platformView);
            _visibleViews[idx] = (mauiView, platformView);
            if (mauiView is Element elem && VirtualView is Element parent)
                parent.AddLogicalChild(elem);

            // Measure and position
            if (!info.Measured)
            {
                var measuredSize = isHorizontal
                    ? MeasureItemWidth(platformView, containerHeight)
                    : MeasureItemHeight(platformView, containerWidth);

                if (Math.Abs(measuredSize - info.Size) > 1)
                {
                    info.Size = measuredSize;
                    info.Measured = true;
                    needsRecalc = true;
                }
                else
                {
                    info.Measured = true;
                }
            }

            PositionItem(platformView, info, idx, isHorizontal, span, containerWidth, containerHeight,
                (nfloat)GetItemSpacing(layout), (nfloat)GetLineSpacing(layout));
        }

        // If any measurements changed, recalculate positions and reposition all visible items,
        // then re-check if more items now fit in the viewport
        if (needsRecalc)
        {
            var rect = new Rect(0, 0, PlatformView.Frame.Width, PlatformView.Frame.Height);
            CalculatePositions(rect);
            foreach (var kvp in _visibleViews)
            {
                PositionItem(kvp.Value.platformView, _flatItems[kvp.Key], kvp.Key,
                    isHorizontal, span, containerWidth, containerHeight,
                    itemSpacing, lineSpacing);
            }
            // Items shrank — more items may now fit in the viewport; re-run once
            UpdateVisibleItems(viewportOverride);
        }
    }

    void PositionItem(NSView platformView, ItemInfo info, int index,
        bool isHorizontal, int span, nfloat containerWidth, nfloat containerHeight,
        nfloat hSpacing, nfloat vSpacing)
    {
        // Header/Footer always span full width/height
        if (info.IsHeader || info.IsFooter)
        {
            if (isHorizontal)
                platformView.Frame = new CGRect(info.Position, 0, info.Size, containerHeight);
            else
                platformView.Frame = new CGRect(0, info.Position, containerWidth, info.Size);
            return;
        }

        if (span > 1)
        {
            if (isHorizontal)
            {
                var rowHeight = (containerHeight - vSpacing * (span - 1)) / span;
                int row = index % span;
                var y = row * (rowHeight + vSpacing);
                platformView.Frame = new CGRect(info.Position, y, info.Size, rowHeight);
            }
            else
            {
                var colWidth = (containerWidth - hSpacing * (span - 1)) / span;
                int col = index % span;
                var x = col * (colWidth + hSpacing);
                platformView.Frame = new CGRect(x, info.Position, colWidth, info.Size);
            }
        }
        else if (isHorizontal)
        {
            platformView.Frame = new CGRect(info.Position, 0, info.Size, containerHeight);
        }
        else
        {
            platformView.Frame = new CGRect(0, info.Position, containerWidth, info.Size);
        }
    }

    (IView mauiView, NSView platformView) CreateOrReuseView(int index, ItemInfo info)
    {
        IView? mauiView = null;
        var template = info.Template;

        // Try to reuse from recycle pool
        if (template != null && _recyclePool.TryGetValue(template, out var pool) && pool.Count > 0)
        {
            mauiView = pool.Dequeue();
            if (mauiView is View v)
                v.BindingContext = info.DataItem;
        }

        if (mauiView == null)
            mauiView = CreateItemView(info.DataItem, info.Template, VirtualView);

        var pView = mauiView!.ToMacOSPlatform(MauiContext!);

        // Add selection gesture if not a header/footer/group header/footer
        if (!info.IsGroupHeader && !info.IsGroupFooter && !info.IsHeader && !info.IsFooter)
            AddSelectionGesture(pView, info.DataItem, index);

        return (mauiView, pView);
    }

    void RecycleView(int index, IView mauiView)
    {
        if (index < 0 || index >= _flatItems.Count)
            return;

        var info = _flatItems[index];
        if (info.Template == null || info.IsGroupHeader || info.IsGroupFooter || info.IsHeader || info.IsFooter)
            return;

        if (!_recyclePool.TryGetValue(info.Template, out var pool))
        {
            pool = new Queue<IView>();
            _recyclePool[info.Template] = pool;
        }

        // Keep a reasonable pool size
        if (pool.Count < 20)
            pool.Enqueue(mauiView);
    }

    #endregion

    #region Layout Helpers

    static nfloat MeasureItemHeight(NSView subview, nfloat width)
    {
        var fittingSize = subview is MacOSContainerView container
            ? container.SizeThatFits(new CGSize(width, nfloat.MaxValue))
            : subview.IntrinsicContentSize;
        var height = fittingSize.Height > 0 && fittingSize.Height < 10000
            ? fittingSize.Height : (nfloat)44;
        return height;
    }

    static nfloat MeasureItemWidth(NSView subview, nfloat height)
    {
        var fittingSize = subview is MacOSContainerView container
            ? container.SizeThatFits(new CGSize(nfloat.MaxValue, height))
            : subview.IntrinsicContentSize;
        var width = fittingSize.Width > 0 && fittingSize.Width < 10000
            ? fittingSize.Width : (nfloat)120;
        return width;
    }

    static ItemsLayoutOrientation GetOrientation(IItemsLayout? layout) => layout switch
    {
        LinearItemsLayout linear => linear.Orientation,
        GridItemsLayout grid => grid.Orientation,
        _ => ItemsLayoutOrientation.Vertical,
    };

    static int GetSpan(IItemsLayout? layout) => layout is GridItemsLayout grid ? grid.Span : 1;

    static double GetItemSpacing(IItemsLayout? layout) => layout switch
    {
        LinearItemsLayout linear => linear.ItemSpacing,
        GridItemsLayout grid => grid.HorizontalItemSpacing,
        _ => 0,
    };

    static double GetLineSpacing(IItemsLayout? layout) => layout switch
    {
        GridItemsLayout grid => grid.VerticalItemSpacing,
        _ => 0,
    };

    #endregion

    #region Property Mappers

    public static void MapItemsSource(CollectionViewHandler handler, CollectionView view)
        => handler.ReloadItems();

    public static void MapItemTemplate(CollectionViewHandler handler, CollectionView view)
        => handler.ReloadItems();

    public static void MapItemsLayout(CollectionViewHandler handler, CollectionView view)
    {
        var layout = view.ItemsLayout;
        var isHorizontal = GetOrientation(layout) == ItemsLayoutOrientation.Horizontal;
        handler.PlatformView.HasVerticalScroller = !isHorizontal;
        handler.PlatformView.HasHorizontalScroller = isHorizontal;
        handler._positionsCalculated = false;

        if (handler.PlatformView.Frame.Width > 0)
        {
            var rect = new Rect(0, 0, handler.PlatformView.Frame.Width, handler.PlatformView.Frame.Height);
            handler.CalculatePositions(rect);
            handler.UpdateVisibleItems();
        }
    }

    public static void MapSelectionMode(CollectionViewHandler handler, CollectionView view)
    {
        // Clear visual selection and reload to re-attach gesture recognizers with new mode
        handler.ClearSelectionVisuals();
        handler.UpdateVisibleItems();
    }

    public static void MapSelectedItem(CollectionViewHandler handler, CollectionView view)
    {
        // If SelectedItem is cleared programmatically, clear visuals
        if (view is SelectableItemsView selectable && selectable.SelectedItem == null)
            handler.ClearSelectionVisuals();
    }
    public static void MapIsGrouped(CollectionViewHandler handler, CollectionView view)
        => handler.ReloadItems();
    public static void MapGroupHeaderTemplate(CollectionViewHandler handler, CollectionView view)
        => handler.ReloadItems();
    public static void MapGroupFooterTemplate(CollectionViewHandler handler, CollectionView view)
        => handler.ReloadItems();

    public static void MapEmptyView(CollectionViewHandler handler, CollectionView view)
    {
        var rect = new Rect(0, 0, handler.PlatformView.Frame.Width, handler.PlatformView.Frame.Height);
        handler.UpdateEmptyView(rect);
    }

    public static void MapHeaderFooter(CollectionViewHandler handler, CollectionView view)
    {
        if (!handler._isReloading)
            handler.UpdateHeaderFooter();
    }

    public static void MapScrollTo(CollectionViewHandler handler, CollectionView view, object? arg)
    {
        if (arg is ScrollToRequestEventArgs scrollArgs)
        {
            handler.HandleScrollTo(scrollArgs);
        }
    }

    #endregion

    #region EmptyView

    void UpdateEmptyView(Rect rect)
    {
        var hasItems = _flatItems.Count > 0;

        if (hasItems)
        {
            RemoveEmptyView();
            return;
        }

        // Show empty view
        if (_emptyView != null)
            return; // already showing

        var emptyView = VirtualView?.EmptyView;
        var emptyTemplate = VirtualView?.EmptyViewTemplate;

        IView? mauiView = null;
        if (emptyTemplate != null)
        {
            var content = emptyTemplate.CreateContent();
            if (content is View v)
            {
                v.BindingContext = emptyView;
                mauiView = v;
            }
        }
        else if (emptyView is IView ev)
        {
            mauiView = ev;
        }
        else if (emptyView is string text)
        {
            mauiView = new Label
            {
                Text = text,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                TextColor = Colors.Gray,
                FontSize = 16,
            };
        }

        if (mauiView != null && MauiContext != null)
        {
            _emptyMauiView = mauiView;
            if (mauiView is Element elem && VirtualView is Element parent)
                parent.AddLogicalChild(elem);
            _emptyView = mauiView.ToMacOSPlatform(MauiContext);
            _documentView?.AddSubview(_emptyView);
            LayoutEmptyView(rect);
        }
    }

    void RemoveEmptyView()
    {
        if (_emptyView != null)
        {
            _emptyView.RemoveFromSuperview();
            if (_emptyMauiView is Element elem && VirtualView is Element parent)
                parent.RemoveLogicalChild(elem);
            _emptyView = null;
            _emptyMauiView = null;
        }
    }

    void LayoutEmptyView(Rect rect)
    {
        if (_emptyView == null || _documentView == null)
            return;

        var width = rect.Width > 0 ? rect.Width : PlatformView.Bounds.Width;
        var height = rect.Height > 0 ? rect.Height : PlatformView.Bounds.Height;

        _emptyView.Frame = new CGRect(0, 0, width, height);
        _documentView.Frame = new CGRect(0, 0, width, height);

        if (_emptyMauiView != null)
        {
            _emptyMauiView.Measure(width, height);
            _emptyMauiView.Arrange(new Rect(0, 0, width, height));
        }
    }

    #endregion

    #region Header/Footer

    void UpdateHeaderFooter()
        => ReloadItems();

    static DataTemplate? GetHeaderFooterTemplate(object? content, DataTemplate? template)
    {
        if (template != null)
            return template;

        if (content is View view)
        {
            // Wrap in a ContentView to avoid parent conflicts
            return new DataTemplate(() =>
            {
                var wrapper = new ContentView { Content = view };
                return wrapper;
            });
        }

        if (content is string text)
            return new DataTemplate(() => new Label { Text = text, FontAttributes = FontAttributes.Bold, Margin = new Thickness(8) });

        if (content != null)
            return new DataTemplate(() => new Label { Text = content.ToString(), Margin = new Thickness(8) });

        return null;
    }

    #endregion

    #region ScrollTo

    void HandleScrollTo(ScrollToRequestEventArgs args)
    {
        if (_documentView == null || _flatItems.Count == 0)
            return;

        int targetIndex = -1;

        if (args.Mode == ScrollToMode.Position)
        {
            targetIndex = args.Index;
        }
        else if (args.Mode == ScrollToMode.Element && args.Item != null)
        {
            for (int i = 0; i < _flatItems.Count; i++)
            {
                if (Equals(_flatItems[i].DataItem, args.Item))
                {
                    targetIndex = i;
                    break;
                }
            }
        }

        if (targetIndex < 0 || targetIndex >= _flatItems.Count)
            return;

        var info = _flatItems[targetIndex];
        var layout = (VirtualView as StructuredItemsView)?.ItemsLayout;
        var isHorizontal = GetOrientation(layout) == ItemsLayoutOrientation.Horizontal;

        var scrollPosition = info.Position;

        // Adjust based on ScrollToPosition
        var visibleRect = PlatformView.ContentView.Bounds;
        if (isHorizontal)
        {
            var targetX = args.ScrollToPosition switch
            {
                ScrollToPosition.Start => scrollPosition,
                ScrollToPosition.Center => scrollPosition - ((nfloat)visibleRect.Width - info.Size) / 2,
                ScrollToPosition.End => scrollPosition - (nfloat)visibleRect.Width + info.Size,
                ScrollToPosition.MakeVisible when scrollPosition < (nfloat)visibleRect.X => scrollPosition,
                ScrollToPosition.MakeVisible when scrollPosition + info.Size > (nfloat)(visibleRect.X + visibleRect.Width) =>
                    scrollPosition - (nfloat)visibleRect.Width + info.Size,
                _ => (nfloat)visibleRect.X,
            };
            var point = new CGPoint(Math.Max(0, (double)targetX), visibleRect.Y);
            ScrollToPoint(point, args.IsAnimated);
        }
        else
        {
            var targetY = args.ScrollToPosition switch
            {
                ScrollToPosition.Start => scrollPosition,
                ScrollToPosition.Center => scrollPosition - ((nfloat)visibleRect.Height - info.Size) / 2,
                ScrollToPosition.End => scrollPosition - (nfloat)visibleRect.Height + info.Size,
                ScrollToPosition.MakeVisible when scrollPosition < (nfloat)visibleRect.Y => scrollPosition,
                ScrollToPosition.MakeVisible when scrollPosition + info.Size > (nfloat)(visibleRect.Y + visibleRect.Height) =>
                    scrollPosition - (nfloat)visibleRect.Height + info.Size,
                _ => (nfloat)visibleRect.Y,
            };
            var point = new CGPoint(visibleRect.X, Math.Max(0, (double)targetY));
            ScrollToPoint(point, args.IsAnimated);
        }

        PlatformView.ReflectScrolledClipView(PlatformView.ContentView);
        UpdateVisibleItems();
    }

    void ScrollToPoint(CGPoint point, bool animated)
    {
        if (animated)
        {
            NSAnimationContext.BeginGrouping();
            NSAnimationContext.CurrentContext.Duration = 0.3;
            NSAnimationContext.CurrentContext.AllowsImplicitAnimation = true;
            PlatformView.ContentView.ScrollToPoint(point);
            NSAnimationContext.EndGrouping();
        }
        else
        {
            PlatformView.ContentView.ScrollToPoint(point);
        }
    }

    #endregion

    #region RemainingItemsThreshold

    void CheckRemainingItemsThreshold()
    {
        if (VirtualView is not ItemsView itemsView)
            return;

        var threshold = itemsView.RemainingItemsThreshold;
        if (threshold < 0) return;

        var layout = (VirtualView as StructuredItemsView)?.ItemsLayout;
        var isHorizontal = GetOrientation(layout) == ItemsLayoutOrientation.Horizontal;
        var visibleRect = PlatformView.ContentView.Bounds;

        // Find the last visible item index
        int lastVisibleIndex = -1;
        foreach (var kvp in _visibleViews)
        {
            if (kvp.Key > lastVisibleIndex)
                lastVisibleIndex = kvp.Key;
        }

        if (lastVisibleIndex < 0) return;

        var remainingItems = _flatItems.Count - lastVisibleIndex - 1;
        if (remainingItems <= threshold && !_remainingThresholdFired)
        {
            _remainingThresholdFired = true;
            itemsView.SendRemainingItemsThresholdReached();
        }
        else if (remainingItems > threshold)
        {
            _remainingThresholdFired = false;
        }
    }

    #endregion

    #region Data Loading

    void UnsubscribeCollection()
    {
        if (_observableSource != null)
        {
            _observableSource.CollectionChanged -= OnCollectionChanged;
            _observableSource = null;
        }
    }

    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => ReloadItems();

    void ReloadItems()
    {
        if (_itemsContainer == null || MauiContext == null || _isReloading)
            return;

        _isReloading = true;
        try
        {
            ReloadItemsCore();
        }
        finally
        {
            _isReloading = false;
        }
    }

    void ReloadItemsCore()
    {

        UnsubscribeCollection();

        // Clear all visible views
        foreach (var kvp in _visibleViews)
        {
            kvp.Value.platformView.RemoveFromSuperview();
            if (kvp.Value.mauiView is Element elem && VirtualView is Element parent)
                parent.RemoveLogicalChild(elem);
        }
        _visibleViews.Clear();
        _recyclePool.Clear();
        _flatItems.Clear();
        _positionsCalculated = false;
        _remainingThresholdFired = false;
        RemoveEmptyView();

        var itemsSource = VirtualView?.ItemsSource;
        if (itemsSource == null)
        {
            var rect = new Rect(0, 0, PlatformView.Frame.Width, PlatformView.Frame.Height);
            UpdateEmptyView(rect);
            return;
        }

        if (itemsSource is INotifyCollectionChanged observable)
        {
            _observableSource = observable;
            _observableSource.CollectionChanged += OnCollectionChanged;
        }

        var isGrouped = (VirtualView as GroupableItemsView)?.IsGrouped ?? false;
        var template = VirtualView?.ItemTemplate;
        var groupHeaderTemplate = (VirtualView as GroupableItemsView)?.GroupHeaderTemplate;
        var groupFooterTemplate = (VirtualView as GroupableItemsView)?.GroupFooterTemplate;

        if (isGrouped)
        {
            foreach (var group in itemsSource)
            {
                if (groupHeaderTemplate != null)
                {
                    _flatItems.Add(new ItemInfo
                    {
                        DataItem = group,
                        Template = groupHeaderTemplate,
                        IsGroupHeader = true,
                        Size = _estimatedItemHeight,
                    });
                }

                if (group is IEnumerable groupItems)
                {
                    foreach (var item in groupItems)
                    {
                        var resolvedTemplate = template is DataTemplateSelector sel
                            ? sel.SelectTemplate(item, VirtualView!) : template;
                        _flatItems.Add(new ItemInfo
                        {
                            DataItem = item,
                            Template = resolvedTemplate,
                            Size = _estimatedItemHeight,
                        });
                    }
                }

                if (groupFooterTemplate != null)
                {
                    _flatItems.Add(new ItemInfo
                    {
                        DataItem = group,
                        Template = groupFooterTemplate,
                        IsGroupFooter = true,
                        Size = _estimatedItemHeight,
                    });
                }
            }
        }
        else
        {
            foreach (var item in itemsSource)
            {
                var resolvedTemplate = template is DataTemplateSelector sel
                    ? sel.SelectTemplate(item, VirtualView!) : template;
                _flatItems.Add(new ItemInfo
                {
                    DataItem = item,
                    Template = resolvedTemplate,
                    Size = _estimatedItemHeight,
                });
            }
        }

        // Add header/footer as items in the flat list
        var structured = VirtualView as StructuredItemsView;
        if (structured != null)
        {
            var headerTemplate = GetHeaderFooterTemplate(structured.Header, structured.HeaderTemplate);
            if (headerTemplate != null)
            {
                _flatItems.Insert(0, new ItemInfo
                {
                    DataItem = structured.Header ?? "Header",
                    Template = headerTemplate,
                    IsHeader = true,
                    Size = _estimatedItemHeight,
                });
            }

            var footerTemplate = GetHeaderFooterTemplate(structured.Footer, structured.FooterTemplate);
            if (footerTemplate != null)
            {
                _flatItems.Add(new ItemInfo
                {
                    DataItem = structured.Footer ?? "Footer",
                    Template = footerTemplate,
                    IsFooter = true,
                    Size = _estimatedItemHeight,
                });
            }
        }

        if (PlatformView.Frame.Width > 0)
        {
            var rect = new Rect(0, 0, PlatformView.Frame.Width, PlatformView.Frame.Height);
            CalculatePositions(rect);
            UpdateVisibleItems();
        }
    }

    void AddSelectionGesture(NSView platformView, object item, int flatIndex)
    {
        // Remove only our own selection click recognizers to avoid duplicates.
        // Do NOT remove MacOSTapGestureRecognizer instances — those are MAUI
        // TapGestureRecognizers from the DataTemplate and must be preserved.
        if (platformView.GestureRecognizers is { Length: > 0 })
        {
            foreach (var g in platformView.GestureRecognizers.OfType<NSClickGestureRecognizer>().ToArray())
            {
                if (g is not MacOSTapGestureRecognizer)
                    platformView.RemoveGestureRecognizer(g);
            }
        }

        var clickRecognizer = new NSClickGestureRecognizer(() =>
        {
            // Read current mode at tap time, not at setup time
            var currentMode = (VirtualView as SelectableItemsView)?.SelectionMode ?? SelectionMode.None;
            if (currentMode == SelectionMode.None || VirtualView is not SelectableItemsView selectable)
                return;

            if (currentMode == SelectionMode.Single)
            {
                var previousIndices = _selectedIndices.ToList();
                _selectedIndices.Clear();
                foreach (var prev in previousIndices)
                    UpdateSelectionVisual(prev, false);

                _selectedIndices.Add(flatIndex);
                selectable.SelectedItem = item;
                UpdateSelectionVisual(flatIndex, true);
            }
            else if (currentMode == SelectionMode.Multiple)
            {
                if (_selectedIndices.Contains(flatIndex))
                {
                    _selectedIndices.Remove(flatIndex);
                    UpdateSelectionVisual(flatIndex, false);
                }
                else
                {
                    _selectedIndices.Add(flatIndex);
                    UpdateSelectionVisual(flatIndex, true);
                }

                var selectedItems = new List<object>();
                foreach (var idx in _selectedIndices)
                {
                    if (idx >= 0 && idx < _flatItems.Count)
                        selectedItems.Add(_flatItems[idx].DataItem);
                }
                selectable.SelectedItems = selectedItems;
            }
        });

        // Allow simultaneous recognition only with MacOSTapGestureRecognizer
        // so MAUI TapGestureRecognizers on the same view also fire.
        clickRecognizer.ShouldRecognizeSimultaneously = (self, other) =>
            other is MacOSTapGestureRecognizer;

        platformView.AddGestureRecognizer(clickRecognizer);

        // Apply initial selection visual if already selected
        if (_selectedIndices.Contains(flatIndex))
            UpdateSelectionVisual(flatIndex, true);
    }

    void UpdateSelectionVisual(int flatIndex, bool selected)
    {
        if (!_visibleViews.TryGetValue(flatIndex, out var entry))
            return;

        var view = entry.platformView;
        if (!view.WantsLayer)
            view.WantsLayer = true;

        if (selected)
        {
            view.Layer!.BackgroundColor = NSColor.SelectedContentBackground.ColorWithAlphaComponent(0.2f).CGColor;
        }
        else
        {
            view.Layer!.BackgroundColor = null;
        }
    }

    void ClearSelectionVisuals()
    {
        foreach (var idx in _selectedIndices.ToList())
            UpdateSelectionVisual(idx, false);
        _selectedIndices.Clear();
    }

    static IView? CreateItemView(object item, DataTemplate? template, CollectionView? collectionView)
    {
        if (template != null)
        {
            var content = template.CreateContent();
            if (content is View view)
            {
                view.BindingContext = item;
                return view;
            }
        }

        return new Label { Text = item?.ToString() ?? string.Empty };
    }

    #endregion
}
