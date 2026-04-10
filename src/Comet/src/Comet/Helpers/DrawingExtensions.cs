using System;

using Comet.Graphics;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

// ReSharper disable once CheckNamespace
namespace Comet
{
	public static class DrawingExtensions
	{
		public static T Shadow<T>(this T view, Color color, float? radius = null, float? x = null, float? y = null, Type type = null) where T : View
			=> view.Shadow(new SolidPaint(color), radius, x, y, type);
		public static T Shadow<T>(this T view, Paint paint = null, float? radius = null, float? x = null, float? y = null, Type type = null) where T : View
		{
			var shadow = view.GetShadow() ?? new Shadow();

			if (paint is not null)
				shadow = shadow.WithPaint(paint);

			if (radius is not null)
				shadow = shadow.WithRadius((float)radius);

			if (x is not null || y is not null)
			{
				var newX = x ?? shadow.Offset.X;
				var newY = y ?? shadow.Offset.Y;
				shadow = shadow.WithOffset(new Point(newX, newY));
			}
			if (type is not null)
				view.SetEnvironment(type, EnvironmentKeys.View.Shadow, shadow, true);
			else
				view.SetEnvironment(EnvironmentKeys.View.Shadow, shadow, false);
			return view;
		}

		public static Shadow GetShadow(this View view, Shadow defaultShadow = null, Type type = null)
		{
			var shadow = view.GetEnvironment<Shadow>(type, EnvironmentKeys.View.Shadow);
			return shadow ?? defaultShadow;
		}
		public static T ClipShape<T>(this T view, Shape shape, Type type = null) where T : View
		{
			if (type is not null)
				view.SetEnvironment(type, EnvironmentKeys.View.ClipShape, shape, true);
			else
				view.SetEnvironment(EnvironmentKeys.View.ClipShape, shape, false);
			return view;
		}

		public static T ClipShape<T>(this T view, IShape shape) where T : View
		{
			view.SetEnvironment(EnvironmentKeys.View.ClipShape, (object)shape, false);
			return view;
		}
		public static T ClipShape<T>(this T view, Func<IShape> shape) where T : View => view.ClipShape(shape());

		public static Shape GetClipShape(this View view, Shape defaultShape = null, Type type = null)
		{
			var shape = view.GetEnvironment<Shape>(type, EnvironmentKeys.View.ClipShape);
			return shape ?? defaultShape;
		}

		public static T Stroke<T>(this T shape, Color color, float lineWidth, bool cascades = true, Type type = null) where T : Shape
		{
			if (type is not null && !cascades)
				Logger.Fatal($"Setting a type, and cascades = false does nothing!");
			if (type is not null)
			{
				shape.SetEnvironment(type, EnvironmentKeys.Shape.LineWidth, lineWidth, true);
				shape.SetEnvironment(type, EnvironmentKeys.Shape.StrokeColor, new SolidPaint(color), true);
			}
			else
			{
				shape.SetEnvironment(EnvironmentKeys.Shape.LineWidth, lineWidth, cascades);
				shape.SetEnvironment(EnvironmentKeys.Shape.StrokeColor, new SolidPaint(color), cascades);
			}
			return shape;
		}
		public static T Stroke<T>(this T shape, Paint paint, float lineWidth, bool cascades = true, Type type = null) where T : Shape
		{
			if (type is not null && !cascades)
				Logger.Fatal($"Setting a type, and cascades = false does nothing!");
			if (type is not null)
			{
				shape.SetEnvironment(type, EnvironmentKeys.Shape.LineWidth, lineWidth, true);
				shape.SetEnvironment(type, EnvironmentKeys.Shape.StrokeColor, paint, true);
			}
			else
			{
				shape.SetEnvironment(EnvironmentKeys.Shape.LineWidth, lineWidth, cascades);
				shape.SetEnvironment(EnvironmentKeys.Shape.StrokeColor, paint, cascades);
			}
			return shape;
		}

		public static T Fill<T>(this T shape, Color color, bool cascades = true, Type type = null) where T : Shape
		{
			if (type is not null && !cascades)
				Logger.Fatal($"Setting a type, and cascades = false does nothing!");
			if (type is not null)
				shape.SetEnvironment(type, EnvironmentKeys.Shape.Fill, color, true);
			else
				shape.SetEnvironment(EnvironmentKeys.Shape.Fill, color, cascades);

			return shape;
		}

		public static T Fill<T>(this T shape, Gradient gradient, bool cascades = true, Type type = null) where T : Shape
		{
			if (type is not null && !cascades)
				Logger.Fatal($"Setting a type, and cascades = false does nothing!");
			if (type is not null)
				shape.SetEnvironment(type, EnvironmentKeys.Shape.Fill, gradient, true);
			else
				shape.SetEnvironment(EnvironmentKeys.Shape.Fill, gradient, cascades);
			return shape;
		}

		public static T Style<T>(this T shape, DrawingStyle drawingStyle, bool cascades = true, Type type = null) where T : Shape
		{
			if (type is not null && !cascades)
				Logger.Fatal($"Setting a type, and cascades = false does nothing!");
			if (type is not null)
				shape.SetEnvironment(type, EnvironmentKeys.Shape.DrawingStyle, drawingStyle, true);
			else
				shape.SetEnvironment(EnvironmentKeys.Shape.DrawingStyle, drawingStyle, cascades);
			return shape;
		}

		public static float GetLineWidth(this Shape shape, View view, float defaultStroke, Type type = null)
		{
			var stroke = shape.GetEnvironment<float?>(view, type, EnvironmentKeys.Shape.LineWidth);
			return stroke ?? defaultStroke;
		}

		public static Color GetStrokeColor(this Shape shape, View view, Color defaultColor, Type type = null)
		{
			// Stroke() stores a SolidPaint, so try Paint first, then Color
			var paint = shape.GetEnvironment<Paint>(view, type, EnvironmentKeys.Shape.StrokeColor);
			if (paint is SolidPaint solidPaint)
				return solidPaint.Color ?? defaultColor;
			var color = shape.GetEnvironment<Color>(view, type, EnvironmentKeys.Shape.StrokeColor);
			return color ?? defaultColor;
		}

		public static object GetFill(this Shape shape, View view, object defaultFill = null, Type type = null)
		{
			var fill = shape.GetEnvironment<object>(view, type, EnvironmentKeys.Shape.Fill);
			return fill ?? defaultFill;
		}

		public static DrawingStyle GetDrawingStyle(this Shape shape, View view, DrawingStyle defaultDrawingStyle = DrawingStyle.StrokeFill, Type type = null)
		{
			var drawingStyle = shape.GetEnvironment<DrawingStyle?>(view, type, EnvironmentKeys.Shape.DrawingStyle);
			return drawingStyle ?? defaultDrawingStyle;
		}

		public static T Border<T>(this T view, Shape shape, Type type = null) where T : View
		{
			view.SetEnvironment(type, EnvironmentKeys.View.Border, shape, type is not null);
			return view;
		}

		public static Shape GetBorder(this View view, Shape defaultShape = null, Type type = null)
		{
			var shape = view.GetEnvironment<Shape>(type, EnvironmentKeys.View.Border);
			return shape ?? defaultShape;
		}

		public static T RoundedBorder<T>(this T view, float radius = 4, Color color = null, float strokeSize = 1, bool filled = false, Type type = null) where T : View
		{
			var finalColor = color ?? Colors.Black;
			var shape = new RoundedRectangle(radius).Stroke(finalColor, strokeSize);
			view.ClipShape(shape);
			view.Border(shape);
			if (filled)
				view.Background(color);
			return view;
		}

		public static Border CornerRadius(this Border view, float radius)
		{
			view.ClipShape(new RoundedRectangle(radius));
			return view;
		}

		public static Border CornerRadius(this Border view, float topLeft, float topRight, float bottomLeft, float bottomRight)
		{
			view.ClipShape(new AsymmetricRoundedRectangle(topLeft, topRight, bottomLeft, bottomRight));
			return view;
		}

		public static Border StrokeColor(this Border view, Color color)
		{
			view.SetEnvironment(EnvironmentKeys.Shape.StrokeColor, (Paint)new SolidPaint(color), false);
			return view;
		}

		public static Border StrokeThickness(this Border view, double thickness)
		{
			view.SetEnvironment(EnvironmentKeys.Shape.LineWidth, thickness, false);
			return view;
		}
	}

	/// <summary>
	/// Custom Shape that draws a rounded rectangle with different corner radii.
	/// Extends Shape (like RoundedRectangle) so it participates in the environment system correctly.
	/// </summary>
	public sealed class AsymmetricRoundedRectangle : Shape
	{
		readonly float _topLeft, _topRight, _bottomLeft, _bottomRight;

		public AsymmetricRoundedRectangle(float topLeft, float topRight, float bottomLeft, float bottomRight)
		{
			_topLeft = topLeft;
			_topRight = topRight;
			_bottomLeft = bottomLeft;
			_bottomRight = bottomRight;
		}

		public override PathF PathForBounds(Rect bounds)
		{
			var path = new PathF();
			var x = (float)bounds.X;
			var y = (float)bounds.Y;
			var w = (float)bounds.Width;
			var h = (float)bounds.Height;
			var tl = Math.Min(_topLeft, Math.Min(w / 2, h / 2));
			var tr = Math.Min(_topRight, Math.Min(w / 2, h / 2));
			var br = Math.Min(_bottomRight, Math.Min(w / 2, h / 2));
			var bl = Math.Min(_bottomLeft, Math.Min(w / 2, h / 2));

			// Start at top-left after the corner
			path.MoveTo(x + tl, y);

			// Top edge → top-right corner
			path.LineTo(x + w - tr, y);
			if (tr > 0)
				path.QuadTo(x + w, y, x + w, y + tr);

			// Right edge → bottom-right corner
			path.LineTo(x + w, y + h - br);
			if (br > 0)
				path.QuadTo(x + w, y + h, x + w - br, y + h);

			// Bottom edge → bottom-left corner
			path.LineTo(x + bl, y + h);
			if (bl > 0)
				path.QuadTo(x, y + h, x, y + h - bl);

			// Left edge → top-left corner
			path.LineTo(x, y + tl);
			if (tl > 0)
				path.QuadTo(x, y, x + tl, y);

			path.Close();
			return path;
		}
	}
}
