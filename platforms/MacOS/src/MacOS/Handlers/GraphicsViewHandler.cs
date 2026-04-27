using CoreGraphics;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;
using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// NSView subclass that hosts an IDrawable and draws via CoreGraphics using DirectRenderer.
/// </summary>
public class MacOSGraphicsView : NSView
{
    readonly DirectRenderer _renderer;

    public MacOSGraphicsView()
    {
        WantsLayer = true;
        _renderer = new DirectRenderer();
    }

    public override bool IsFlipped => true;

    public IDrawable? Drawable
    {
        get => _renderer.Drawable;
        set
        {
            _renderer.Drawable = value;
            NeedsDisplay = true;
        }
    }

    public void Invalidate()
    {
        NeedsDisplay = true;
    }

    public override void DrawRect(CGRect dirtyRect)
    {
        base.DrawRect(dirtyRect);

        var context = NSGraphicsContext.CurrentContext?.CGContext;
        if (context == null)
            return;

        // Reset clip to full bounds — AppKit may clip to dirtyRect which
        // can be a partial region, causing drawables to be clipped.
        var bounds = Bounds;
        context.SaveState();
        context.ClipToRect(bounds);

        var rect = new RectF(0, 0, (float)bounds.Width, (float)bounds.Height);
        _renderer.Draw(context, rect);

        context.RestoreState();
    }

    public override void SetFrameSize(CGSize newSize)
    {
        base.SetFrameSize(newSize);
        _renderer.SizeChanged((float)newSize.Width, (float)newSize.Height);
        NeedsDisplay = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderer.Detached();
            _renderer.Dispose();
        }
        base.Dispose(disposing);
    }
}

public partial class GraphicsViewHandler : MacOSViewHandler<IGraphicsView, MacOSGraphicsView>
{
    public static readonly IPropertyMapper<IGraphicsView, GraphicsViewHandler> Mapper =
        new PropertyMapper<IGraphicsView, GraphicsViewHandler>(ViewMapper)
        {
            [nameof(IGraphicsView.Drawable)] = MapDrawable,
        };

    public static readonly CommandMapper<IGraphicsView, GraphicsViewHandler> CommandMapper =
        new(ViewCommandMapper)
        {
            [nameof(IGraphicsView.Invalidate)] = MapInvalidate,
        };

    public GraphicsViewHandler() : base(Mapper, CommandMapper)
    {
    }

    protected override MacOSGraphicsView CreatePlatformView()
    {
        return new MacOSGraphicsView();
    }

    protected override void ConnectHandler(MacOSGraphicsView platformView)
    {
        base.ConnectHandler(platformView);
    }

    public static void MapDrawable(GraphicsViewHandler handler, IGraphicsView graphicsView)
    {
        handler.PlatformView.Drawable = graphicsView.Drawable;
    }

    public static void MapInvalidate(GraphicsViewHandler handler, IGraphicsView graphicsView, object? args)
    {
        handler.PlatformView.Invalidate();
    }
}
