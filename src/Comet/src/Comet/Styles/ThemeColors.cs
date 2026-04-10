using Microsoft.Maui.Graphics;

namespace Comet.Styles
{
	/// <summary>
	/// Semantic color tokens following Material Design 3 conventions.
	/// Each role has a "surface" color and a matching "on" color for content placed on it.
	/// </summary>
	public class ThemeColors
	{
		// Primary
		public Color Primary { get; set; }
		public Color OnPrimary { get; set; }
		public Color PrimaryContainer { get; set; }
		public Color OnPrimaryContainer { get; set; }

		// Secondary
		public Color Secondary { get; set; }
		public Color OnSecondary { get; set; }
		public Color SecondaryContainer { get; set; }
		public Color OnSecondaryContainer { get; set; }

		// Tertiary
		public Color Tertiary { get; set; }
		public Color OnTertiary { get; set; }
		public Color TertiaryContainer { get; set; }
		public Color OnTertiaryContainer { get; set; }

		// Error
		public Color Error { get; set; }
		public Color OnError { get; set; }
		public Color ErrorContainer { get; set; }
		public Color OnErrorContainer { get; set; }

		// Background & Surface
		public Color Background { get; set; }
		public Color OnBackground { get; set; }
		public Color Surface { get; set; }
		public Color OnSurface { get; set; }
		public Color SurfaceVariant { get; set; }
		public Color OnSurfaceVariant { get; set; }

		// Outline
		public Color Outline { get; set; }
		public Color OutlineVariant { get; set; }

		// Inverse
		public Color InverseSurface { get; set; }
		public Color InverseOnSurface { get; set; }
		public Color InversePrimary { get; set; }

		/// <summary>
		/// Default light color scheme using .NET MAUI purple as primary.
		/// </summary>
		public static ThemeColors LightScheme => new ThemeColors
		{
			Primary = Color.FromArgb("#512BD4"),
			OnPrimary = Colors.White,
			PrimaryContainer = Color.FromArgb("#EADDFF"),
			OnPrimaryContainer = Color.FromArgb("#21005D"),

			Secondary = Color.FromArgb("#625B71"),
			OnSecondary = Colors.White,
			SecondaryContainer = Color.FromArgb("#E8DEF8"),
			OnSecondaryContainer = Color.FromArgb("#1D192B"),

			Tertiary = Color.FromArgb("#7D5260"),
			OnTertiary = Colors.White,
			TertiaryContainer = Color.FromArgb("#FFD8E4"),
			OnTertiaryContainer = Color.FromArgb("#31111D"),

			Error = Color.FromArgb("#B3261E"),
			OnError = Colors.White,
			ErrorContainer = Color.FromArgb("#F9DEDC"),
			OnErrorContainer = Color.FromArgb("#410E0B"),

			Background = Color.FromArgb("#FFFBFE"),
			OnBackground = Color.FromArgb("#1C1B1F"),
			Surface = Color.FromArgb("#FFFBFE"),
			OnSurface = Color.FromArgb("#1C1B1F"),
			SurfaceVariant = Color.FromArgb("#E7E0EC"),
			OnSurfaceVariant = Color.FromArgb("#49454F"),

			Outline = Color.FromArgb("#79747E"),
			OutlineVariant = Color.FromArgb("#CAC4D0"),

			InverseSurface = Color.FromArgb("#313033"),
			InverseOnSurface = Color.FromArgb("#F4EFF4"),
			InversePrimary = Color.FromArgb("#D0BCFF"),
		};

		/// <summary>
		/// Default dark color scheme using .NET MAUI purple as primary.
		/// </summary>
		public static ThemeColors DarkScheme => new ThemeColors
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

			Background = Color.FromArgb("#1C1B1F"),
			OnBackground = Color.FromArgb("#E6E1E5"),
			Surface = Color.FromArgb("#1C1B1F"),
			OnSurface = Color.FromArgb("#E6E1E5"),
			SurfaceVariant = Color.FromArgb("#49454F"),
			OnSurfaceVariant = Color.FromArgb("#CAC4D0"),

			Outline = Color.FromArgb("#938F99"),
			OutlineVariant = Color.FromArgb("#49454F"),

			InverseSurface = Color.FromArgb("#E6E1E5"),
			InverseOnSurface = Color.FromArgb("#313033"),
			InversePrimary = Color.FromArgb("#6750A4"),
		};

		/// <summary>
		/// Pushes all semantic color tokens into the environment.
		/// When target is null, sets values on the global environment.
		/// </summary>
		internal void ApplyToEnvironment(ContextualObject target = null)
		{
			SetColor(target, EnvironmentKeys.ThemeColor.Primary, Primary);
			SetColor(target, EnvironmentKeys.ThemeColor.OnPrimary, OnPrimary);
			SetColor(target, EnvironmentKeys.ThemeColor.PrimaryContainer, PrimaryContainer);
			SetColor(target, EnvironmentKeys.ThemeColor.OnPrimaryContainer, OnPrimaryContainer);

			SetColor(target, EnvironmentKeys.ThemeColor.Secondary, Secondary);
			SetColor(target, EnvironmentKeys.ThemeColor.OnSecondary, OnSecondary);
			SetColor(target, EnvironmentKeys.ThemeColor.SecondaryContainer, SecondaryContainer);
			SetColor(target, EnvironmentKeys.ThemeColor.OnSecondaryContainer, OnSecondaryContainer);

			SetColor(target, EnvironmentKeys.ThemeColor.Tertiary, Tertiary);
			SetColor(target, EnvironmentKeys.ThemeColor.OnTertiary, OnTertiary);
			SetColor(target, EnvironmentKeys.ThemeColor.TertiaryContainer, TertiaryContainer);
			SetColor(target, EnvironmentKeys.ThemeColor.OnTertiaryContainer, OnTertiaryContainer);

			SetColor(target, EnvironmentKeys.ThemeColor.Error, Error);
			SetColor(target, EnvironmentKeys.ThemeColor.OnError, OnError);
			SetColor(target, EnvironmentKeys.ThemeColor.ErrorContainer, ErrorContainer);
			SetColor(target, EnvironmentKeys.ThemeColor.OnErrorContainer, OnErrorContainer);

			SetColor(target, EnvironmentKeys.ThemeColor.Background, Background);
			SetColor(target, EnvironmentKeys.ThemeColor.OnBackground, OnBackground);
			SetColor(target, EnvironmentKeys.ThemeColor.Surface, Surface);
			SetColor(target, EnvironmentKeys.ThemeColor.OnSurface, OnSurface);
			SetColor(target, EnvironmentKeys.ThemeColor.SurfaceVariant, SurfaceVariant);
			SetColor(target, EnvironmentKeys.ThemeColor.OnSurfaceVariant, OnSurfaceVariant);

			SetColor(target, EnvironmentKeys.ThemeColor.Outline, Outline);
			SetColor(target, EnvironmentKeys.ThemeColor.OutlineVariant, OutlineVariant);

			SetColor(target, EnvironmentKeys.ThemeColor.InverseSurface, InverseSurface);
			SetColor(target, EnvironmentKeys.ThemeColor.InverseOnSurface, InverseOnSurface);
			SetColor(target, EnvironmentKeys.ThemeColor.InversePrimary, InversePrimary);
		}

		static void SetColor(ContextualObject target, string key, Color value)
		{
			if (value is null)
				return;

			if (target is not null)
				target.SetEnvironment(key, value, true);
			else
				View.SetGlobalEnvironment(key, value);
		}
	}
}
