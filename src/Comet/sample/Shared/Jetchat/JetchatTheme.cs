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

		/// <summary>The Jetchat brand seed (Blue40). Both platforms generate their Material 3 scheme
		/// from THIS color via <see cref="ApplyDynamicScheme"/>, so the look is theme-driven and identical
		/// cross-platform (the gold seeds from the device wallpaper, which no iOS app can read).</summary>
		public static readonly Color SeedColor = Color.FromArgb("#1546F6");

		/// <summary>Generate a full Material 3 tonal scheme from a single <paramref name="seed"/> using
		/// Google's material-color-utilities algorithm (HCT → tonal palettes → roles — the SAME math
		/// Android's Material You uses) and apply it. The cross-platform "dynamic color" path: seed from
		/// the brand color (consistent) or from content (a photo). Call BEFORE building the view tree.
		/// Returns the role colors so a platform can also feed them to its native theme (Compose MaterialTheme).</summary>
		public static MaterialColorUtilities.Schemes.Scheme<uint> ApplyDynamicScheme(Color seed, bool dark = false)
		{
			var core = MaterialColorUtilities.Palettes.CorePalette.Of(ToArgbUint(seed));
			MaterialColorUtilities.Schemes.Scheme<uint> s = dark
				? new MaterialColorUtilities.Schemes.DarkSchemeMapper().Map(core)
				: new MaterialColorUtilities.Schemes.LightSchemeMapper().Map(core);
			ApplyScheme(
				primary: FromArgbUint(s.Primary), onPrimary: FromArgbUint(s.OnPrimary),
				primaryContainer: FromArgbUint(s.PrimaryContainer), secondary: FromArgbUint(s.Secondary),
				surface: FromArgbUint(s.Surface), onSurface: FromArgbUint(s.OnSurface),
				surfaceVariant: FromArgbUint(s.SurfaceVariant), onSurfaceVariant: FromArgbUint(s.OnSurfaceVariant),
				background: FromArgbUint(s.Background), tertiary: FromArgbUint(s.Tertiary),
				tertiaryContainer: FromArgbUint(s.TertiaryContainer), onTertiaryContainer: FromArgbUint(s.OnTertiaryContainer),
				outline: FromArgbUint(s.Outline));
			return s;
		}

		/// <summary>Content-based theming: extract a Material You seed from image pixels (AARRGGBB) via
		/// material-color-utilities' Quantize + Score, then apply the generated scheme — e.g. theme the
		/// whole app from a profile photo, the same content-based mode Material You offers. Falls back to
		/// the brand <see cref="SeedColor"/> when no usable seed is found.</summary>
		public static MaterialColorUtilities.Schemes.Scheme<uint> ApplyDynamicSchemeFromPixels(uint[] argbPixels, bool dark = false)
		{
			var seeds = MaterialColorUtilities.Utils.ImageUtils.ColorsFromImage(argbPixels);
			return ApplyDynamicScheme(seeds.Count > 0 ? FromArgbUint(seeds[0]) : SeedColor, dark);
		}

		// material-color-utilities works in AARRGGBB uints; convert to/from Maui colors.
		static uint ToArgbUint(Color c) =>
			((uint)System.Math.Round(c.Alpha * 255) << 24) | ((uint)System.Math.Round(c.Red * 255) << 16) |
			((uint)System.Math.Round(c.Green * 255) << 8) | (uint)System.Math.Round(c.Blue * 255);

		public static Color FromArgbUint(uint argb) => new Color(
			((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f, ((argb >> 24) & 0xFF) / 255f);

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
		// Each style carries the EXACT size + lineHeight from Typography.kt (not a 1.3× heuristic). ──
		const string Montserrat = "Montserrat";
		const string Karla = "Karla";

		public static T HeadlineSmall<T>(this T v) where T : View => v.Type(Montserrat, FontWeight.Semibold, 24, 32);
		public static T TitleLarge<T>(this T v) where T : View => v.Type(Montserrat, FontWeight.Semibold, 22, 28);
		public static T TitleMedium<T>(this T v) where T : View => v.Type(Montserrat, FontWeight.Semibold, 16, 24);
		public static T BodyLarge<T>(this T v) where T : View => v.Type(Karla, FontWeight.Regular, 16, 24);
		public static T BodyMedium<T>(this T v) where T : View => v.Type(Montserrat, FontWeight.Medium, 14, 20);
		public static T BodySmall<T>(this T v) where T : View => v.Type(Karla, FontWeight.Bold, 12, 16);
		public static T LabelLarge<T>(this T v) where T : View => v.Type(Montserrat, FontWeight.Semibold, 14, 20);
		public static T LabelSmall<T>(this T v) where T : View => v.Type(Montserrat, FontWeight.Semibold, 11, 16);

		static T Type<T>(this T v, string family, FontWeight weight, double size, double lineHeight) where T : View =>
			v.FontFamily(family).FontWeight(weight).FontSize(size).LineHeight(lineHeight);

		// ── Shapes ──
		// The chat bubble (RoundedCornerShape(4,20,20,20)); apply with .CornerRadius(BubbleTL,…).
		public const double BubbleTopStart = 4, BubbleOther = 20;
	}
}
