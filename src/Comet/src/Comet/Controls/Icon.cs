#nullable enable
namespace Comet
{
	/// <summary>
	/// A platform-vector icon. The <see cref="Symbol"/> is a cross-platform name (e.g.
	/// <c>"search"</c>, <c>"info"</c>, <c>"menu"</c>, <c>"send"</c>) that each backend maps to its
	/// native icon set — Compose <c>Icons.*</c> (<c>ImageVector</c>) and SwiftUI SF Symbols — so a
	/// Comet <c>Icon</c> renders as the real Material / SF icon rather than a glyph in a label.
	/// Tint with <c>.Color(...)</c> and size with <c>.IconSize(...)</c> (default 24dp).
	/// </summary>
	public partial class Icon : View
	{
		public Icon(string symbol) => Symbol = symbol;

		public string Symbol { get; }
	}

	public static class IconExtensions
	{
		/// <summary>Sets the icon's square size in Dp (default 24).</summary>
		/// <summary>The asset draws at the FRAME's size instead of the square
		/// <c>IconSize</c>-with-centering-slack rule — for non-square bundled vectors
		/// (JetNews' 80×24 wordmark). Frame the view to the asset's aspect.</summary>
		public static T IconFillFrame<T>(this T icon) where T : Icon
			=> icon.SetEnvironment("Comet.IconFillFrame", true, false);

		public static T IconSize<T>(this T icon, double size) where T : Icon
		{
			icon.SetEnvironment("Comet.IconSize", size, false);
			return icon;
		}
	}
}
