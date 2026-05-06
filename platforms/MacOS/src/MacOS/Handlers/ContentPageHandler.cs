using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class ContentPageHandler : MacOSViewHandler<IContentView, MacOSContainerView>
{
    public static readonly IPropertyMapper<IContentView, ContentPageHandler> Mapper =
        new PropertyMapper<IContentView, ContentPageHandler>(ViewMapper)
        {
            [nameof(IContentView.Content)] = MapContent,
            [nameof(IView.Background)] = MapBackground,
            [nameof(ContentPage.MenuBarItems)] = MapMenuBarItems,
            [nameof(ContentPage.Title)] = MapTitle,
        };

    public ContentPageHandler() : base(Mapper)
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

    public static void MapContent(ContentPageHandler handler, IContentView page)
    {
        if (handler.PlatformView == null || handler.MauiContext == null)
            return;

        foreach (var subview in handler.PlatformView.Subviews)
            subview.RemoveFromSuperview();

        if (page.PresentedContent is IView content)
        {
            var platformView = content.ToMacOSPlatform(handler.MauiContext);
            handler.PlatformView.AddSubview(platformView);
        }
    }

    public static void MapBackground(ContentPageHandler handler, IContentView page)
    {
        if (handler.PlatformView.Layer == null)
            return;

        if (page.Background is Graphics.SolidPaint solidPaint && solidPaint.Color != null)
            handler.PlatformView.Layer.BackgroundColor = solidPaint.Color.ToPlatformColor().CGColor;
        else
            handler.PlatformView.Layer.BackgroundColor = NSColor.Clear.CGColor;
    }

    public static void MapMenuBarItems(ContentPageHandler handler, IContentView page)
    {
        if (page is ContentPage contentPage)
            MenuBarManager.UpdateMenuBar(contentPage.MenuBarItems);
    }

    public static void MapTitle(ContentPageHandler handler, IContentView page)
    {
        if (page is not ITitledElement titled)
            return;

        var window = handler.PlatformView.Window;
        if (window != null && !string.IsNullOrEmpty(titled.Title))
            window.Title = titled.Title;
    }
}
