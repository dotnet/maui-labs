#nullable enable
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace CometSamples.Jetcaster
{
	/// <summary>
	/// The gold's theme, values-from-source. Mobile ALWAYS renders the static dark
	/// scheme (Theme.kt:478 — dynamicColor defaults false and is never overridden),
	/// so only darkScheme tokens are ported. Typography = Type.kt: RobotoFlex for
	/// display styles (variable font, displayLarge weight 738), Montserrat W400/W500
	/// for everything else.
	/// </summary>
	public static class JetcasterTheme
	{
		// ── darkScheme tokens (mobile/ui/theme via core/designsystem Color.kt) ──
		public static readonly Color Primary = Color.FromArgb("#F0FCB0");
		public static readonly Color OnPrimary = Color.FromArgb("#626004");
		public static readonly Color PrimaryContainer = Color.FromArgb("#313002");
		public static readonly Color OnPrimaryContainer = Color.FromArgb("#FDFCCE");
		public static readonly Color Secondary = Color.FromArgb("#FFE523");
		public static readonly Color OnSecondary = Color.FromArgb("#332D00");
		public static readonly Color SecondaryContainer = Color.FromArgb("#998700");
		public static readonly Color OnSecondaryContainer = Color.FromArgb("#FFF9CC");
		public static readonly Color Tertiary = Color.FromArgb("#FF9AD8");
		public static readonly Color OnTertiary = Color.FromArgb("#33000A");
		public static readonly Color TertiaryContainer = Color.FromArgb("#660014");
		public static readonly Color OnTertiaryContainer = Color.FromArgb("#FFE5EB");
		public static readonly Color Background = Color.FromArgb("#151218");
		public static readonly Color OnBackground = Color.FromArgb("#E7E0E8");
		public static readonly Color Surface = Color.FromArgb("#261604");
		public static readonly Color OnSurface = Color.FromArgb("#FBEDE4");
		public static readonly Color SurfaceVariant = Color.FromArgb("#49454E");
		public static readonly Color OnSurfaceVariant = Color.FromArgb("#CBC4CF");
		public static readonly Color Outline = Color.FromArgb("#948F99");
		public static readonly Color OutlineVariant = Color.FromArgb("#49454E");
		public static readonly Color SurfaceDim = Color.FromArgb("#19120C");
		public static readonly Color SurfaceBright = Color.FromArgb("#413731");
		public static readonly Color SurfaceContainerLowest = Color.FromArgb("#140D08");
		public static readonly Color SurfaceContainerLow = Color.FromArgb("#221A14");
		public static readonly Color SurfaceContainer = Color.FromArgb("#261E18");
		public static readonly Color SurfaceContainerHigh = Color.FromArgb("#312822");
		public static readonly Color SurfaceContainerHighest = Color.FromArgb("#3C332C");
		public static readonly Color Error = Color.FromArgb("#FFB4AB");
		public static readonly Color OnError = Color.FromArgb("#690005");
		public static readonly Color ErrorContainer = Color.FromArgb("#93000A");
		public static readonly Color OnErrorContainer = Color.FromArgb("#FFDAD6");
		public static readonly Color InversePrimary = Color.FromArgb("#68548E");
		public static readonly Color InverseSurface = Color.FromArgb("#E7E0E8");
		public static readonly Color InverseOnSurface = Color.FromArgb("#322F35");
		public static readonly Color Scrim = Colors.Black;

#if ANDROID
		/// <summary>The gold's static darkScheme as a real Compose ColorScheme, so the
		/// REAL M3 widgets (FilterChip, SearchBar, carousels) self-theme identically.</summary>
		public static AndroidX.Compose.Material3.ColorScheme ComposeScheme()
		{
			static AndroidX.Compose.Color C(Color c) => AndroidX.Compose.Color.FromArgb(
				(byte)(c.Alpha * 255), (byte)(c.Red * 255), (byte)(c.Green * 255), (byte)(c.Blue * 255));
			return AndroidX.Compose.MaterialTheme.DarkColorScheme(
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

		// ── Type.kt (JetcasterTypography) — line heights ride the sp values below ──
		public static Comet.Text DisplayLarge(string s) => (Comet.Text)new Comet.Text(s)
			.FontFamily("RobotoFlex").FontWeight((FontWeight)738).FontSize(64).LineHeight(56);
		public static Comet.Text DisplayMedium(string s) => (Comet.Text)new Comet.Text(s)
			.FontFamily("RobotoFlex").FontSize(45).LineHeight(52);
		public static Comet.Text HeadlineMedium(string s) => (Comet.Text)new Comet.Text(s)
			.FontFamily("Montserrat").FontWeight(FontWeight.Medium).FontSize(28).LineHeight(36);
		public static Comet.Text HeadlineSmall(string s) => (Comet.Text)new Comet.Text(s)
			.FontFamily("Montserrat").FontWeight(FontWeight.Medium).FontSize(24).LineHeight(32);
		public static Comet.Text TitleLarge(string s) => (Comet.Text)new Comet.Text(s)
			.FontFamily("Montserrat").FontSize(22).LineHeight(28);
		public static Comet.Text TitleMedium(string s) => (Comet.Text)new Comet.Text(s)
			.FontFamily("Montserrat").FontWeight(FontWeight.Medium).FontSize(16).LineHeight(24);
		public static Comet.Text TitleSmall(string s) => (Comet.Text)new Comet.Text(s)
			.FontFamily("Montserrat").FontWeight(FontWeight.Medium).FontSize(14).LineHeight(20);
		public static Comet.Text LabelLarge(string s) => (Comet.Text)new Comet.Text(s)
			.FontFamily("Montserrat").FontWeight(FontWeight.Medium).FontSize(14).LineHeight(20);
		public static Comet.Text LabelMedium(string s) => (Comet.Text)new Comet.Text(s)
			.FontFamily("Montserrat").FontWeight(FontWeight.Medium).FontSize(12).LineHeight(16);
		public static Comet.Text BodyLarge(string s) => (Comet.Text)new Comet.Text(s)
			.FontFamily("Montserrat").FontWeight(FontWeight.Medium).FontSize(16).LineHeight(24);
		public static Comet.Text BodyMedium(string s) => (Comet.Text)new Comet.Text(s)
			.FontFamily("Montserrat").FontWeight(FontWeight.Medium).FontSize(14).LineHeight(20);
		public static Comet.Text BodySmall(string s) => (Comet.Text)new Comet.Text(s)
			.FontFamily("Montserrat").FontWeight(FontWeight.Medium).FontSize(12).LineHeight(16);
	}
}
