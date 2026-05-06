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

                // Measure the content with appropriate constraints based on scroll orientation
                double measureWidth = orientation == ScrollOrientation.Horizontal || orientation == ScrollOrientation.Both
                    ? double.PositiveInfinity
                    : rect.Width;

                double measureHeight = orientation == ScrollOrientation.Vertical || orientation == ScrollOrientation.Both
                    ? double.PositiveInfinity
                    : rect.Height;

                var contentSize = content.Measure(measureWidth, measureHeight);

                var arrangeWidth = orientation == ScrollOrientation.Vertical
                    ? rect.Width
                    : Math.Max(rect.Width, contentSize.Width);

                var arrangeHeight = orientation == ScrollOrientation.Horizontal
                    ? rect.Height
                    : Math.Max(rect.Height, contentSize.Height);

                // Set document view frame first so AppKit knows the scrollable area,
                // then arrange content. AutoresizesSubviews=false prevents the
                // document view from overwriting MAUI-managed child frames.
                _documentView.Frame = new CGRect(0, 0, arrangeWidth, arrangeHeight);
                content.Arrange(new Rect(0, 0, arrangeWidth, arrangeHeight));
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
