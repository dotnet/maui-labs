using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class ContentViewHandler : MacOSViewHandler<IContentView, MacOSContainerView>
{
    public static readonly IPropertyMapper<IContentView, ContentViewHandler> Mapper =
        new PropertyMapper<IContentView, ContentViewHandler>(ViewMapper)
        {
            [nameof(IContentView.Content)] = MapContent,
            [nameof(IView.Background)] = MapBackground,
        };

    public ContentViewHandler() : base(Mapper)
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
    }

    protected override void DisconnectHandler(MacOSContainerView platformView)
    {
        platformView.CrossPlatformMeasure = null;
        platformView.CrossPlatformArrange = null;
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

    public static void MapContent(ContentViewHandler handler, IContentView contentView)
    {
        if (handler.PlatformView == null || handler.MauiContext == null)
            return;

        // Clear existing content
        foreach (var subview in handler.PlatformView.Subviews)
            subview.RemoveFromSuperview();

        if (contentView.PresentedContent is IView view)
        {
            var platformView = view.ToMacOSPlatform(handler.MauiContext);
            handler.PlatformView.AddSubview(platformView);
        }

        // New content needs measurement and layout
        handler.PlatformView.InvalidateIntrinsicContentSize();
        handler.PlatformView.NeedsLayout = true;
    }

    public static void MapBackground(ContentViewHandler handler, IContentView contentView)
    {
        if (handler.PlatformView.Layer == null)
            return;

        if (contentView.Background is Graphics.SolidPaint solidPaint && solidPaint.Color != null)
            handler.PlatformView.Layer.BackgroundColor = solidPaint.Color.ToPlatformColor().CGColor;
    }
}
