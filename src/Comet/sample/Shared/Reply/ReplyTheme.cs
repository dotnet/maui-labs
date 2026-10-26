#nullable enable
using Microsoft.Maui.Graphics;

namespace CometSamples.Reply
{
	/// <summary>Reply's static light scheme — every value a literal from the gold's
	/// ui/theme/Color.kt (ContrastAwareReplyTheme defaults dynamicColor=false, default
	/// contrast → lightScheme). Deterministic: fidelity compares exact hex, no seed
	/// generation. Dark + contrast variants land at the fidelity pass if needed.</summary>
	public static class ReplyTheme
	{
		public static readonly Color Primary = Color.FromArgb("#805610");
		public static readonly Color OnPrimary = Color.FromArgb("#FFFFFF");
		public static readonly Color PrimaryContainer = Color.FromArgb("#FFDDB3");
		public static readonly Color OnPrimaryContainer = Color.FromArgb("#291800");
		public static readonly Color Secondary = Color.FromArgb("#6F5B40");
		public static readonly Color OnSecondary = Color.FromArgb("#FFFFFF");
		public static readonly Color SecondaryContainer = Color.FromArgb("#FBDEBC");
		public static readonly Color OnSecondaryContainer = Color.FromArgb("#271904");
		public static readonly Color Tertiary = Color.FromArgb("#51643F");
		public static readonly Color OnTertiary = Color.FromArgb("#FFFFFF");
		public static readonly Color TertiaryContainer = Color.FromArgb("#D4EABB");
		public static readonly Color OnTertiaryContainer = Color.FromArgb("#102004");
		public static readonly Color Error = Color.FromArgb("#BA1A1A");
		public static readonly Color OnError = Color.FromArgb("#FFFFFF");
		public static readonly Color ErrorContainer = Color.FromArgb("#FFDAD6");
		public static readonly Color OnErrorContainer = Color.FromArgb("#410002");
		public static readonly Color Background = Color.FromArgb("#FFF8F4");
		public static readonly Color OnBackground = Color.FromArgb("#201B13");
		public static readonly Color Surface = Color.FromArgb("#FFF8F4");
		public static readonly Color OnSurface = Color.FromArgb("#201B13");
		public static readonly Color SurfaceVariant = Color.FromArgb("#F0E0CF");
		public static readonly Color OnSurfaceVariant = Color.FromArgb("#4F4539");
		public static readonly Color Outline = Color.FromArgb("#817567");
		public static readonly Color OutlineVariant = Color.FromArgb("#D3C4B4");
		public static readonly Color Scrim = Color.FromArgb("#000000");
		public static readonly Color InverseSurface = Color.FromArgb("#362F27");
		public static readonly Color InverseOnSurface = Color.FromArgb("#FCEFE2");
		public static readonly Color InversePrimary = Color.FromArgb("#F4BD6F");
		public static readonly Color SurfaceDim = Color.FromArgb("#E4D8CC");
		public static readonly Color SurfaceBright = Color.FromArgb("#FFF8F4");
		public static readonly Color SurfaceContainerLowest = Color.FromArgb("#FFFFFF");
		public static readonly Color SurfaceContainerLow = Color.FromArgb("#FFF1E5");
		public static readonly Color SurfaceContainer = Color.FromArgb("#F9ECDF");
		public static readonly Color SurfaceContainerHigh = Color.FromArgb("#F3E6DA");
		public static readonly Color SurfaceContainerHighest = Color.FromArgb("#EDE0D4");

#if ANDROID
		/// <summary>The matching Compose <c>ColorScheme</c>, so the REAL Material widgets
		/// (NavigationBar/Rail, drawer sheets, ripples) render Reply's palette — the gold's
		/// M3 defaults derive from these roles (e.g. the nav bar container is
		/// surfaceContainer, the selection pill secondaryContainer).</summary>
		public static AndroidX.Compose.Material3.ColorScheme ComposeScheme()
		{
			static AndroidX.Compose.Color C(Color c) => AndroidX.Compose.Color.FromArgb(
				(byte)(c.Alpha * 255), (byte)(c.Red * 255), (byte)(c.Green * 255), (byte)(c.Blue * 255));
			return AndroidX.Compose.MaterialTheme.LightColorScheme(
				primary: C(Primary), onPrimary: C(OnPrimary),
				primaryContainer: C(PrimaryContainer), onPrimaryContainer: C(OnPrimaryContainer),
				inversePrimary: C(InversePrimary),
				secondary: C(Secondary), onSecondary: C(OnSecondary),
				secondaryContainer: C(SecondaryContainer), onSecondaryContainer: C(OnSecondaryContainer),
				tertiary: C(Tertiary), onTertiary: C(OnTertiary),
				tertiaryContainer: C(TertiaryContainer), onTertiaryContainer: C(OnTertiaryContainer),
				background: C(Background), onBackground: C(OnBackground),
				surface: C(Surface), onSurface: C(OnSurface),
				surfaceVariant: C(SurfaceVariant), onSurfaceVariant: C(OnSurfaceVariant),
				inverseSurface: C(InverseSurface), inverseOnSurface: C(InverseOnSurface),
				error: C(Error), onError: C(OnError),
				errorContainer: C(ErrorContainer), onErrorContainer: C(OnErrorContainer),
				outline: C(Outline), outlineVariant: C(OutlineVariant),
				scrim: C(Scrim),
				surfaceBright: C(SurfaceBright), surfaceDim: C(SurfaceDim),
				surfaceContainer: C(SurfaceContainer),
				surfaceContainerHigh: C(SurfaceContainerHigh),
				surfaceContainerHighest: C(SurfaceContainerHighest),
				surfaceContainerLow: C(SurfaceContainerLow),
				surfaceContainerLowest: C(SurfaceContainerLowest));
		}
#endif
	}
}
