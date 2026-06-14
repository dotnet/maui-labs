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
		static readonly Color DarkBlue40 = Color.FromArgb("#3648EA");
		static readonly Color Yellow10 = Color.FromArgb("#261900");
		static readonly Color Yellow40 = Color.FromArgb("#7A5900");
		static readonly Color Yellow90 = Color.FromArgb("#FFDE9C");
		static readonly Color Grey10 = Color.FromArgb("#191C1D");
		static readonly Color Grey99 = Color.FromArgb("#FBFDFD");
		static readonly Color BlueGrey30 = Color.FromArgb("#45464F");
		static readonly Color BlueGrey50 = Color.FromArgb("#767680");
		static readonly Color BlueGrey90 = Color.FromArgb("#E2E1EC");

		// ── Themes.kt: semantic roles. Settable so ApplyScheme can swap in the platform's
		// Material You (dynamicLightColorScheme) at runtime; the values below are the static
		// JetchatLightColorScheme fallback (used pre-Android-12 / when dynamic is off). Consumers
		// must read these live (not cache in a static field) so the dynamic swap reaches them. ──
		public static Color Primary { get; private set; } = Blue40;
		public static Color OnPrimary { get; private set; } = Colors.White;
		public static Color PrimaryContainer { get; private set; } = Blue90;
		public static Color Secondary { get; private set; } = DarkBlue40;       // footer Surface contentColor
		public static Color Surface { get; private set; } = Grey99;
		public static Color OnSurface { get; private set; } = Grey10;
		public static Color SurfaceVariant { get; private set; } = BlueGrey90;
		public static Color OnSurfaceVariant { get; private set; } = BlueGrey30;
		public static Color Background { get; private set; } = Grey99;
		public static Color Tertiary { get; private set; } = Yellow40;
		public static Color Outline { get; private set; } = BlueGrey50;
		// The profile FAB roles (tertiaryContainer / onTertiaryContainer) — Themes.kt JetchatLightColorScheme.
		public static Color TertiaryContainer { get; private set; } = Yellow90;   // #FFDE9C (was sampled pink #F8D8F0)
		public static Color OnTertiaryContainer { get; private set; } = Yellow10; // #261900

		// The footer bar = Surface(tonalElevation = 2.dp): primary composited over surface at the M3
		// 2dp overlay alpha (≈0.0694). The HEADER (CenterAlignedTopAppBar) is plain Surface.
		public static Color SurfaceTinted { get; private set; } = SurfaceAtElevation(Blue40, Grey99, 2);
		public static Color Divider { get; private set; } = Grey10.WithAlpha(0.12f);   // onSurface @ 12%
		public static readonly Color Disabled = Color.FromArgb("#C4C6D0");

		/// <summary>Swap the semantic roles to a platform <c>ColorScheme</c> (Material You) at startup,
		/// mirroring <c>JetchatTheme(isDynamicColor = true)</c> in Themes.kt. Call BEFORE building the
		/// view tree. Derived roles (the tonal footer + the 12% divider) are recomputed from the new
		/// surface/primary/onSurface.</summary>
		public static void ApplyScheme(Color primary, Color onPrimary, Color primaryContainer, Color secondary,
			Color surface, Color onSurface, Color surfaceVariant, Color onSurfaceVariant, Color background,
			Color tertiary, Color tertiaryContainer, Color onTertiaryContainer, Color outline)
		{
			Primary = primary; OnPrimary = onPrimary; PrimaryContainer = primaryContainer; Secondary = secondary;
			Surface = surface; OnSurface = onSurface; SurfaceVariant = surfaceVariant; OnSurfaceVariant = onSurfaceVariant;
			Background = background; Tertiary = tertiary; TertiaryContainer = tertiaryContainer;
			OnTertiaryContainer = onTertiaryContainer; Outline = outline;
			SurfaceTinted = SurfaceAtElevation(primary, surface, 2);
			Divider = onSurface.WithAlpha(0.12f);
		}

		// Material 3 surfaceColorAtElevation: primary composited over surface at alpha = (4.5·ln(dp+1)+2)/100.
		static Color SurfaceAtElevation(Color primary, Color surface, double dp)
		{
			double a = (4.5 * System.Math.Log(dp + 1) + 2) / 100.0;
			return new Color(
				(float)(primary.Red * a + surface.Red * (1 - a)),
				(float)(primary.Green * a + surface.Green * (1 - a)),
				(float)(primary.Blue * a + surface.Blue * (1 - a)));
		}

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
