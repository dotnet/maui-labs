using System;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;
using Comet.Graphics;

namespace Comet
{
	// Command, Command<T>: Use Microsoft.Maui.Controls.Command (already in scope)

	// ──────────────────────────────────────────────
	// 2. ToolTipProperties
	// ──────────────────────────────────────────────

	/// <summary>
	/// Provides tooltip support for Comet views, mirroring MAUI's ToolTipProperties.
	/// </summary>
	public static class ToolTipProperties
	{
		internal const string ToolTipTextKey = "View.ToolTipText";

		public static string GetText(View view) =>
			view.GetEnvironment<string>(ToolTipTextKey) ?? string.Empty;

		public static void SetText(View view, string tooltip) =>
			view.SetEnvironment(ToolTipTextKey, tooltip, cascades: false);
	}

	// ──────────────────────────────────────────────
	// 3. Brush types
	// ──────────────────────────────────────────────

	/// <summary>
	/// Abstract base for gradient brushes, wrapping Comet's Gradient.
	/// </summary>
	public abstract class GradientBrush
	{
		public List<GradientStop> GradientStops { get; set; } = new();

		public abstract Gradient ToGradient();
	}

	/// <summary>
	/// Represents a single gradient stop with color and offset.
	/// </summary>
	public class GradientStop
	{
		public Color Color { get; set; }
		public float Offset { get; set; }

		public GradientStop() { }

		public GradientStop(Color color, float offset)
		{
			Color = color;
			Offset = offset;
		}
	}

	/// <summary>
	/// Solid color brush wrapping a single Color value.
	/// </summary>
	public class SolidColorBrush
	{
		public Color Color { get; set; }

		public SolidColorBrush() { }

		public SolidColorBrush(Color color) => Color = color;

		public SolidPaint ToPaint() => new SolidPaint(Color);

		public static implicit operator Paint(SolidColorBrush brush) => brush?.ToPaint();
	}

	/// <summary>
	/// Linear gradient brush wrapping Comet's LinearGradient.
	/// </summary>
	public class LinearGradientBrush : GradientBrush
	{
		public Point StartPoint { get; set; } = Point.Zero;
		public Point EndPoint { get; set; } = new Point(1, 1);

		public override Gradient ToGradient()
		{
			var stops = GradientStops.Select(s => new Stop(s.Offset, s.Color)).ToArray();
			return stops.Length > 0
				? new LinearGradient(stops, StartPoint, EndPoint)
				: new LinearGradient(new[] { Colors.Transparent, Colors.Transparent }, StartPoint, EndPoint);
		}
	}

	/// <summary>
	/// Brush that paints an area with an image. Maps to MAUI's ImageBrush concept.
	/// </summary>
	public class ImageBrush
	{
		public ImageBrush() { }
		public ImageBrush(ImageSource imageSource) { ImageSource = imageSource; }

		public ImageSource ImageSource { get; set; }

		public bool IsEmpty => ImageSource is null;
	}

	/// <summary>
	/// Radial gradient brush wrapping Comet's RadialGradient.
	/// </summary>
	public class RadialGradientBrush : GradientBrush
	{
		public Point Center { get; set; } = new Point(0.5, 0.5);
		public float Radius { get; set; } = 0.5f;

		public override Gradient ToGradient()
		{
			var stops = GradientStops.Select(s => new Stop(s.Offset, s.Color)).ToArray();
			return stops.Length > 0
				? new RadialGradient(stops, Center, 0f, Radius)
				: new RadialGradient(new[] { Colors.Transparent, Colors.Transparent }, Center, 0f, Radius);
		}
	}

	// ──────────────────────────────────────────────
	// 3b. Keyboard accelerator
	// ──────────────────────────────────────────────

	/// <summary>
	/// Defines a keyboard shortcut for a menu item. Maps to MAUI's KeyboardAccelerator.
	/// </summary>
	public class KeyboardAccelerator
	{
		public string Key { get; set; }
		public KeyboardAcceleratorModifiers Modifiers { get; set; }
	}

	[Flags]
	public enum KeyboardAcceleratorModifiers
	{
		None = 0,
		Shift = 1,
		Ctrl = 2,
		Alt = 4,
		Windows = 8
	}

	// ──────────────────────────────────────────────
	// 4. Trigger types
	// ──────────────────────────────────────────────

	/// <summary>
	/// Abstract base for all trigger types, mirroring MAUI's TriggerBase.
	/// </summary>
	public abstract class TriggerBase
	{
		internal View AssociatedObject { get; private set; }

		internal void Attach(View view) => AssociatedObject = view;
		internal void Detach() => AssociatedObject = null;
	}

	/// <summary>
	/// A property-value trigger that fires when a property equals a given value.
	/// </summary>
	public class Trigger : TriggerBase
	{
		public string Property { get; set; }
		public object Value { get; set; }
	}

	/// <summary>
	/// Abstract base for trigger actions.
	/// </summary>
	public abstract class TriggerAction
	{
		public abstract void Invoke(View sender);
	}

	/// <summary>
	/// Generic trigger action for type-safe invocation.
	/// </summary>
	public abstract class TriggerAction<T> : TriggerAction where T : View
	{
		protected abstract void Invoke(T sender);

		public override void Invoke(View sender)
		{
			if (sender is T typed)
				Invoke(typed);
		}
	}

	/// <summary>
	/// A trigger that fires when multiple conditions are satisfied.
	/// </summary>
	public class MultiTrigger : TriggerBase
	{
		public List<Condition> Conditions { get; } = new();
	}

	/// <summary>
	/// Base class for trigger conditions.
	/// </summary>
	public abstract class Condition { }

	/// <summary>
	/// A condition that evaluates a property against a value.
	/// </summary>
	public class PropertyCondition : Condition
	{
		public string Property { get; set; }
		public object Value { get; set; }
	}

	/// <summary>
	/// A condition that evaluates a binding expression.
	/// </summary>
	public class BindingCondition : Condition
	{
		public Func<bool> Binding { get; set; }
		public object Value { get; set; }
	}

	// ──────────────────────────────────────────────
	// 5. Geometry classes
	// ──────────────────────────────────────────────

	/// <summary>
	/// Abstract base for geometry types used in clip paths and shapes.
	/// </summary>
	public abstract class Geometry { }

	/// <summary>
	/// An ellipse geometry defined by a center point and radii.
	/// </summary>
	public class EllipseGeometry : Geometry
	{
		public Point Center { get; set; }
		public double RadiusX { get; set; }
		public double RadiusY { get; set; }

		public EllipseGeometry() { }

		public EllipseGeometry(Point center, double radiusX, double radiusY)
		{
			Center = center;
			RadiusX = radiusX;
			RadiusY = radiusY;
		}
	}

	/// <summary>
	/// A rectangular geometry.
	/// </summary>
	public class RectangleGeometry : Geometry
	{
		public Rect Rect { get; set; }

		public RectangleGeometry() { }

		public RectangleGeometry(Rect rect) => Rect = rect;
	}

	/// <summary>
	/// A rounded rectangle geometry with corner radius.
	/// </summary>
	public class RoundRectangleGeometry : Geometry
	{
		public CornerRadius CornerRadius { get; set; }
		public Rect Rect { get; set; }

		public RoundRectangleGeometry() { }

		public RoundRectangleGeometry(CornerRadius cornerRadius, Rect rect)
		{
			CornerRadius = cornerRadius;
			Rect = rect;
		}
	}

	/// <summary>
	/// A line geometry defined by start and end points.
	/// </summary>
	public class LineGeometry : Geometry
	{
		public Point StartPoint { get; set; }
		public Point EndPoint { get; set; }

		public LineGeometry() { }

		public LineGeometry(Point startPoint, Point endPoint)
		{
			StartPoint = startPoint;
			EndPoint = endPoint;
		}
	}

	/// <summary>
	/// A geometry defined by path figures.
	/// </summary>
	public class PathGeometry : Geometry
	{
		public List<PathFigure> Figures { get; } = new();
		public FillRule FillRule { get; set; } = FillRule.EvenOdd;
	}

	/// <summary>
	/// Fill rule for path geometries.
	/// </summary>
	public enum FillRule
	{
		EvenOdd,
		Nonzero
	}

	/// <summary>
	/// Represents a sub-path within a PathGeometry.
	/// </summary>
	public class PathFigure
	{
		public Point StartPoint { get; set; }
		public bool IsClosed { get; set; }
		public bool IsFilled { get; set; } = true;
		public List<PathSegment> Segments { get; } = new();
	}

	/// <summary>
	/// Abstract base for path segments.
	/// </summary>
	public abstract class PathSegment { }

	/// <summary>
	/// A line segment in a path figure.
	/// </summary>
	public class LineSegment : PathSegment
	{
		public Point Point { get; set; }

		public LineSegment() { }
		public LineSegment(Point point) => Point = point;
	}

	/// <summary>
	/// An arc segment in a path figure.
	/// </summary>
	public class ArcSegment : PathSegment
	{
		public Point Point { get; set; }
		public Size Size { get; set; }
		public double RotationAngle { get; set; }
		public bool IsLargeArc { get; set; }
		public SweepDirection SweepDirection { get; set; }
	}

	/// <summary>
	/// Sweep direction for arc segments.
	/// </summary>
	public enum SweepDirection
	{
		CounterClockwise,
		Clockwise
	}

	/// <summary>
	/// A cubic Bezier segment in a path figure.
	/// </summary>
	public class BezierSegment : PathSegment
	{
		public Point Point1 { get; set; }
		public Point Point2 { get; set; }
		public Point Point3 { get; set; }
	}

	/// <summary>
	/// A quadratic Bezier segment in a path figure.
	/// </summary>
	public class QuadraticBezierSegment : PathSegment
	{
		public Point Point1 { get; set; }
		public Point Point2 { get; set; }
	}

	/// <summary>
	/// A poly Bezier segment composed of multiple points.
	/// </summary>
	public class PolyBezierSegment : PathSegment
	{
		public PolyBezierSegment() { }
		public PolyBezierSegment(IList<Point> points) { Points = new List<Point>(points); }
		public List<Point> Points { get; set; } = new();
	}

	/// <summary>
	/// A poly line segment composed of multiple points.
	/// </summary>
	public class PolyLineSegment : PathSegment
	{
		public PolyLineSegment() { }
		public PolyLineSegment(IList<Point> points) { Points = new List<Point>(points); }
		public List<Point> Points { get; set; } = new();
	}

	/// <summary>
	/// A poly quadratic Bezier segment composed of multiple points.
	/// </summary>
	public class PolyQuadraticBezierSegment : PathSegment
	{
		public PolyQuadraticBezierSegment() { }
		public PolyQuadraticBezierSegment(IList<Point> points) { Points = new List<Point>(points); }
		public List<Point> Points { get; set; } = new();
	}

	/// <summary>
	/// A group of geometry objects combined into a single geometry.
	/// </summary>
	public class GeometryGroup : Geometry
	{
		public List<Geometry> Children { get; } = new();
		public FillRule FillRule { get; set; } = FillRule.EvenOdd;
	}

	// ──────────────────────────────────────────────
	// 6. Transform classes
	// ──────────────────────────────────────────────

	/// <summary>
	/// Abstract base for 2D transform operations.
	/// </summary>
	public abstract class Transform { }

	/// <summary>
	/// Rotates an element by the specified angle around a center point.
	/// </summary>
	public class RotateTransform : Transform
	{
		public double Angle { get; set; }
		public double CenterX { get; set; }
		public double CenterY { get; set; }

		public RotateTransform() { }

		public RotateTransform(double angle)
		{
			Angle = angle;
		}
	}

	/// <summary>
	/// Scales an element by the specified factors around a center point.
	/// </summary>
	public class ScaleTransform : Transform
	{
		public double ScaleX { get; set; } = 1;
		public double ScaleY { get; set; } = 1;
		public double CenterX { get; set; }
		public double CenterY { get; set; }

		public ScaleTransform() { }

		public ScaleTransform(double scaleX, double scaleY)
		{
			ScaleX = scaleX;
			ScaleY = scaleY;
		}
	}

	/// <summary>
	/// Translates an element by the specified X and Y offsets.
	/// </summary>
	public class TranslateTransform : Transform
	{
		public double X { get; set; }
		public double Y { get; set; }

		public TranslateTransform() { }

		public TranslateTransform(double x, double y)
		{
			X = x;
			Y = y;
		}
	}

	/// <summary>
	/// Skews an element by the specified angles around a center point.
	/// </summary>
	public class SkewTransform : Transform
	{
		public double AngleX { get; set; }
		public double AngleY { get; set; }
		public double CenterX { get; set; }
		public double CenterY { get; set; }

		public SkewTransform() { }

		public SkewTransform(double angleX, double angleY)
		{
			AngleX = angleX;
			AngleY = angleY;
		}
	}

	/// <summary>
	/// A composite transform combining rotation, scale, skew, and translation.
	/// </summary>
	public class CompositeTransform : Transform
	{
		public double CenterX { get; set; }
		public double CenterY { get; set; }
		public double Rotation { get; set; }
		public double ScaleX { get; set; } = 1;
		public double ScaleY { get; set; } = 1;
		public double SkewX { get; set; }
		public double SkewY { get; set; }
		public double TranslateX { get; set; }
		public double TranslateY { get; set; }
	}

	/// <summary>
	/// A group of transforms applied sequentially.
	/// </summary>
	public class TransformGroup : Transform
	{
		public List<Transform> Children { get; } = new();
	}

	// ──────────────────────────────────────────────
	// 7. Navigation page types
	// ──────────────────────────────────────────────

	// NavigationPage: Use Comet.NavigationView or Microsoft.Maui.Controls.NavigationPage

	// FlyoutPage: Use Microsoft.Maui.Controls.FlyoutPage (already in scope)

	// TabbedPage: Use Comet.TabView or Microsoft.Maui.Controls.TabbedPage

	// ──────────────────────────────────────────────
	// ToolTip extension method
	// ──────────────────────────────────────────────

	public static class ToolTipViewExtensions
	{
		/// <summary>
		/// Sets a tooltip on the view, returning the view for fluent chaining.
		/// </summary>
		public static T ToolTip<T>(this T view, string tooltip) where T : View
		{
			ToolTipProperties.SetText(view, tooltip);
			return view;
		}
	}

	// ──────────────────────────────────────────────
	// 8. Shell types (FlyoutItem, Tab, TabBar)
	// ──────────────────────────────────────────────

	// FlyoutItem: Use Microsoft.Maui.Controls.FlyoutItem (already in scope)

	// Tab: Use Microsoft.Maui.Controls.Tab (already in scope)

	// TabBar: Use Microsoft.Maui.Controls.TabBar (already in scope)

	// ──────────────────────────────────────────────
	// 9. Window
	// ──────────────────────────────────────────────

	// Window is intentionally NOT aliased here because it conflicts with
	// Microsoft.Maui.Controls.Window. Use CometWindow directly instead.

	// ──────────────────────────────────────────────
	// 10. Effect / RoutingEffect
	// ──────────────────────────────────────────────

	/// <summary>
	/// Abstract base for platform effects, mirroring MAUI's Effect.
	/// </summary>
	public abstract class Effect
	{
		public string ResolveId { get; set; }
		public View Element { get; internal set; }
		public bool IsAttached { get; internal set; }
		protected virtual void OnAttached() { }
		protected virtual void OnDetached() { }
	}

	/// <summary>
	/// An effect resolved by ID, mirroring MAUI's RoutingEffect.
	/// </summary>
	public class RoutingEffect : Effect
	{
		public RoutingEffect(string effectId) { ResolveId = effectId; }
	}

	// ──────────────────────────────────────────────
	// 11. RelativeBindingSource
	// ──────────────────────────────────────────────

	/// <summary>
	/// Describes the source of a relative binding.
	/// </summary>
	public class RelativeBindingSource
	{
		public static readonly RelativeBindingSource Self = new(RelativeBindingSourceMode.Self);
		public static readonly RelativeBindingSource TemplatedParent = new(RelativeBindingSourceMode.TemplatedParent);

		public RelativeBindingSourceMode Mode { get; }
		public Type AncestorType { get; set; }
		public int AncestorLevel { get; set; } = 1;

		public RelativeBindingSource(RelativeBindingSourceMode mode) { Mode = mode; }
	}

	/// <summary>
	/// Modes for relative binding source resolution.
	/// </summary>
	public enum RelativeBindingSourceMode
	{
		Self,
		TemplatedParent,
		FindAncestor,
		FindAncestorBindingContext
	}

	// ──────────────────────────────────────────────
	// 12. TemplateBinding
	// ──────────────────────────────────────────────

	/// <summary>
	/// A binding that connects a property in a control template to a property on the templated parent.
	/// </summary>
	public class TemplateBinding
	{
		public string Path { get; set; }
		public IValueConverter Converter { get; set; }
		public object ConverterParameter { get; set; }
		public string StringFormat { get; set; }
	}

	// ──────────────────────────────────────────────
	// 13. RelativeLayout
	// ──────────────────────────────────────────────

	/// <summary>
	/// RelativeLayout is legacy in MAUI 9.0 but still exists.
	/// MAUI recommends using Grid instead.
	/// </summary>
	public class RelativeLayout : AbstractLayout
	{
		protected override ILayoutManager CreateLayoutManager() => new Comet.Layout.GridLayoutManager(this, 0);
	}

	// ──────────────────────────────────────────────
	// 14. MultiPage / TemplatedPage
	// ──────────────────────────────────────────────

	// MultiPage: Use Microsoft.Maui.Controls.MultiPage (already in scope)

	// TemplatedPage: Use Microsoft.Maui.Controls.TemplatedPage (already in scope)
}
