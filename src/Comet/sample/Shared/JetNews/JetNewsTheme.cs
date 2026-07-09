#nullable enable
using Microsoft.Maui.Graphics;

namespace CometSamples.JetNews
{
	/// <summary>
	/// JetNews theme. The gold app uses DYNAMIC color unconditionally on S+
	/// (Theme.kt:90-97), so the gold screenshots carry the capture emulator's
	/// wallpaper-derived Material You scheme. We regenerate that scheme with the SAME
	/// algorithm (material-color-utilities, the Jetchat generator path) from the seed
	/// sampled off the gold captures (tab indicator / primary ≈ #475D92), so both
	/// backends render the identical palette the golds show.
	/// </summary>
	public static class JetNewsTheme
	{
		/// <summary>Sampled from the gold captures (Interests tab indicator = primary).</summary>
		public static readonly Color Seed = Color.FromArgb("#475D92");

		static readonly MaterialColorUtilities.Schemes.Scheme<uint> S = BuildScheme();

		static MaterialColorUtilities.Schemes.Scheme<uint> BuildScheme()
		{
			var core = MaterialColorUtilities.Palettes.CorePalette.Of(ToArgbUint(Seed));
			return new MaterialColorUtilities.Schemes.LightSchemeMapper().Map(core);
		}

		public static readonly Color Primary = C(S.Primary);
		public static readonly Color OnPrimary = C(S.OnPrimary);
		public static readonly Color PrimaryContainer = C(S.PrimaryContainer);
		public static readonly Color OnPrimaryContainer = C(S.OnPrimaryContainer);
		public static readonly Color Secondary = C(S.Secondary);
		public static readonly Color SecondaryContainer = C(S.SecondaryContainer);
		public static readonly Color OnSecondaryContainer = C(S.OnSecondaryContainer);
		public static readonly Color Background = C(S.Background);
		public static readonly Color OnBackground = C(S.OnBackground);
		public static readonly Color Surface = C(S.Surface);
		public static readonly Color OnSurface = C(S.OnSurface);
		public static readonly Color SurfaceVariant = C(S.SurfaceVariant);
		public static readonly Color OnSurfaceVariant = C(S.OnSurfaceVariant);
		public static readonly Color Outline = C(S.Outline);
		public static readonly Color InverseOnSurface = C(S.InverseOnSurface);

		/// <summary>onSurface @ 12% — the gold's list dividers (DividerDefaults).</summary>
		public static readonly Color Divider = C(S.OnSurface).WithAlpha(0.12f);

#if ANDROID
		/// <summary>The same scheme as a Compose ColorScheme, so the REAL M3 widgets
		/// (TabRow, drawer sheets, snackbar) are seeded identically (the Reply pattern).</summary>
		public static AndroidX.Compose.Material3.ColorScheme ComposeScheme()
		{
			static AndroidX.Compose.Color C(Color c) => AndroidX.Compose.Color.FromArgb(
				(byte)(c.Alpha * 255), (byte)(c.Red * 255), (byte)(c.Green * 255), (byte)(c.Blue * 255));
			return AndroidX.Compose.MaterialTheme.LightColorScheme(
				primary: C(Primary), onPrimary: C(OnPrimary),
				primaryContainer: C(PrimaryContainer), onPrimaryContainer: C(OnPrimaryContainer),
				secondary: C(Secondary),
				secondaryContainer: C(SecondaryContainer), onSecondaryContainer: C(OnSecondaryContainer),
				background: C(Background), onBackground: C(OnBackground),
				surface: C(Surface), onSurface: C(OnSurface),
				surfaceVariant: C(SurfaceVariant), onSurfaceVariant: C(OnSurfaceVariant),
				inverseOnSurface: C(InverseOnSurface),
				outline: C(Outline));
		}
#endif

		static Color C(uint argb) => Color.FromUint(argb | 0xFF000000);
		static uint ToArgbUint(Color c) =>
			((uint)(c.Alpha * 255) << 24) | ((uint)(c.Red * 255) << 16) |
			((uint)(c.Green * 255) << 8) | (uint)(c.Blue * 255);
	}
}
