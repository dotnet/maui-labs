using System.Linq;
using CoreAnimation;
using CoreGraphics;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public class BorderNSView : NSView
{
    public BorderNSView()
    {
        WantsLayer = true;
    }

    public override bool IsFlipped => true;

    public Func<double, double, Size>? CrossPlatformMeasure { get; set; }
    public Func<Rect, Size>? CrossPlatformArrange { get; set; }

    public CGSize SizeThatFits(CGSize size)
    {
        if (CrossPlatformMeasure is not null)
        {
            var result = CrossPlatformMeasure((double)size.Width, (double)size.Height);
            return new CGSize(result.Width, result.Height);
        }
        return new CGSize(size.Width, size.Height);
    }

    public override void Layout()
    {
        base.Layout();

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        try
        {
            CrossPlatformMeasure?.Invoke((double)bounds.Width, (double)bounds.Height);
            CrossPlatformArrange?.Invoke(new Rect(0, 0, (double)bounds.Width, (double)bounds.Height));
        }
        catch (Exception ex)
        {
            // AppKit retries Layout() infinitely when exceptions propagate
            Console.Error.WriteLine($"[BorderNSView.Layout] {ex}");
        }
    }
}

public partial class BorderHandler : MacOSViewHandler<IBorderView, BorderNSView>
{
    CAShapeLayer? _borderLayer;
    Size _lastSize;

    public static readonly IPropertyMapper<IBorderView, BorderHandler> Mapper =
        new PropertyMapper<IBorderView, BorderHandler>(ViewMapper)
        {
            [nameof(IBorderView.Content)] = MapContent,
            [nameof(IBorderView.Background)] = MapBackground,
            [nameof(IBorderView.Shape)] = MapStrokeShape,
            [nameof(IBorderView.Stroke)] = MapStroke,
            [nameof(IBorderView.StrokeThickness)] = MapStrokeThickness,
            [nameof(IBorderView.StrokeLineCap)] = MapStrokeLineCap,
            [nameof(IBorderView.StrokeLineJoin)] = MapStrokeLineJoin,
            [nameof(IBorderView.StrokeDashPattern)] = MapStrokeDashPattern,
            [nameof(IBorderView.StrokeDashOffset)] = MapStrokeDashOffset,
            [nameof(IBorderView.StrokeMiterLimit)] = MapStrokeMiterLimit,
        };

    public BorderHandler() : base(Mapper) { }

    protected override BorderNSView CreatePlatformView()
    {
        var view = new BorderNSView
        {
            CrossPlatformMeasure = VirtualViewMeasure,
            CrossPlatformArrange = VirtualViewArrange
        };
        return view;
    }

    protected override void ConnectHandler(BorderNSView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.CrossPlatformMeasure = VirtualViewMeasure;
        platformView.CrossPlatformArrange = VirtualViewArrange;
    }

    protected override void DisconnectHandler(BorderNSView platformView)
    {
        platformView.CrossPlatformMeasure = null;
        platformView.CrossPlatformArrange = null;
        _borderLayer?.RemoveFromSuperLayer();
        _borderLayer = null;
        base.DisconnectHandler(platformView);
    }

    Size VirtualViewMeasure(double w, double h) =>
        VirtualView?.CrossPlatformMeasure(w, h) ?? Size.Zero;

    Size VirtualViewArrange(Rect bounds) =>
        VirtualView?.CrossPlatformArrange(bounds) ?? Size.Zero;

    public override void PlatformArrange(Rect rect)
    {
        base.PlatformArrange(rect);
        if (_lastSize != rect.Size)
        {
            _lastSize = rect.Size;
            UpdateBorder();
        }
    }

    void UpdateBorder()
    {
        if (VirtualView is null || PlatformView is null)
            return;

        var bounds = PlatformView.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var shape = VirtualView.Shape;
        if (shape is null)
        {
            _borderLayer?.RemoveFromSuperLayer();
            _borderLayer = null;
            PlatformView.Layer!.Mask = null;
            return;
        }

        var pathBounds = new Rect(0, 0, (double)bounds.Width, (double)bounds.Height);
        var pathF = shape.PathForBounds(pathBounds);
        if (pathF is null)
            return;

        var cgPath = PathFToCGPath(pathF);

        // Clip content to shape
        var maskLayer = new CAShapeLayer { Path = cgPath, Frame = bounds };
        PlatformView.Layer!.Mask = maskLayer;

        // Draw border stroke
        if (_borderLayer is null)
        {
            _borderLayer = new CAShapeLayer();
            PlatformView.Layer.AddSublayer(_borderLayer);
        }

        _borderLayer.Frame = bounds;
        _borderLayer.Path = cgPath;
        _borderLayer.FillColor = null;

        // Stroke color
        if (VirtualView.Stroke is SolidPaint solidStroke && solidStroke.Color is not null)
            _borderLayer.StrokeColor = solidStroke.Color.ToPlatformColor().CGColor;
        else
            _borderLayer.StrokeColor = NSColor.Clear.CGColor;

        // Stroke thickness
        _borderLayer.LineWidth = (nfloat)VirtualView.StrokeThickness;

        // Line cap
        _borderLayer.LineCap = VirtualView.StrokeLineCap switch
        {
            LineCap.Round => CAShapeLayer.CapRound,
            LineCap.Square => CAShapeLayer.CapSquare,
            _ => CAShapeLayer.CapButt,
        };

        // Line join
        _borderLayer.LineJoin = VirtualView.StrokeLineJoin switch
        {
            LineJoin.Round => CAShapeLayer.JoinRound,
            LineJoin.Bevel => CAShapeLayer.JoinBevel,
            _ => CAShapeLayer.JoinMiter,
        };

        // Miter limit
        _borderLayer.MiterLimit = (nfloat)VirtualView.StrokeMiterLimit;

        // Dash pattern
        if (VirtualView.StrokeDashPattern is { Length: > 0 } pattern)
        {
            _borderLayer.LineDashPattern = pattern.Select(v => new Foundation.NSNumber(v)).ToArray();
            _borderLayer.LineDashPhase = (nfloat)VirtualView.StrokeDashOffset;
        }
        else
        {
            _borderLayer.LineDashPattern = null;
            _borderLayer.LineDashPhase = 0;
        }
    }

    public static void MapContent(BorderHandler handler, IBorderView border)
    {
        if (handler.PlatformView is null || handler.MauiContext is null)
            return;

        foreach (var subview in handler.PlatformView.Subviews)
            subview.RemoveFromSuperview();

        if (border.PresentedContent is IView content)
        {
            var platformView = content.ToMacOSPlatform(handler.MauiContext);
            handler.PlatformView.AddSubview(platformView);
        }

        handler.PlatformView.InvalidateIntrinsicContentSize();
        handler.PlatformView.NeedsLayout = true;
    }

    public static void MapBackground(BorderHandler handler, IBorderView border)
    {
        if (handler.PlatformView?.Layer is null)
            return;

        if (border.Background is SolidPaint solidPaint && solidPaint.Color is not null)
            handler.PlatformView.Layer.BackgroundColor = solidPaint.Color.ToPlatformColor().CGColor;
        else
            handler.PlatformView.Layer.BackgroundColor = NSColor.Clear.CGColor;
    }

    public static void MapStrokeShape(BorderHandler handler, IBorderView border) => handler.UpdateBorder();
    public static void MapStroke(BorderHandler handler, IBorderView border) => handler.UpdateBorder();
    public static void MapStrokeThickness(BorderHandler handler, IBorderView border) => handler.UpdateBorder();
    public static void MapStrokeLineCap(BorderHandler handler, IBorderView border) => handler.UpdateBorder();
    public static void MapStrokeLineJoin(BorderHandler handler, IBorderView border) => handler.UpdateBorder();
    public static void MapStrokeDashPattern(BorderHandler handler, IBorderView border) => handler.UpdateBorder();
    public static void MapStrokeDashOffset(BorderHandler handler, IBorderView border) => handler.UpdateBorder();
    public static void MapStrokeMiterLimit(BorderHandler handler, IBorderView border) => handler.UpdateBorder();

    static CGPath PathFToCGPath(PathF pathF)
    {
        var cgPath = new CGPath();
        var points = pathF.Points?.ToArray();
        var segments = pathF.SegmentTypes?.ToArray();
        if (points is null || segments is null)
            return cgPath;

        int index = 0;
        foreach (var op in segments)
        {
            switch (op)
            {
                case PathOperation.Move:
                    if (index < points.Length) { cgPath.MoveToPoint(points[index].X, points[index].Y); index++; }
                    break;
                case PathOperation.Line:
                    if (index < points.Length) { cgPath.AddLineToPoint(points[index].X, points[index].Y); index++; }
                    break;
                case PathOperation.Quad:
                    if (index + 1 < points.Length) { cgPath.AddQuadCurveToPoint(points[index].X, points[index].Y, points[index + 1].X, points[index + 1].Y); index += 2; }
                    break;
                case PathOperation.Cubic:
                    if (index + 2 < points.Length) { cgPath.AddCurveToPoint(points[index].X, points[index].Y, points[index + 1].X, points[index + 1].Y, points[index + 2].X, points[index + 2].Y); index += 3; }
                    break;
                case PathOperation.Close:
                    cgPath.CloseSubpath();
                    break;
            }
        }
        return cgPath;
    }
}
