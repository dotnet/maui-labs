#nullable enable
using Microsoft.Maui.Graphics;

namespace Comet
{
	/// <summary>Per-corner radii (Dp) for a rounded surface — the wire shape the backend nodes
	/// consume (derived from a view's <c>ClipShape</c>). Uniform when all four are equal.</summary>
	public readonly record struct CornerRadii(double TopLeft, double TopRight, double BottomRight, double BottomLeft)
	{
		public CornerRadii(double uniform) : this(uniform, uniform, uniform, uniform) { }

		public bool IsUniform => TopLeft == TopRight && TopRight == BottomRight && BottomRight == BottomLeft;

		public bool IsZero => TopLeft == 0 && TopRight == 0 && BottomRight == 0 && BottomLeft == 0;
	}

	/// <summary>A stroke border the backend nodes consume (derived from a view's <c>Border</c>
	/// shape's stroke): <paramref name="Width"/> Dp of <paramref name="Color"/>.</summary>
	public readonly record struct BorderSpec(double Width, Color Color);

	/// <summary>
	/// Ergonomic sugar for rounded corners, elevation and stroke borders. These write the SAME
	/// canonical view environment (<c>ClipShape</c> / <c>Shadow</c> / <c>Border</c>) that Comet's
	/// styling system already uses (<c>.ClipShape</c>, <c>.Shadow</c>, <c>.RoundedBorder</c>,
	/// <c>ButtonStyles</c>, <c>ViewModifier</c>) — so a Material card / chat bubble is composable
	/// and there is a single styling vocabulary the backend nodes read, not a parallel one.
	/// </summary>
	public static class SurfaceExtensions
	{
		/// <summary>Rounds this view's corners (uniform) — sets <c>ClipShape(RoundedRectangle)</c>.</summary>
		public static T CornerRadius<T>(this T view, double radius) where T : View =>
			view.ClipShape(new RoundedRectangle((float)radius));

		/// <summary>Rounds each corner independently (Dp), top-left, top-right, bottom-right,
		/// bottom-left — e.g. the Jetchat bubble <c>(4, 20, 20, 20)</c>. Sets
		/// <c>ClipShape(AsymmetricRoundedRectangle)</c>.</summary>
		public static T CornerRadius<T>(this T view, double topLeft, double topRight, double bottomRight, double bottomLeft) where T : View =>
			// AsymmetricRoundedRectangle's ctor order is (topLeft, topRight, bottomLeft, bottomRight).
			view.ClipShape(new AsymmetricRoundedRectangle((float)topLeft, (float)topRight, (float)bottomLeft, (float)bottomRight));

		/// <summary>Raises this view with a soft drop shadow of <paramref name="elevation"/> Dp —
		/// sets the canonical <c>Shadow</c> (the backend reads its radius as the elevation).</summary>
		public static T Elevation<T>(this T view, double elevation) where T : View =>
			view.Shadow(Colors.Black, radius: (float)elevation, x: 0, y: (float)(elevation / 2));

		/// <summary>Strokes this view's outline (following its corner radius) with
		/// <paramref name="width"/> Dp of <paramref name="color"/> — sets the canonical
		/// <c>Border</c> shape carrying the stroke.</summary>
		public static T Border<T>(this T view, double width, Color color) where T : View =>
			view.Border(new RoundedRectangle(0).Stroke(color, (float)width));
	}
}
