using AppKit;

using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

public static class ViewExtensions
{
    public static NSView ToMacOSPlatform(this IView view, IMauiContext context)
    {
        var handler = view.ToHandler(context);
        return handler.ToPlatformView();
    }

    public static NSView ToPlatformView(this IElementHandler handler)
    {
        if (handler.PlatformView is NSView nsView)
            return nsView;

        throw new InvalidOperationException(
            $"Unable to convert handler platform view ({handler.PlatformView?.GetType().Name}) to NSView");
    }

    public static IElementHandler ToHandler(this IView view, IMauiContext context)
    {
        var handler = context.Handlers.GetHandler(view.GetType());
        if (handler == null)
            throw new InvalidOperationException($"No handler found for view type {view.GetType().Name}");

        handler.SetMauiContext(context);
        handler.SetVirtualView((IElement)view);
        return handler;
    }
}
