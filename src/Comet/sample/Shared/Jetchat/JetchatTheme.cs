#nullable enable
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace CometSamples.Jetchat
{
	/// <summary>
	/// The Jetchat design tokens, mirroring the sample's <c>theme/</c> package
	/// (Color.kt / Themes.kt / Typography.kt) as a single source of truth: the raw palette,
	/// the semantic color roles (the light <c>ColorScheme</c>), the type scale, and the shapes.
	/// Components read these instead of hardcoding values inline — the C# analog of
	/// <c>MaterialTheme.colorScheme / typography / shapes</c>.
	/// </summary>
	static class JetchatTheme
	{
		// ── Color.kt: raw palette ──
		static readonly Color Blue40 = Color.FromArgb("#1546F6");
		static readonly Color Blue80 = Color.FromArgb("#B8C3FF");
		static readonly Color Blue90 = Color.FromArgb("#DDE1FF");
		static readonly Color Yellow40 = Color.FromArgb("#7A5900");
		static readonly Color Grey10 = Color.FromArgb("#191C1D");
		static readonly Color Grey99 = Color.FromArgb("#FBFDFD");
		static readonly Color BlueGrey30 = Color.FromArgb("#45464F");
		static readonly Color BlueGrey50 = Color.FromArgb("#767680");
		static readonly Color BlueGrey90 = Color.FromArgb("#E2E1EC");

		// ── Themes.kt: LightColorScheme (semantic roles) ──
		public static readonly Color Primary = Blue40;
		public static readonly Color OnPrimary = Colors.White;
		public static readonly Color PrimaryContainer = Blue90;
		public static readonly Color Surface = Grey99;
		public static readonly Color OnSurface = Grey10;
		public static readonly Color SurfaceVariant = BlueGrey90;
		public static readonly Color OnSurfaceVariant = BlueGrey30;
		public static readonly Color Background = Grey99;
		public static readonly Color Tertiary = Yellow40;
		public static readonly Color Outline = BlueGrey50;
		// The profile FAB's tertiaryContainer (light pink in the sampled build) + its content color.
		public static readonly Color TertiaryContainer = Color.FromArgb("#F8D8F0");
		public static readonly Color OnTertiaryContainer = Color.FromArgb("#3A2A33");

		// Surface at tonal elevation (the header/footer bars) + a 12% divider + disabled grey.
		public static readonly Color SurfaceTinted = Color.FromArgb("#E7E9F8");
		public static readonly Color Divider = Color.FromArgb("#1F191C1D");
		public static readonly Color Disabled = Color.FromArgb("#C4C6D0");

		// ── Typography.kt: Jetchat uses Montserrat (titles/labels) + Karla (body), NOT Roboto.
		// FontFamily names are applied once those fonts are bundled (see note); size/weight/line-
		// height already match the scale so the layout is correct. ──
		const string Montserrat = "Montserrat";
		const string Karla = "Karla";

		public static T HeadlineSmall<T>(this T v) where T : View => v.Type(Montserrat, FontWeight.Semibold, 24);
		public static T TitleLarge<T>(this T v) where T : View => v.Type(Montserrat, FontWeight.Semibold, 22);
		public static T TitleMedium<T>(this T v) where T : View => v.Type(Montserrat, FontWeight.Semibold, 16);
		public static T BodyLarge<T>(this T v) where T : View => v.Type(Karla, FontWeight.Regular, 16);
		public static T BodyMedium<T>(this T v) where T : View => v.Type(Montserrat, FontWeight.Medium, 14);
		public static T BodySmall<T>(this T v) where T : View => v.Type(Karla, FontWeight.Bold, 12);
		public static T LabelLarge<T>(this T v) where T : View => v.Type(Montserrat, FontWeight.Semibold, 14);
		public static T LabelSmall<T>(this T v) where T : View => v.Type(Montserrat, FontWeight.Semibold, 11);

		static T Type<T>(this T v, string family, FontWeight weight, double size) where T : View =>
			v.FontFamily(family).FontWeight(weight).FontSize(size);

		// ── Shapes ──
		// The chat bubble (RoundedCornerShape(4,20,20,20)); apply with .CornerRadius(BubbleTL,…).
		public const double BubbleTopStart = 4, BubbleOther = 20;
	}
}
