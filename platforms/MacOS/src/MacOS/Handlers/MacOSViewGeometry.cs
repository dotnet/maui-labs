using AppKit;
using CoreGraphics;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platforms.MacOS.Platform;
using System.Runtime.CompilerServices;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

internal static class MacOSViewGeometry
{
    static readonly ConditionalWeakTable<NSView, WeakReference<IViewHandler>> Handlers = new();

    internal static void Register(NSView view, IViewHandler handler)
    {
        Handlers.Remove(view);
        Handlers.Add(view, new(handler));
    }

    internal static void Unregister(NSView view, IViewHandler handler)
    {
        if (Handlers.TryGetValue(view, out var reference) &&
            reference.TryGetTarget(out var current) && current == handler)
            Handlers.Remove(view);
    }

    // Native layout owners supply logical rectangles, just like MAUI layout does.
    internal static void SetLayoutFrame(this NSView view, CGRect frame)
    {
        if (view is not MacOSContainerView { ExternalFrameManagement: true } &&
            Handlers.TryGetValue(view, out var reference) && reference.TryGetTarget(out var handler))
            handler.PlatformArrange(new Rect(frame.X, frame.Y, frame.Width, frame.Height));
        else
            view.Frame = frame;
    }
}
