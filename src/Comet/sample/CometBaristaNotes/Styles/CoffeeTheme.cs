using System.Runtime.CompilerServices;
using Microsoft.Maui.ApplicationModel;

namespace CometBaristaNotes.Styles;

/// <summary>
/// App-specific color tokens that extend the Material 3 set.
/// These resolve from a <see cref="CoffeeThemeData"/> attached to the theme.
/// </summary>
public static class CoffeeTokens
{
	// App-specific color roles not in Material 3
	public static readonly Token<Color> SurfaceElevated =
		new("coffee.color.surfaceElevated",
			theme => GetData(theme)?.SurfaceElevated,
			"Surface Elevated",
			Color.FromArgb("#FFF7EC"));

	public static readonly Token<Color> TextPrimary =
		new("coffee.color.textPrimary",
			theme => GetData(theme)?.TextPrimary,
			"Text Primary",
			Color.FromArgb("#352B23"));

	public static readonly Token<Color> TextSecondary =
		new("coffee.color.textSecondary",
			theme => GetData(theme)?.TextSecondary,
			"Text Secondary",
			Color.FromArgb("#7C7067"));

	public static readonly Token<Color> TextMuted =
		new("coffee.color.textMuted",
			theme => GetData(theme)?.TextMuted,
			"Text Muted",
			Color.FromArgb("#A38F7D"));

	public static readonly Token<Color> Success =
		new("coffee.color.success",
			theme => GetData(theme)?.Success,
			"Success",
			Color.FromArgb("#4CAF50"));

	public static readonly Token<Color> Warning =
		new("coffee.color.warning",
			theme => GetData(theme)?.Warning,
			"Warning",
			Color.FromArgb("#FFA726"));

	public static readonly Token<Color> Error =
		new("coffee.color.error",
			theme => GetData(theme)?.Error,
			"Error",
			Color.FromArgb("#EF5350"));

	public static readonly Token<Color> Info =
		new("coffee.color.info",
			theme => GetData(theme)?.Info,
			"Info",
			Color.FromArgb("#42A5F5"));

	// Spacing: XXL is app-specific (framework only goes to ExtraLarge=32)
	public static readonly Token<double> SpacingXXL =
		new("coffee.spacing.xxl",
			theme => GetData(theme)?.SpacingXXL ?? 48,
			"Spacing XXL",
			48);

	// Font families
	public static readonly Token<string> FontRegular =
		new("coffee.font.regular",
			theme => GetData(theme)?.FontRegular,
			"Font Regular",
			"Manrope");

	public static readonly Token<string> FontSemibold =
		new("coffee.font.semibold",
			theme => GetData(theme)?.FontSemibold,
			"Font Semibold",
			"ManropeSemibold");

	static CoffeeThemeData GetData(Theme theme)
		=> CoffeeTheme.GetThemeData(theme);
}

/// <summary>
/// Extra data bag attached to each coffee-themed Theme instance.
/// Holds values that don't map to Material 3 token sets.
/// </summary>
public class CoffeeThemeData
{
	// App-specific colors
	public Color SurfaceElevated { get; init; }
	public Color TextPrimary { get; init; }
	public Color TextSecondary { get; init; }
	public Color TextMuted { get; init; }
	public Color Success { get; init; }
	public Color Warning { get; init; }
	public Color Error { get; init; }
	public Color Info { get; init; }

	// App-specific spacing
	public double SpacingXXL { get; init; }

	// Font families
	public string FontRegular { get; init; }
	public string FontSemibold { get; init; }
}

/// <summary>
/// Creates and manages coffee-themed light and dark Theme instances
/// wired to the Comet token system.
/// </summary>
public static class CoffeeTheme
{
	static readonly ConditionalWeakTable<Theme, CoffeeThemeData> _themeData = new();

	internal static CoffeeThemeData GetThemeData(Theme theme)
	{
		if (theme == null)
			return null;
		_themeData.TryGetValue(theme, out var data);
		return data;
	}

	static Theme _light;
	static Theme _dark;

	public static Theme Light => _light ??= CreateLight();
	public static Theme Dark => _dark ??= CreateDark();

	static Theme CreateLight()
	{
		var theme = new Theme
		{
			Name = "Coffee Light",

			// Material 3 color token set
			Colors = new ColorTokenSet
			{
				Primary = Color.FromArgb("#86543F"),
				OnPrimary = Color.FromArgb("#F8F6F4"),
				PrimaryContainer = Color.FromArgb("#ECDAC4"),
				OnPrimaryContainer = Color.FromArgb("#352B23"),

				Secondary = Color.FromArgb("#7C7067"),
				OnSecondary = Color.FromArgb("#F8F6F4"),
				SecondaryContainer = Color.FromArgb("#ECDAC4"),
				OnSecondaryContainer = Color.FromArgb("#352B23"),

				Tertiary = Color.FromArgb("#A38F7D"),
				OnTertiary = Color.FromArgb("#F8F6F4"),
				TertiaryContainer = Color.FromArgb("#FFF7EC"),
				OnTertiaryContainer = Color.FromArgb("#352B23"),

				Error = Color.FromArgb("#EF5350"),
				OnError = Color.FromArgb("#F8F6F4"),
				ErrorContainer = Color.FromArgb("#FDECEA"),
				OnErrorContainer = Color.FromArgb("#5F2120"),

				Surface = Color.FromArgb("#FCEFE1"),
				OnSurface = Color.FromArgb("#352B23"),
				SurfaceVariant = Color.FromArgb("#ECDAC4"),
				OnSurfaceVariant = Color.FromArgb("#7C7067"),
				SurfaceContainer = Color.FromArgb("#FCEFE1"),
				SurfaceContainerLow = Color.FromArgb("#FFF7EC"),
				SurfaceContainerHigh = Color.FromArgb("#ECDAC4"),

				Background = Color.FromArgb("#D2BCA5"),
				OnBackground = Color.FromArgb("#352B23"),

				Outline = Color.FromArgb("#D7C5B2"),
				OutlineVariant = Color.FromArgb("#ECDAC4"),

				InverseSurface = Color.FromArgb("#48362E"),
				InverseOnSurface = Color.FromArgb("#F8F6F4"),
				InversePrimary = Color.FromArgb("#D2BCA5"),
			},


			// Typography with Manrope font and app-specific sizes
			Typography = new TypographyTokenSet
			{
				DisplayLarge = new FontSpec(36, FontWeight.Regular, "Manrope", LineHeight: 44),
				DisplayMedium = new FontSpec(36, FontWeight.Regular, "Manrope", LineHeight: 44),
				DisplaySmall = new FontSpec(36, FontWeight.Regular, "Manrope", LineHeight: 44),

				HeadlineLarge = new FontSpec(28, FontWeight.Semibold, "ManropeSemibold", LineHeight: 36),
				HeadlineMedium = new FontSpec(28, FontWeight.Semibold, "ManropeSemibold", LineHeight: 36),
				HeadlineSmall = new FontSpec(22, FontWeight.Semibold, "ManropeSemibold", LineHeight: 28),

				TitleLarge = new FontSpec(22, FontWeight.Semibold, "ManropeSemibold", LineHeight: 28),
				TitleMedium = new FontSpec(18, FontWeight.Medium, "Manrope", LineHeight: 24),
				TitleSmall = new FontSpec(16, FontWeight.Medium, "Manrope", LineHeight: 22),

				BodyLarge = new FontSpec(18, FontWeight.Regular, "Manrope", LineHeight: 24),
				BodyMedium = new FontSpec(16, FontWeight.Regular, "Manrope", LineHeight: 22),
				BodySmall = new FontSpec(14, FontWeight.Regular, "Manrope", LineHeight: 20),

				LabelLarge = new FontSpec(14, FontWeight.Medium, "ManropeSemibold", LineHeight: 20),
				LabelMedium = new FontSpec(12, FontWeight.Medium, "ManropeSemibold", LineHeight: 16),
				LabelSmall = new FontSpec(12, FontWeight.Medium, "ManropeSemibold", LineHeight: 16),
			},

			// Spacing uses standard Material scale (matches our XS=4..XL=32)
			Spacing = new SpacingTokenSet
			{
				None = 0,
				ExtraSmall = 4,
				Small = 8,
				Medium = 16,
				Large = 24,
				ExtraLarge = 32,
			},

			Shapes = new ShapeTokenSet
			{
				None = 0,
				ExtraSmall = 4,
				Small = 8,
				Medium = 12,
				Large = 16,
				ExtraLarge = 25,
				Full = 999,
			},
		};

		_themeData.AddOrUpdate(theme, new CoffeeThemeData
		{
			SurfaceElevated = Color.FromArgb("#FFF7EC"),
			TextPrimary = Color.FromArgb("#352B23"),
			TextSecondary = Color.FromArgb("#7C7067"),
			TextMuted = Color.FromArgb("#A38F7D"),
			Success = Color.FromArgb("#4CAF50"),
			Warning = Color.FromArgb("#FFA726"),
			Error = Color.FromArgb("#EF5350"),
			Info = Color.FromArgb("#42A5F5"),
			SpacingXXL = 48,
			FontRegular = "Manrope",
			FontSemibold = "ManropeSemibold",
		});

		return theme;
	}

	static Theme CreateDark()
	{
		var theme = new Theme
		{
			Name = "Coffee Dark",

			// Material 3 color token set
			Colors = new ColorTokenSet
			{
				Primary = Color.FromArgb("#86543F"),
				OnPrimary = Color.FromArgb("#F8F6F4"),
				PrimaryContainer = Color.FromArgb("#7D5A45"),
				OnPrimaryContainer = Color.FromArgb("#F8F6F4"),

				Secondary = Color.FromArgb("#C5BFBB"),
				OnSecondary = Color.FromArgb("#48362E"),
				SecondaryContainer = Color.FromArgb("#7D5A45"),
				OnSecondaryContainer = Color.FromArgb("#F8F6F4"),

				Tertiary = Color.FromArgb("#A19085"),
				OnTertiary = Color.FromArgb("#48362E"),
				TertiaryContainer = Color.FromArgb("#5A463B"),
				OnTertiaryContainer = Color.FromArgb("#F8F6F4"),

				Error = Color.FromArgb("#EF5350"),
				OnError = Color.FromArgb("#F8F6F4"),
				ErrorContainer = Color.FromArgb("#5F2120"),
				OnErrorContainer = Color.FromArgb("#FDECEA"),

				Surface = Color.FromArgb("#48362E"),
				OnSurface = Color.FromArgb("#F8F6F4"),
				SurfaceVariant = Color.FromArgb("#7D5A45"),
				OnSurfaceVariant = Color.FromArgb("#C5BFBB"),
				SurfaceContainer = Color.FromArgb("#48362E"),
				SurfaceContainerLow = Color.FromArgb("#3A2C24"),
				SurfaceContainerHigh = Color.FromArgb("#5A463B"),

				Background = Color.FromArgb("#48362E"),
				OnBackground = Color.FromArgb("#F8F6F4"),

				Outline = Color.FromArgb("#5A463B"),
				OutlineVariant = Color.FromArgb("#7D5A45"),

				InverseSurface = Color.FromArgb("#FCEFE1"),
				InverseOnSurface = Color.FromArgb("#352B23"),
				InversePrimary = Color.FromArgb("#86543F"),
			},


			// Same Manrope typography as light theme
			Typography = new TypographyTokenSet
			{
				DisplayLarge = new FontSpec(36, FontWeight.Regular, "Manrope", LineHeight: 44),
				DisplayMedium = new FontSpec(36, FontWeight.Regular, "Manrope", LineHeight: 44),
				DisplaySmall = new FontSpec(36, FontWeight.Regular, "Manrope", LineHeight: 44),

				HeadlineLarge = new FontSpec(28, FontWeight.Semibold, "ManropeSemibold", LineHeight: 36),
				HeadlineMedium = new FontSpec(28, FontWeight.Semibold, "ManropeSemibold", LineHeight: 36),
				HeadlineSmall = new FontSpec(22, FontWeight.Semibold, "ManropeSemibold", LineHeight: 28),

				TitleLarge = new FontSpec(22, FontWeight.Semibold, "ManropeSemibold", LineHeight: 28),
				TitleMedium = new FontSpec(18, FontWeight.Medium, "Manrope", LineHeight: 24),
				TitleSmall = new FontSpec(16, FontWeight.Medium, "Manrope", LineHeight: 22),

				BodyLarge = new FontSpec(18, FontWeight.Regular, "Manrope", LineHeight: 24),
				BodyMedium = new FontSpec(16, FontWeight.Regular, "Manrope", LineHeight: 22),
				BodySmall = new FontSpec(14, FontWeight.Regular, "Manrope", LineHeight: 20),

				LabelLarge = new FontSpec(14, FontWeight.Medium, "ManropeSemibold", LineHeight: 20),
				LabelMedium = new FontSpec(12, FontWeight.Medium, "ManropeSemibold", LineHeight: 16),
				LabelSmall = new FontSpec(12, FontWeight.Medium, "ManropeSemibold", LineHeight: 16),
			},

			Spacing = new SpacingTokenSet
			{
				None = 0,
				ExtraSmall = 4,
				Small = 8,
				Medium = 16,
				Large = 24,
				ExtraLarge = 32,
			},

			Shapes = new ShapeTokenSet
			{
				None = 0,
				ExtraSmall = 4,
				Small = 8,
				Medium = 12,
				Large = 16,
				ExtraLarge = 25,
				Full = 999,
			},
		};

		_themeData.AddOrUpdate(theme, new CoffeeThemeData
		{
			SurfaceElevated = Color.FromArgb("#B3A291"),
			TextPrimary = Color.FromArgb("#F8F6F4"),
			TextSecondary = Color.FromArgb("#C5BFBB"),
			TextMuted = Color.FromArgb("#A19085"),
			Success = Color.FromArgb("#4CAF50"),
			Warning = Color.FromArgb("#FFA726"),
			Error = Color.FromArgb("#EF5350"),
			Info = Color.FromArgb("#42A5F5"),
			SpacingXXL = 48,
			FontRegular = "Manrope",
			FontSemibold = "ManropeSemibold",
		});

		return theme;
	}

	/// <summary>
	/// Resolves the appropriate coffee theme for a given app theme mode.
	/// </summary>
	public static Theme ForMode(AppThemeMode mode)
	{
		return mode switch
		{
			AppThemeMode.Dark => Dark,
			AppThemeMode.Light => Light,
			AppThemeMode.System => IsSystemDark() ? Dark : Light,
			_ => Light,
		};
	}

	/// <summary>
	/// Initializes the theme system. Call once at startup.
	/// Loads saved preference and sets the active theme via ThemeManager.
	/// </summary>
	public static void Initialize(IThemeService themeService)
	{
		themeService.LoadSavedTheme();
		var theme = ForMode(themeService.CurrentMode);
		ThemeManager.SetTheme(theme);

		// Keep ThemeManager in sync when ThemeService changes
		themeService.ThemeChanged += mode =>
		{
			var next = ForMode(mode);
			ThemeManager.SetTheme(next);
		};
	}

	static bool IsSystemDark()
	{
		try
		{
			return AppInfo.Current.RequestedTheme == Microsoft.Maui.ApplicationModel.AppTheme.Dark;
		}
		catch
		{
			return false;
		}
	}
}

/// <summary>
/// Extension for ConditionalWeakTable that adds or updates a value.
/// </summary>
file static class ConditionalWeakTableExtensions
{
	public static void AddOrUpdate<TKey, TValue>(
		this System.Runtime.CompilerServices.ConditionalWeakTable<TKey, TValue> table,
		TKey key,
		TValue value)
		where TKey : class
		where TValue : class
	{
		table.Remove(key);
		table.Add(key, value);
	}
}
