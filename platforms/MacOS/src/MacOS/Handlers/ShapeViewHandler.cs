using System.Linq;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// NSView subclass for shape rendering that enables layer-backing and flipped coordinates.
/// Overrides mouse event methods to ensure gesture recognizers receive events.
/// </summary>
internal class ShapeNSView : NSView
{
    public ShapeNSView()
    {
        WantsLayer = true;
    }

    public override bool IsFlipped => true;

    public override bool AcceptsFirstResponder() => true;

    public override void MouseDown(NSEvent theEvent) => base.MouseDown(theEvent);
    public override void MouseDragged(NSEvent theEvent) => base.MouseDragged(theEvent);
    public override void MouseUp(NSEvent theEvent) => base.MouseUp(theEvent);

    public override void MouseEntered(NSEvent theEvent)
    {
        foreach (var area in TrackingAreas())
        {
            if (area is MacOSPointerTrackingArea pointerArea)
            {
                var parent = (pointerArea.Recognizer as IElement)?.FindParentOfType<View>();
                pointerArea.FireEntered(parent, theEvent);
            }
        }
        base.MouseEntered(theEvent);
    }

    public override void MouseExited(NSEvent theEvent)
    {
        foreach (var area in TrackingAreas())
        {
            if (area is MacOSPointerTrackingArea pointerArea)
            {
                var parent = (pointerArea.Recognizer as IElement)?.FindParentOfType<View>();
                pointerArea.FireExited(parent, theEvent);
            }
        }
        base.MouseExited(theEvent);
    }

    public override void MouseMoved(NSEvent theEvent)
    {
        foreach (var area in TrackingAreas())
        {
            if (area is MacOSPointerTrackingArea pointerArea)
            {
                var parent = (pointerArea.Recognizer as IElement)?.FindParentOfType<View>();
                pointerArea.FireMoved(parent, theEvent);
            }
        }
        base.MouseMoved(theEvent);
    }
}

public partial class ShapeViewHandler : MacOSViewHandler<IShapeView, NSView>
{
    public static readonly IPropertyMapper<IShapeView, ShapeViewHandler> Mapper =
        new PropertyMapper<IShapeView, ShapeViewHandler>(ViewMapper)
        {
            [nameof(IShapeView.Shape)] = MapShape,
            [nameof(IShapeView.Fill)] = MapFill,
            [nameof(IShapeView.Aspect)] = MapAspect,
            [nameof(IShapeView.Stroke)] = MapStroke,
            [nameof(IShapeView.StrokeThickness)] = MapStrokeThickness,
            [nameof(IShapeView.StrokeDashPattern)] = MapStrokeDashPattern,
            [nameof(IShapeView.StrokeLineCap)] = MapStrokeLineCap,
            [nameof(IShapeView.StrokeLineJoin)] = MapStrokeLineJoin,
        };

    CAShapeLayer? _shapeLayer;

    public ShapeViewHandler() : base(Mapper)
    {
    }

    protected override NSView CreatePlatformView()
    {
        return new ShapeNSView();
    }

    public static void MapFill(ShapeViewHandler handler, IShapeView shapeView)
    {
        if (handler._shapeLayer != null)
        {
            if (shapeView.Fill is SolidPaint solidPaint && solidPaint.Color != null)
                handler._shapeLayer.FillColor = solidPaint.Color.ToPlatformColor().CGColor;
            else
                handler._shapeLayer.FillColor = NSColor.Clear.CGColor;
        }
        else
        {
            if (shapeView.Fill is SolidPaint solidPaint && solidPaint.Color != null)
            {
                handler.PlatformView.WantsLayer = true;
                handler.PlatformView.Layer!.BackgroundColor = solidPaint.Color.ToPlatformColor().CGColor;
            }
            else
            {
                handler.PlatformView.WantsLayer = true;
                handler.PlatformView.Layer!.BackgroundColor = NSColor.Clear.CGColor;
            }
        }
    }

    public static void MapShape(ShapeViewHandler handler, IShapeView shapeView)
    {
        handler.UpdateShape(shapeView);
    }

    public static void MapAspect(ShapeViewHandler handler, IShapeView shapeView)
    {
        handler.UpdateShape(shapeView);
    }

    void UpdateShape(IShapeView shapeView)
    {
        var bounds = PlatformView.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var shape = shapeView.Shape;
        if (shape == null)
        {
            _shapeLayer?.RemoveFromSuperLayer();
            _shapeLayer = null;
            return;
        }

        var pathBounds = new Graphics.Rect(0, 0, (double)bounds.Width, (double)bounds.Height);
        var pathF = shape.PathForBounds(pathBounds);
        if (pathF == null)
            return;

        if (_shapeLayer == null)
        {
            _shapeLayer = new CAShapeLayer();
            PlatformView.WantsLayer = true;
            PlatformView.Layer!.AddSublayer(_shapeLayer);
            PlatformView.Layer.BackgroundColor = NSColor.Clear.CGColor;
        }

        _shapeLayer.Frame = bounds;
        _shapeLayer.Path = PathFToCGPath(pathF);

        if (shapeView.Fill is SolidPaint solidPaint && solidPaint.Color != null)
            _shapeLayer.FillColor = solidPaint.Color.ToPlatformColor().CGColor;
        else
            _shapeLayer.FillColor = NSColor.Clear.CGColor;

        // Apply stroke
        if (shapeView.Stroke is SolidPaint strokePaint && strokePaint.Color != null)
            _shapeLayer.StrokeColor = strokePaint.Color.ToPlatformColor().CGColor;
        else
            _shapeLayer.StrokeColor = NSColor.Clear.CGColor;

        _shapeLayer.LineWidth = (nfloat)shapeView.StrokeThickness;

        // Dash pattern
        if (shapeView.StrokeDashPattern is { Length: > 0 } dashPattern)
        {
            _shapeLayer.LineDashPattern = dashPattern
                .Select(d => NSNumber.FromFloat((float)(d * shapeView.StrokeThickness)))
                .ToArray();
        }
        else
        {
            _shapeLayer.LineDashPattern = null;
        }

        // Line cap
        _shapeLayer.LineCap = shapeView.StrokeLineCap switch
        {
            LineCap.Round => CAShapeLayer.CapRound,
            LineCap.Square => CAShapeLayer.CapSquare,
            _ => CAShapeLayer.CapButt,
        };

        // Line join
        _shapeLayer.LineJoin = shapeView.StrokeLineJoin switch
        {
            LineJoin.Round => CAShapeLayer.JoinRound,
            LineJoin.Bevel => CAShapeLayer.JoinBevel,
            _ => CAShapeLayer.JoinMiter,
        };
    }

    public static void MapStroke(ShapeViewHandler handler, IShapeView shapeView) => handler.UpdateShape(shapeView);
    public static void MapStrokeThickness(ShapeViewHandler handler, IShapeView shapeView) => handler.UpdateShape(shapeView);
    public static void MapStrokeDashPattern(ShapeViewHandler handler, IShapeView shapeView) => handler.UpdateShape(shapeView);
    public static void MapStrokeLineCap(ShapeViewHandler handler, IShapeView shapeView) => handler.UpdateShape(shapeView);
    public static void MapStrokeLineJoin(ShapeViewHandler handler, IShapeView shapeView) => handler.UpdateShape(shapeView);

    static CGPath PathFToCGPath(PathF pathF)
    {
        var cgPath = new CGPath();

        var points = pathF.Points?.ToArray();
        if (points == null || points.Length == 0)
            return cgPath;

        var segments = pathF.SegmentTypes?.ToArray();
        if (segments == null || segments.Length == 0)
            return cgPath;

        // Walk through path operations
        int index = 0;
        foreach (var op in segments)
        {
            switch (op)
            {
                case PathOperation.Move:
                    if (index < points.Length)
                    {
                        cgPath.MoveToPoint(points[index].X, points[index].Y);
                        index++;
                    }
                    break;

                case PathOperation.Line:
                    if (index < points.Length)
                    {
                        cgPath.AddLineToPoint(points[index].X, points[index].Y);
                        index++;
                    }
                    break;

                case PathOperation.Quad:
                    if (index + 1 < points.Length)
                    {
                        cgPath.AddQuadCurveToPoint(
                            points[index].X, points[index].Y,
                            points[index + 1].X, points[index + 1].Y);
                        index += 2;
                    }
                    break;

                case PathOperation.Cubic:
                    if (index + 2 < points.Length)
                    {
                        cgPath.AddCurveToPoint(
                            points[index].X, points[index].Y,
                            points[index + 1].X, points[index + 1].Y,
                            points[index + 2].X, points[index + 2].Y);
                        index += 3;
                    }
                    break;

                case PathOperation.Close:
                    cgPath.CloseSubpath();
                    break;
            }
        }

        return cgPath;
    }

    public override void PlatformArrange(Graphics.Rect rect)
    {
        base.PlatformArrange(rect);

        if (VirtualView != null && rect.Width > 0 && rect.Height > 0)
            UpdateShape(VirtualView);
    }
}
