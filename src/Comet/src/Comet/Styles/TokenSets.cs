using System;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Comet.Styles
{
	/// <summary>
	/// Immutable font specification used by typography tokens.
	/// </summary>
	public readonly record struct FontSpec(
		double Size,
		FontWeight Weight,
		string Family = null,
		double LineHeight = 0,
		double LetterSpacing = 0
	);

	/// <summary>
	/// Color token values for a theme — Material 3 semantic color scheme.
	/// </summary>
	public record ColorTokenSet
	{
		// Primary
		public Color Primary { get; init; }
		public Color OnPrimary { get; init; }
		public Color PrimaryContainer { get; init; }
		public Color OnPrimaryContainer { get; init; }

		// Secondary
		public Color Secondary { get; init; }
		public Color OnSecondary { get; init; }
		public Color SecondaryContainer { get; init; }
		public Color OnSecondaryContainer { get; init; }

		// Tertiary
		public Color Tertiary { get; init; }
		public Color OnTertiary { get; init; }
		public Color TertiaryContainer { get; init; }
		public Color OnTertiaryContainer { get; init; }

		// Error
		public Color Error { get; init; }
		public Color OnError { get; init; }
		public Color ErrorContainer { get; init; }
		public Color OnErrorContainer { get; init; }

		// Surface
		public Color Surface { get; init; }
		public Color OnSurface { get; init; }
		public Color SurfaceVariant { get; init; }
		public Color OnSurfaceVariant { get; init; }
		public Color SurfaceContainer { get; init; }
		public Color SurfaceContainerLow { get; init; }
		public Color SurfaceContainerHigh { get; init; }

		// Background
		public Color Background { get; init; }
		public Color OnBackground { get; init; }

		// Outline
		public Color Outline { get; init; }
		public Color OutlineVariant { get; init; }

		// Inverse
		public Color InverseSurface { get; init; }
		public Color InverseOnSurface { get; init; }
		public Color InversePrimary { get; init; }
	}

	/// <summary>
	/// Typography token values for a theme — Material 3 type scale.
	/// </summary>
	public record TypographyTokenSet
	{
		// Display
		public FontSpec DisplayLarge { get; init; }
		public FontSpec DisplayMedium { get; init; }
		public FontSpec DisplaySmall { get; init; }

		// Headline
		public FontSpec HeadlineLarge { get; init; }
		public FontSpec HeadlineMedium { get; init; }
		public FontSpec HeadlineSmall { get; init; }

		// Title
		public FontSpec TitleLarge { get; init; }
		public FontSpec TitleMedium { get; init; }
		public FontSpec TitleSmall { get; init; }

		// Body
		public FontSpec BodyLarge { get; init; }
		public FontSpec BodyMedium { get; init; }
		public FontSpec BodySmall { get; init; }

		// Label
		public FontSpec LabelLarge { get; init; }
		public FontSpec LabelMedium { get; init; }
		public FontSpec LabelSmall { get; init; }
	}

	/// <summary>
	/// Spacing token values for a theme.
	/// </summary>
	public record SpacingTokenSet
	{
		public double None { get; init; }
		public double ExtraSmall { get; init; }
		public double Small { get; init; }
		public double Medium { get; init; }
		public double Large { get; init; }
		public double ExtraLarge { get; init; }
	}

	/// <summary>
	/// Shape (corner radius) token values for a theme.
	/// </summary>
	public record ShapeTokenSet
	{
		public double None { get; init; }
		public double ExtraSmall { get; init; }
		public double Small { get; init; }
		public double Medium { get; init; }
		public double Large { get; init; }
		public double ExtraLarge { get; init; }
		public double Full { get; init; }
	}
}
