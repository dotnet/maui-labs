#nullable enable
namespace Comet
{
	/// <summary>Per-corner radii (Dp) for a rounded surface. Uniform when all four are equal.</summary>
	public readonly record struct CornerRadii(double TopLeft, double TopRight, double BottomRight, double BottomLeft)
	{
		public CornerRadii(double uniform) : this(uniform, uniform, uniform, uniform) { }

		public bool IsUniform => TopLeft == TopRight && TopRight == BottomRight && BottomRight == BottomLeft;

		public bool IsZero => TopLeft == 0 && TopRight == 0 && BottomRight == 0 && BottomLeft == 0;
	}

	/// <summary>
	/// Fluent surface styling that any view can opt into — rounded corners (uniform or
	/// per-corner) and elevation — the building blocks of a Material "card" or a chat bubble.
	/// Stored as environment values and emitted to the backend node, so a card is just
	/// <c>new VStack { … }.Background(c).CornerRadius(12).Elevation(2).Padding(16)</c> and a
	/// Jetchat bubble is <c>.CornerRadius(4, 20, 20, 20)</c> — composable and identical across
	/// the Compose/SwiftUI backends.
	/// </summary>
	public static class SurfaceExtensions
	{
		/// <summary>Environment key for the corner radii (<see cref="CornerRadii"/>).</summary>
		public const string CornerRadiusKey = "Comet.CornerRadius";

		/// <summary>Environment key for the elevation / shadow depth (Dp).</summary>
		public const string ElevationKey = "Comet.Elevation";

		/// <summary>Rounds this view's corners (and clips its content/background) to <paramref name="radius"/> Dp.</summary>
		public static T CornerRadius<T>(this T view, double radius) where T : View
		{
			view.SetEnvironment(CornerRadiusKey, new CornerRadii(radius), false);
			return view;
		}

		/// <summary>Rounds each corner independently (Dp), in the order top-left, top-right,
		/// bottom-right, bottom-left — e.g. the Jetchat chat bubble is <c>(4, 20, 20, 20)</c>.</summary>
		public static T CornerRadius<T>(this T view, double topLeft, double topRight, double bottomRight, double bottomLeft) where T : View
		{
			view.SetEnvironment(CornerRadiusKey, new CornerRadii(topLeft, topRight, bottomRight, bottomLeft), false);
			return view;
		}

		/// <summary>Raises this view with a soft drop shadow of <paramref name="elevation"/> Dp
		/// (the Material card "lift"). Combine with <see cref="CornerRadius{T}(T,double)"/> for a rounded card.</summary>
		public static T Elevation<T>(this T view, double elevation) where T : View
		{
			view.SetEnvironment(ElevationKey, elevation, false);
			return view;
		}
	}
}
