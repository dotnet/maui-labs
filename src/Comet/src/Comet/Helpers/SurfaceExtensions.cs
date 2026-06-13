#nullable enable
namespace Comet
{
	/// <summary>
	/// Fluent surface styling that any view can opt into — rounded corners and elevation —
	/// the building blocks of a Material "card" (a rounded, tonal, slightly-raised surface).
	/// Stored as environment values and emitted to the backend node, so a card is just
	/// <c>new VStack { … }.Background(c).CornerRadius(12).Elevation(2).Padding(16)</c> rather
	/// than a dedicated control — composable and identical across the Compose/SwiftUI backends.
	/// </summary>
	public static class SurfaceExtensions
	{
		/// <summary>Environment key for the corner radius (Dp), consumed by the backend nodes.</summary>
		public const string CornerRadiusKey = "Comet.CornerRadius";

		/// <summary>Environment key for the elevation / shadow depth (Dp).</summary>
		public const string ElevationKey = "Comet.Elevation";

		/// <summary>Rounds this view's corners (and clips its content/background) to <paramref name="radius"/> Dp.</summary>
		public static T CornerRadius<T>(this T view, double radius) where T : View
		{
			view.SetEnvironment(CornerRadiusKey, radius, false);
			return view;
		}

		/// <summary>Raises this view with a soft drop shadow of <paramref name="elevation"/> Dp
		/// (the Material card "lift"). Combine with <see cref="CornerRadius{T}"/> for a rounded card.</summary>
		public static T Elevation<T>(this T view, double elevation) where T : View
		{
			view.SetEnvironment(ElevationKey, elevation, false);
			return view;
		}
	}
}
