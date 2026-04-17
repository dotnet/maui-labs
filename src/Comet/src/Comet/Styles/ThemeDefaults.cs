using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using MauiColors = Microsoft.Maui.Graphics.Colors;

namespace Comet.Styles
{
	/// <summary>
	/// Built-in light and dark themes using Material 3 token values.
	/// </summary>
	public static class Defaults
	{
		public static Theme Light { get; } = new Theme
		{
			Name = "Light",
			Colors = LightColors,
			Typography = TypographyDefaults.Material3,
			Spacing = SpacingDefaults.Standard,
			Shapes = ShapeDefaults.Rounded,
		};

		public static Theme Dark { get; } = new Theme
		{
			Name = "Dark",
			Colors = DarkColors,
			Typography = TypographyDefaults.Material3,
			Spacing = SpacingDefaults.Standard,
			Shapes = ShapeDefaults.Rounded,
		};

		static readonly ColorTokenSet LightColors = new ColorTokenSet
		{
			Primary = Color.FromArgb("#6750A4"),
			OnPrimary = MauiColors.White,
			PrimaryContainer = Color.FromArgb("#EADDFF"),
			OnPrimaryContainer = Color.FromArgb("#21005D"),

			Secondary = Color.FromArgb("#625B71"),
			OnSecondary = MauiColors.White,
			SecondaryContainer = Color.FromArgb("#E8DEF8"),
			OnSecondaryContainer = Color.FromArgb("#1D192B"),

			Tertiary = Color.FromArgb("#7D5260"),
			OnTertiary = MauiColors.White,
			TertiaryContainer = Color.FromArgb("#FFD8E4"),
			OnTertiaryContainer = Color.FromArgb("#31111D"),

			Error = Color.FromArgb("#B3261E"),
			OnError = MauiColors.White,
			ErrorContainer = Color.FromArgb("#F9DEDC"),
			OnErrorContainer = Color.FromArgb("#410E0B"),

			Surface = Color.FromArgb("#FFFBFE"),
			OnSurface = Color.FromArgb("#1C1B1F"),
			SurfaceVariant = Color.FromArgb("#E7E0EC"),
			OnSurfaceVariant = Color.FromArgb("#49454F"),
			SurfaceContainer = Color.FromArgb("#F3EDF7"),
			SurfaceContainerLow = Color.FromArgb("#F7F2FA"),
			SurfaceContainerHigh = Color.FromArgb("#ECE6F0"),

			Background = Color.FromArgb("#FFFBFE"),
			OnBackground = Color.FromArgb("#1C1B1F"),

			Outline = Color.FromArgb("#79747E"),
			OutlineVariant = Color.FromArgb("#CAC4D0"),

			InverseSurface = Color.FromArgb("#313033"),
			InverseOnSurface = Color.FromArgb("#F4EFF4"),
			InversePrimary = Color.FromArgb("#D0BCFF"),
		};

		static readonly ColorTokenSet DarkColors = new ColorTokenSet
		{
			Primary = Color.FromArgb("#D0BCFF"),
			OnPrimary = Color.FromArgb("#381E72"),
			PrimaryContainer = Color.FromArgb("#4F378B"),
			OnPrimaryContainer = Color.FromArgb("#EADDFF"),

			Secondary = Color.FromArgb("#CCC2DC"),
			OnSecondary = Color.FromArgb("#332D41"),
			SecondaryContainer = Color.FromArgb("#4A4458"),
			OnSecondaryContainer = Color.FromArgb("#E8DEF8"),

			Tertiary = Color.FromArgb("#EFB8C8"),
			OnTertiary = Color.FromArgb("#492532"),
			TertiaryContainer = Color.FromArgb("#633B48"),
			OnTertiaryContainer = Color.FromArgb("#FFD8E4"),

			Error = Color.FromArgb("#F2B8B5"),
			OnError = Color.FromArgb("#601410"),
			ErrorContainer = Color.FromArgb("#8C1D18"),
			OnErrorContainer = Color.FromArgb("#F9DEDC"),

			Surface = Color.FromArgb("#1C1B1F"),
			OnSurface = Color.FromArgb("#E6E1E5"),
			SurfaceVariant = Color.FromArgb("#49454F"),
			OnSurfaceVariant = Color.FromArgb("#CAC4D0"),
			SurfaceContainer = Color.FromArgb("#211F26"),
			SurfaceContainerLow = Color.FromArgb("#1D1B20"),
			SurfaceContainerHigh = Color.FromArgb("#2B2930"),

			Background = Color.FromArgb("#1C1B1F"),
			OnBackground = Color.FromArgb("#E6E1E5"),

			Outline = Color.FromArgb("#938F99"),
			OutlineVariant = Color.FromArgb("#49454F"),

			InverseSurface = Color.FromArgb("#E6E1E5"),
			InverseOnSurface = Color.FromArgb("#313033"),
			InversePrimary = Color.FromArgb("#6750A4"),
		};
	}

	/// <summary>
	/// Default Material 3 typography definitions.
	/// </summary>
	public static class TypographyDefaults
	{
		public static readonly TypographyTokenSet Material3 = new TypographyTokenSet
		{
			DisplayLarge = new FontSpec(57, FontWeight.Regular, LineHeight: 64, LetterSpacing: -0.25),
			DisplayMedium = new FontSpec(45, FontWeight.Regular, LineHeight: 52),
			DisplaySmall = new FontSpec(36, FontWeight.Regular, LineHeight: 44),

			HeadlineLarge = new FontSpec(32, FontWeight.Regular, LineHeight: 40),
			HeadlineMedium = new FontSpec(28, FontWeight.Regular, LineHeight: 36),
			HeadlineSmall = new FontSpec(24, FontWeight.Regular, LineHeight: 32),

			TitleLarge = new FontSpec(22, FontWeight.Regular, LineHeight: 28),
			TitleMedium = new FontSpec(16, FontWeight.Medium, LineHeight: 24, LetterSpacing: 0.15),
			TitleSmall = new FontSpec(14, FontWeight.Medium, LineHeight: 20, LetterSpacing: 0.1),

			BodyLarge = new FontSpec(16, FontWeight.Regular, LineHeight: 24, LetterSpacing: 0.5),
			BodyMedium = new FontSpec(14, FontWeight.Regular, LineHeight: 20, LetterSpacing: 0.25),
			BodySmall = new FontSpec(12, FontWeight.Regular, LineHeight: 16, LetterSpacing: 0.4),

			LabelLarge = new FontSpec(14, FontWeight.Medium, LineHeight: 20, LetterSpacing: 0.1),
			LabelMedium = new FontSpec(12, FontWeight.Medium, LineHeight: 16, LetterSpacing: 0.5),
			LabelSmall = new FontSpec(11, FontWeight.Medium, LineHeight: 16, LetterSpacing: 0.5),
		};
	}

	/// <summary>
	/// Default Material 3 spacing definitions.
	/// </summary>
	public static class SpacingDefaults
	{
		public static readonly SpacingTokenSet Standard = new SpacingTokenSet
		{
			None = 0,
			ExtraSmall = 4,
			Small = 8,
			Medium = 16,
			Large = 24,
			ExtraLarge = 32,
		};
	}

	/// <summary>
	/// Default Material 3 shape (corner radius) definitions.
	/// </summary>
	public static class ShapeDefaults
	{
		public static readonly ShapeTokenSet Rounded = new ShapeTokenSet
		{
			None = 0,
			ExtraSmall = 4,
			Small = 8,
			Medium = 12,
			Large = 16,
			ExtraLarge = 28,
			Full = 9999,
		};
	}
}
