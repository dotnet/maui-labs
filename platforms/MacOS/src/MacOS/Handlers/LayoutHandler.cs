using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class LayoutHandler : MacOSViewHandler<ILayout, MacOSContainerView>
{
    public static readonly IPropertyMapper<ILayout, LayoutHandler> Mapper =
        new PropertyMapper<ILayout, LayoutHandler>(ViewMapper)
        {
            [nameof(IView.Background)] = MapBackground,
            [nameof(ILayout.ClipsToBounds)] = MapClipsToBounds,
        };

    public LayoutHandler() : base(Mapper)
    {
    }

    protected override MacOSContainerView CreatePlatformView()
    {
        var view = new MacOSContainerView();
        view.CrossPlatformMeasure = VirtualViewCrossPlatformMeasure;
        view.CrossPlatformArrange = VirtualViewCrossPlatformArrange;
        return view;
    }

    protected override void ConnectHandler(MacOSContainerView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.CrossPlatformMeasure = VirtualViewCrossPlatformMeasure;
        platformView.CrossPlatformArrange = VirtualViewCrossPlatformArrange;
        platformView.View = VirtualView;

        // Add existing children
        foreach (var child in VirtualView)
        {
            if (MauiContext != null)
            {
                var childView = child.ToMacOSPlatform(MauiContext);
                platformView.AddSubview(childView);
            }
        }
    }

    protected override void DisconnectHandler(MacOSContainerView platformView)
    {
        platformView.CrossPlatformMeasure = null;
        platformView.CrossPlatformArrange = null;
        platformView.View = null;
        base.DisconnectHandler(platformView);
    }

    Graphics.Size VirtualViewCrossPlatformMeasure(double widthConstraint, double heightConstraint)
    {
        return VirtualView?.CrossPlatformMeasure(widthConstraint, heightConstraint) ?? Graphics.Size.Zero;
    }

    Graphics.Size VirtualViewCrossPlatformArrange(Graphics.Rect bounds)
    {
        return VirtualView?.CrossPlatformArrange(bounds) ?? Graphics.Size.Zero;
    }

    public void Add(IView child)
    {
        if (MauiContext == null)
            return;

        var platformView = child.ToMacOSPlatform(MauiContext);
        PlatformView.AddSubview(platformView);
        PlatformView.InvalidateIntrinsicContentSize();
        PlatformView.NeedsLayout = true;
    }

    public void Remove(IView child)
    {
        if (child.Handler?.PlatformView is NSView platformView)
            platformView.RemoveFromSuperview();
        PlatformView.InvalidateIntrinsicContentSize();
        PlatformView.NeedsLayout = true;
    }

    public void Clear()
    {
        foreach (var subview in PlatformView.Subviews)
            subview.RemoveFromSuperview();
        PlatformView.InvalidateIntrinsicContentSize();
        PlatformView.NeedsLayout = true;
    }

    public void Insert(int index, IView child)
    {
        if (MauiContext == null)
            return;

        var platformView = child.ToMacOSPlatform(MauiContext);

        // NSView doesn't have InsertSubview(view, index) — use positioned insertion
        if (index < PlatformView.Subviews.Length)
        {
            var referenceView = PlatformView.Subviews[index];
            PlatformView.AddSubview(platformView, NSWindowOrderingMode.Below, referenceView);
        }
        else
        {
            PlatformView.AddSubview(platformView);
        }
        PlatformView.InvalidateIntrinsicContentSize();
        PlatformView.NeedsLayout = true;
    }

    public void Update(int index, IView child)
    {
        if (MauiContext == null)
            return;

        // Remove existing at index
        if (index < PlatformView.Subviews.Length)
            PlatformView.Subviews[index].RemoveFromSuperview();

        var platformView = child.ToMacOSPlatform(MauiContext);

        if (index < PlatformView.Subviews.Length)
        {
            var referenceView = PlatformView.Subviews[index];
            PlatformView.AddSubview(platformView, NSWindowOrderingMode.Below, referenceView);
        }
        else
        {
            PlatformView.AddSubview(platformView);
        }
        PlatformView.InvalidateIntrinsicContentSize();
        PlatformView.NeedsLayout = true;
    }

    public static void MapBackground(LayoutHandler handler, ILayout layout)
    {
        if (handler.PlatformView.Layer == null)
            return;

        if (layout.Background is Graphics.SolidPaint solidPaint && solidPaint.Color != null)
            handler.PlatformView.Layer.BackgroundColor = solidPaint.Color.ToPlatformColor().CGColor;
    }

    public static void MapClipsToBounds(LayoutHandler handler, ILayout layout)
    {
        if (handler.PlatformView.Layer != null)
            handler.PlatformView.Layer.MasksToBounds = layout.ClipsToBounds;
    }
}
