using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Controls.Shapes;

namespace Platform.Maui.Linux.Gtk4.Handlers;

/// <summary>
/// Handler for IShapeView (used internally by BoxView and Shape controls).
/// Renders shapes via Gtk.DrawingArea with Cairo.
/// </summary>
public class ShapeViewHandler : GtkViewHandler<IShapeView, Gtk.DrawingArea>
{
	public static new IPropertyMapper<IShapeView, ShapeViewHandler> Mapper =
		new PropertyMapper<IShapeView, ShapeViewHandler>(ViewMapper)
		{
			[nameof(IShapeView.Shape)] = MapShape,
			[nameof(IShapeView.Fill)] = MapFill,
			[nameof(IShapeView.Stroke)] = MapStroke,
			[nameof(IShapeView.StrokeThickness)] = MapStrokeThickness,
			[nameof(IShapeView.Aspect)] = MapAspect,
			[nameof(IShapeView.StrokeDashOffset)] = MapStrokeDash,
			[nameof(IShapeView.StrokeDashPattern)] = MapStrokeDash,
			[nameof(IShapeView.StrokeLineCap)] = MapStrokeLineCap,
			[nameof(IShapeView.StrokeLineJoin)] = MapStrokeLineJoin,
			[nameof(IShapeView.StrokeMiterLimit)] = MapStrokeMiterLimit,
		};

	public ShapeViewHandler() : base(Mapper) { }

	protected override Gtk.DrawingArea CreatePlatformView()
	{
		var area = Gtk.DrawingArea.New();
		area.SetDrawFunc(OnDraw);
		return area;
	}

	public override void PlatformArrange(Rect rect)
	{
		base.PlatformArrange(rect);

		if (PlatformView != null)
		{
			PlatformView.SetContentWidth((int)rect.Width);
			PlatformView.SetContentHeight((int)rect.Height);
			PlatformView.QueueDraw();
		}
	}

	private void OnDraw(Gtk.DrawingArea area, Cairo.Context cr, int width, int height)
	{
		if (VirtualView == null)
			return;

		var strokeThickness = VirtualView.StrokeThickness;
		var halfStroke = strokeThickness / 2.0;

		// Apply stroke settings
		ApplyStrokeSettings(cr);

		// Build the shape path
		BuildShapePath(cr, width, height, halfStroke);

		// Fill
		if (VirtualView.Fill is SolidPaint fillPaint && fillPaint.Color != null)
		{
			var c = fillPaint.Color;
			cr.SetSourceRgba(c.Red, c.Green, c.Blue, c.Alpha);
			cr.FillPreserve();
		}

		// Stroke
		if (VirtualView.Stroke is SolidPaint strokePaint && strokePaint.Color != null)
		{
			var c = strokePaint.Color;
			cr.SetSourceRgba(c.Red, c.Green, c.Blue, c.Alpha);
			cr.LineWidth = strokeThickness;
			cr.Stroke();
		}
		else
		{
			cr.NewPath();
		}
	}

	void ApplyStrokeSettings(Cairo.Context cr)
	{
		if (VirtualView == null) return;

		// Dash pattern
		if (VirtualView.StrokeDashPattern is { Length: > 0 } dashPattern)
		{
			var dashes = new double[dashPattern.Length];
			for (int i = 0; i < dashPattern.Length; i++)
				dashes[i] = dashPattern[i] * VirtualView.StrokeThickness;
			cr.SetDash(dashes, VirtualView.StrokeDashOffset * VirtualView.StrokeThickness);
		}

		// Line cap
		cr.LineCap = VirtualView.StrokeLineCap switch
		{
			LineCap.Round => Cairo.LineCap.Round,
			LineCap.Square => Cairo.LineCap.Square,
			_ => Cairo.LineCap.Butt
		};

		// Line join
		cr.LineJoin = VirtualView.StrokeLineJoin switch
		{
			LineJoin.Round => Cairo.LineJoin.Round,
			LineJoin.Bevel => Cairo.LineJoin.Bevel,
			_ => Cairo.LineJoin.Miter
		};

		cr.MiterLimit = VirtualView.StrokeMiterLimit;
	}

	void BuildShapePath(Cairo.Context cr, int width, int height, double halfStroke)
	{
		if (VirtualView == null)
		{
			cr.Rectangle(halfStroke, halfStroke, width - halfStroke * 2, height - halfStroke * 2);
			return;
		}

		double x = halfStroke, y = halfStroke;
		double w = width - halfStroke * 2, h = height - halfStroke * 2;

		// Check the VirtualView type (the MAUI control) rather than IShapeView.Shape
		// (which returns IShape geometry objects like RoundedRectangle)
		switch (VirtualView)
		{
			case Microsoft.Maui.Controls.Shapes.Rectangle rect:
				DrawRoundedRectangle(cr, x, y, w, h, rect.RadiusX, rect.RadiusY);
				break;

			case Microsoft.Maui.Controls.Shapes.Ellipse:
				DrawEllipse(cr, width / 2.0, height / 2.0, w / 2.0, h / 2.0);
				break;

			case Microsoft.Maui.Controls.Shapes.Line line:
				cr.MoveTo(line.X1, line.Y1);
				cr.LineTo(line.X2, line.Y2);
				break;

			case Microsoft.Maui.Controls.Shapes.Polyline polyline:
				DrawPoints(cr, polyline.Points, closed: false);
				break;

			case Microsoft.Maui.Controls.Shapes.Polygon polygon:
				DrawPoints(cr, polygon.Points, closed: true);
				break;

			case Microsoft.Maui.Controls.Shapes.Path path:
				if (path.Data != null)
					DrawGeometry(cr, path.Data);
				break;

			default:
				// Fallback: try using IShape.PathForBounds if available
				var shape = VirtualView.Shape;
				if (shape != null)
				{
					var pathF = shape.PathForBounds(new RectF((float)x, (float)y, (float)w, (float)h));
					DrawPathF(cr, pathF);
				}
				else
				{
					cr.Rectangle(x, y, w, h);
				}
				break;
		}
	}

	static void DrawRoundedRectangle(Cairo.Context cr, double x, double y, double w, double h, double rx, double ry)
	{
		if (rx <= 0 && ry <= 0)
		{
			cr.Rectangle(x, y, w, h);
			return;
		}

		var r = Math.Max(rx, ry);
		r = Math.Min(r, Math.Min(w / 2, h / 2));

		cr.MoveTo(x + r, y);
		cr.LineTo(x + w - r, y);
		cr.Arc(x + w - r, y + r, r, -Math.PI / 2, 0);
		cr.LineTo(x + w, y + h - r);
		cr.Arc(x + w - r, y + h - r, r, 0, Math.PI / 2);
		cr.LineTo(x + r, y + h);
		cr.Arc(x + r, y + h - r, r, Math.PI / 2, Math.PI);
		cr.LineTo(x, y + r);
		cr.Arc(x + r, y + r, r, Math.PI, 3 * Math.PI / 2);
		cr.ClosePath();
	}

	static void DrawEllipse(Cairo.Context cr, double cx, double cy, double rx, double ry)
	{
		cr.Save();
		cr.Translate(cx, cy);
		cr.Scale(rx, ry);
		cr.Arc(0, 0, 1.0, 0, 2 * Math.PI);
		cr.ClosePath();
		cr.Restore();
	}

	static void DrawPoints(Cairo.Context cr, PointCollection? points, bool closed)
	{
		if (points == null || points.Count == 0) return;

		cr.MoveTo(points[0].X, points[0].Y);
		for (int i = 1; i < points.Count; i++)
			cr.LineTo(points[i].X, points[i].Y);

		if (closed)
			cr.ClosePath();
	}

	static void DrawGeometry(Cairo.Context cr, Geometry geometry)
	{
		switch (geometry)
		{
			case LineGeometry line:
				cr.MoveTo(line.StartPoint.X, line.StartPoint.Y);
				cr.LineTo(line.EndPoint.X, line.EndPoint.Y);
				break;

			case RectangleGeometry rect:
				cr.Rectangle(rect.Rect.X, rect.Rect.Y, rect.Rect.Width, rect.Rect.Height);
				break;

			case EllipseGeometry ellipse:
				DrawEllipse(cr, ellipse.Center.X, ellipse.Center.Y, ellipse.RadiusX, ellipse.RadiusY);
				break;

			case PathGeometry pathGeo:
				foreach (var figure in pathGeo.Figures)
					DrawPathFigure(cr, figure);
				break;

			case GeometryGroup group:
				foreach (var child in group.Children)
					DrawGeometry(cr, child);
				break;
		}
	}

	static void DrawPathFigure(Cairo.Context cr, PathFigure figure)
	{
		cr.MoveTo(figure.StartPoint.X, figure.StartPoint.Y);

		foreach (var segment in figure.Segments)
		{
			switch (segment)
			{
				case LineSegment line:
					cr.LineTo(line.Point.X, line.Point.Y);
					break;

				case BezierSegment bezier:
					cr.CurveTo(
						bezier.Point1.X, bezier.Point1.Y,
						bezier.Point2.X, bezier.Point2.Y,
						bezier.Point3.X, bezier.Point3.Y);
					break;

				case QuadraticBezierSegment quad:
					// Convert quadratic to cubic bezier
					cr.GetCurrentPoint(out var cx, out var cy);
					var cp1x = cx + 2.0 / 3.0 * (quad.Point1.X - cx);
					var cp1y = cy + 2.0 / 3.0 * (quad.Point1.Y - cy);
					var cp2x = quad.Point2.X + 2.0 / 3.0 * (quad.Point1.X - quad.Point2.X);
					var cp2y = quad.Point2.Y + 2.0 / 3.0 * (quad.Point1.Y - quad.Point2.Y);
					cr.CurveTo(cp1x, cp1y, cp2x, cp2y, quad.Point2.X, quad.Point2.Y);
					break;

				case ArcSegment arc:
					DrawArcSegment(cr, arc);
					break;

				case PolyLineSegment polyLine:
					foreach (var pt in polyLine.Points)
						cr.LineTo(pt.X, pt.Y);
					break;

				case PolyBezierSegment polyBezier:
					for (int i = 0; i + 2 < polyBezier.Points.Count; i += 3)
						cr.CurveTo(
							polyBezier.Points[i].X, polyBezier.Points[i].Y,
							polyBezier.Points[i + 1].X, polyBezier.Points[i + 1].Y,
							polyBezier.Points[i + 2].X, polyBezier.Points[i + 2].Y);
					break;

				case PolyQuadraticBezierSegment polyQuad:
					for (int i = 0; i + 1 < polyQuad.Points.Count; i += 2)
					{
						cr.GetCurrentPoint(out var pcx, out var pcy);
						var pcp1x = pcx + 2.0 / 3.0 * (polyQuad.Points[i].X - pcx);
						var pcp1y = pcy + 2.0 / 3.0 * (polyQuad.Points[i].Y - pcy);
						var pcp2x = polyQuad.Points[i + 1].X + 2.0 / 3.0 * (polyQuad.Points[i].X - polyQuad.Points[i + 1].X);
						var pcp2y = polyQuad.Points[i + 1].Y + 2.0 / 3.0 * (polyQuad.Points[i].Y - polyQuad.Points[i + 1].Y);
						cr.CurveTo(pcp1x, pcp1y, pcp2x, pcp2y, polyQuad.Points[i + 1].X, polyQuad.Points[i + 1].Y);
					}
					break;
			}
		}

		if (figure.IsClosed)
			cr.ClosePath();
	}

	static void DrawArcSegment(Cairo.Context cr, ArcSegment arc)
	{
		// Simplified arc: draw a line to the endpoint
		cr.LineTo(arc.Point.X, arc.Point.Y);
	}

	static void DrawPathF(Cairo.Context cr, PathF? path)
	{
		if (path == null) return;

		for (int i = 0; i < path.OperationCount; i++)
		{
			var type = path.GetSegmentType(i);
			var points = path.GetPointsForSegment(i);

			switch (type)
			{
				case PathOperation.Move:
					if (points.Length > 0)
						cr.MoveTo(points[0].X, points[0].Y);
					break;
				case PathOperation.Line:
					if (points.Length > 0)
						cr.LineTo(points[0].X, points[0].Y);
					break;
				case PathOperation.Cubic:
					if (points.Length >= 3)
						cr.CurveTo(points[0].X, points[0].Y, points[1].X, points[1].Y, points[2].X, points[2].Y);
					break;
				case PathOperation.Quad:
					if (points.Length >= 2)
					{
						cr.GetCurrentPoint(out var cx, out var cy);
						var c1x = cx + 2.0 / 3.0 * (points[0].X - cx);
						var c1y = cy + 2.0 / 3.0 * (points[0].Y - cy);
						var c2x = points[1].X + 2.0 / 3.0 * (points[0].X - points[1].X);
						var c2y = points[1].Y + 2.0 / 3.0 * (points[0].Y - points[1].Y);
						cr.CurveTo(c1x, c1y, c2x, c2y, points[1].X, points[1].Y);
					}
					break;
				case PathOperation.Close:
					cr.ClosePath();
					break;
			}
		}
	}

	public static void MapShape(ShapeViewHandler handler, IShapeView view) => handler.PlatformView?.QueueDraw();
	public static void MapFill(ShapeViewHandler handler, IShapeView view) => handler.PlatformView?.QueueDraw();
	public static void MapStroke(ShapeViewHandler handler, IShapeView view) => handler.PlatformView?.QueueDraw();
	public static void MapStrokeThickness(ShapeViewHandler handler, IShapeView view) => handler.PlatformView?.QueueDraw();
	public static void MapAspect(ShapeViewHandler handler, IShapeView view) { }
	public static void MapStrokeDash(ShapeViewHandler handler, IShapeView view) => handler.PlatformView?.QueueDraw();
	public static void MapStrokeLineCap(ShapeViewHandler handler, IShapeView view) => handler.PlatformView?.QueueDraw();
	public static void MapStrokeLineJoin(ShapeViewHandler handler, IShapeView view) => handler.PlatformView?.QueueDraw();
	public static void MapStrokeMiterLimit(ShapeViewHandler handler, IShapeView view) => handler.PlatformView?.QueueDraw();
}
