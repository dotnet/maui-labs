using CoreGraphics;
using CoreAnimation;
using Foundation;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// Flipped NSView used as NSScrollView.DocumentView for correct top-left origin.
/// </summary>
internal class FlippedDocumentView : NSView
{
    public FlippedDocumentView()
    {
        WantsLayer = true;
    }

    public override bool IsFlipped => true;

    public override void ViewDidChangeEffectiveAppearance()
    {
        base.ViewDidChangeEffectiveAppearance();
        FlippedNSView.SyncUserAppTheme(EffectiveAppearance);
    }
}

public partial class ScrollViewHandler : MacOSViewHandler<IScrollView, NSScrollView>
{
    public static readonly IPropertyMapper<IScrollView, ScrollViewHandler> Mapper =
        new PropertyMapper<IScrollView, ScrollViewHandler>(ViewMapper)
        {
            [nameof(IScrollView.HorizontalScrollBarVisibility)] = MapHorizontalScrollBarVisibility,
            [nameof(IScrollView.VerticalScrollBarVisibility)] = MapVerticalScrollBarVisibility,
            [nameof(IScrollView.Orientation)] = MapOrientation,
            [nameof(IScrollView.ContentSize)] = MapContentSize,
            [nameof(IContentView.Content)] = MapContent,
        };

    public static readonly CommandMapper<IScrollView, ScrollViewHandler> ScrollCommandMapper =
        new(ViewCommandMapper)
        {
            [nameof(IScrollView.RequestScrollTo)] = MapRequestScrollTo,
        };

    FlippedDocumentView? _documentView;
    NSObject? _boundsChangedObserver;
    bool _arranging;

    public ScrollViewHandler() : base(Mapper, ScrollCommandMapper)
    {
    }

    protected override NSScrollView CreatePlatformView()
    {
        var scrollView = new NSScrollView
        {
            HasVerticalScroller = true,
            HasHorizontalScroller = false,
            AutohidesScrollers = true,
            DrawsBackground = false,
        };

        _documentView = new FlippedDocumentView { AutoresizesSubviews = false };
        scrollView.DocumentView = _documentView;

        return scrollView;
    }

    protected override void ConnectHandler(NSScrollView platformView)
    {
        base.ConnectHandler(platformView);
        _boundsChangedObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            NSView.BoundsChangedNotification,
            OnBoundsChanged,
            platformView.ContentView);
        platformView.ContentView.PostsBoundsChangedNotifications = true;
    }

    protected override void DisconnectHandler(NSScrollView platformView)
    {
        if (_boundsChangedObserver != null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(_boundsChangedObserver);
            _boundsChangedObserver = null;
        }
        base.DisconnectHandler(platformView);
    }

    void OnBoundsChanged(NSNotification notification)
    {
        if (VirtualView == null || PlatformView?.ContentView == null)
            return;

        var origin = PlatformView.ContentView.Bounds.Location;
        if (VirtualView is Microsoft.Maui.Controls.ScrollView scrollView)
            scrollView.SetScrolledPosition(origin.X, origin.Y);
    }

    public static void MapRequestScrollTo(ScrollViewHandler handler, IScrollView scrollView, object? args)
    {
        if (args is ScrollToRequest request && handler.PlatformView?.DocumentView != null)
        {
            var point = new CGPoint(request.HorizontalOffset, request.VerticalOffset);
            if (request.Instant)
            {
                handler.PlatformView.ContentView.ScrollToPoint(point);
                handler.PlatformView.ReflectScrolledClipView(handler.PlatformView.ContentView);
            }
            else
            {
                NSAnimationContext.BeginGrouping();
                NSAnimationContext.CurrentContext.Duration = 0.3;
                ((NSClipView)handler.PlatformView.ContentView.Animator).SetBoundsOrigin(point);
                NSAnimationContext.EndGrouping();
                handler.PlatformView.ReflectScrolledClipView(handler.PlatformView.ContentView);
            }
            scrollView.ScrollFinished();
        }
    }

    /// <summary>
    /// NSScrollView has no useful intrinsic or fitting size, so the base measure reported ~0 and a
    /// ScrollView sized by its parent (an Auto row, a stack, a horizontal ScrollView's height) collapsed.
    /// Measure the content the way the other MAUI backends do - unconstrained along the scrolling axis,
    /// inside the ScrollView's own size requests and padding - then honour the constraints, explicit sizes
    /// and minimum/maximum requests.
    /// </summary>
    public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
    {
        var scrollView = VirtualView;
        if (scrollView == null || PlatformView == null)
            return base.GetDesiredSize(widthConstraint, heightConstraint);

        if (double.IsNaN(widthConstraint))
            widthConstraint = double.PositiveInfinity;
        if (double.IsNaN(heightConstraint))
            heightConstraint = double.PositiveInfinity;

        // The ScrollView's own size requests bound what its content may use (wrapping text in a scroller
        // with WidthRequest=200 must be measured at 200, not at the parent's width) ...
        var padding = scrollView.Padding;
        var outerWidth = ResolveConstraint(widthConstraint, scrollView.Width, scrollView.MaximumWidth);
        var outerHeight = ResolveConstraint(heightConstraint, scrollView.Height, scrollView.MaximumHeight);

        // ... and CrossPlatformMeasure measures only the content, so padding is taken out here and added back.
        var content = scrollView.CrossPlatformMeasure(
            Math.Max(0, outerWidth - padding.HorizontalThickness),
            Math.Max(0, outerHeight - padding.VerticalThickness));
        var measuredWidth = content.Width + padding.HorizontalThickness;
        var measuredHeight = content.Height + padding.VerticalThickness;

        var width = IsExplicitSize(scrollView.Width) ? scrollView.Width : Math.Min(measuredWidth, widthConstraint);
        var height = IsExplicitSize(scrollView.Height) ? scrollView.Height : Math.Min(measuredHeight, heightConstraint);

        if (IsExplicitSize(scrollView.MinimumWidth))
            width = Math.Max(width, scrollView.MinimumWidth);
        if (IsExplicitSize(scrollView.MinimumHeight))
            height = Math.Max(height, scrollView.MinimumHeight);
        if (IsExplicitSize(scrollView.MaximumWidth))
            width = Math.Min(width, scrollView.MaximumWidth);
        if (IsExplicitSize(scrollView.MaximumHeight))
            height = Math.Min(height, scrollView.MaximumHeight);

        // ComputeDesiredSize passes negative constraints when margins exceed the space available.
        return new Size(SanitizeSize(width), SanitizeSize(height));
    }

    static double ResolveConstraint(double constraint, double explicitSize, double maximum)
    {
        if (IsExplicitSize(explicitSize))
            return explicitSize;
        return IsExplicitSize(maximum) ? Math.Min(constraint, maximum) : constraint;
    }

    static double SanitizeSize(double value) => double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? 0 : value;

    static bool IsExplicitSize(double value) => !double.IsNaN(value) && value >= 0 && !double.IsInfinity(value);

    public override void PlatformArrange(Rect rect)
    {
        base.PlatformArrange(rect);

        if (_arranging)
            return;

        _arranging = true;
        try
        {
            if (VirtualView?.PresentedContent is IView content && content.Handler != null && _documentView != null)
            {
                var orientation = VirtualView.Orientation;
                var padding = VirtualView.Padding;
                var viewportWidth = Math.Max(0, rect.Width - padding.HorizontalThickness);
                var viewportHeight = Math.Max(0, rect.Height - padding.VerticalThickness);

                // Measure the content with appropriate constraints based on scroll orientation
                double measureWidth = orientation == ScrollOrientation.Horizontal || orientation == ScrollOrientation.Both
                    ? double.PositiveInfinity
                    : viewportWidth;

                double measureHeight = orientation == ScrollOrientation.Vertical || orientation == ScrollOrientation.Both
                    ? double.PositiveInfinity
                    : viewportHeight;

                var contentSize = content.Measure(measureWidth, measureHeight);

                var arrangeWidth = orientation == ScrollOrientation.Vertical
                    ? viewportWidth
                    : Math.Max(viewportWidth, contentSize.Width);

                var arrangeHeight = orientation == ScrollOrientation.Horizontal
                    ? viewportHeight
                    : Math.Max(viewportHeight, contentSize.Height);

                // Set document view frame first so AppKit knows the scrollable area (content plus padding),
                // then arrange content inside the padding. AutoresizesSubviews=false prevents the
                // document view from overwriting MAUI-managed child frames.
                _documentView.Frame = new CGRect(0, 0,
                    arrangeWidth + padding.HorizontalThickness,
                    arrangeHeight + padding.VerticalThickness);
                content.Arrange(new Rect(padding.Left, padding.Top, arrangeWidth, arrangeHeight));
            }
        }
        finally
        {
            _arranging = false;
        }
    }

    public static void MapContent(ScrollViewHandler handler, IScrollView scrollView)
    {
        if (handler.PlatformView == null || handler.MauiContext == null || handler._documentView == null)
            return;

        // Clear existing content from document view
        foreach (var subview in handler._documentView.Subviews)
            subview.RemoveFromSuperview();

        if (scrollView.PresentedContent is IView content)
        {
            var platformView = content.ToMacOSPlatform(handler.MauiContext);
            handler._documentView.AddSubview(platformView);
        }
    }

    public static void MapHorizontalScrollBarVisibility(ScrollViewHandler handler, IScrollView scrollView)
    {
        handler.PlatformView.HasHorizontalScroller = scrollView.HorizontalScrollBarVisibility != ScrollBarVisibility.Never;
    }

    public static void MapVerticalScrollBarVisibility(ScrollViewHandler handler, IScrollView scrollView)
    {
        handler.PlatformView.HasVerticalScroller = scrollView.VerticalScrollBarVisibility != ScrollBarVisibility.Never;
    }

    public static void MapOrientation(ScrollViewHandler handler, IScrollView scrollView)
    {
        var orientation = scrollView.Orientation;
        handler.PlatformView.HasHorizontalScroller =
            orientation == ScrollOrientation.Horizontal || orientation == ScrollOrientation.Both;
        handler.PlatformView.HasVerticalScroller =
            orientation == ScrollOrientation.Vertical || orientation == ScrollOrientation.Both;
    }

    public static void MapContentSize(ScrollViewHandler handler, IScrollView scrollView)
    {
        if (handler._documentView != null)
        {
            var contentSize = scrollView.ContentSize;
            handler._documentView.Frame = new CGRect(0, 0, contentSize.Width, contentSize.Height);
        }
    }
}
