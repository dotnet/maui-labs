using System;
using Microsoft.Maui.Graphics;

namespace Comet.Styles
{
	/// <summary>
	/// Strongly-typed color token identifiers following Material 3 semantic naming.
	/// Each field resolves its value from the active Theme's ColorTokenSet.
	/// </summary>
	public static class ColorTokens
	{
		// Primary
		public static readonly Token<Color> Primary =
			new("theme.color.primary", theme => theme.Colors?.Primary, "Primary");
		public static readonly Token<Color> OnPrimary =
			new("theme.color.onPrimary", theme => theme.Colors?.OnPrimary, "On Primary");
		public static readonly Token<Color> PrimaryContainer =
			new("theme.color.primaryContainer", theme => theme.Colors?.PrimaryContainer, "Primary Container");
		public static readonly Token<Color> OnPrimaryContainer =
			new("theme.color.onPrimaryContainer", theme => theme.Colors?.OnPrimaryContainer, "On Primary Container");

		// Secondary
		public static readonly Token<Color> Secondary =
			new("theme.color.secondary", theme => theme.Colors?.Secondary, "Secondary");
		public static readonly Token<Color> OnSecondary =
			new("theme.color.onSecondary", theme => theme.Colors?.OnSecondary, "On Secondary");
		public static readonly Token<Color> SecondaryContainer =
			new("theme.color.secondaryContainer", theme => theme.Colors?.SecondaryContainer, "Secondary Container");
		public static readonly Token<Color> OnSecondaryContainer =
			new("theme.color.onSecondaryContainer", theme => theme.Colors?.OnSecondaryContainer, "On Secondary Container");

		// Tertiary
		public static readonly Token<Color> Tertiary =
			new("theme.color.tertiary", theme => theme.Colors?.Tertiary, "Tertiary");
		public static readonly Token<Color> OnTertiary =
			new("theme.color.onTertiary", theme => theme.Colors?.OnTertiary, "On Tertiary");
		public static readonly Token<Color> TertiaryContainer =
			new("theme.color.tertiaryContainer", theme => theme.Colors?.TertiaryContainer, "Tertiary Container");
		public static readonly Token<Color> OnTertiaryContainer =
			new("theme.color.onTertiaryContainer", theme => theme.Colors?.OnTertiaryContainer, "On Tertiary Container");

		// Error
		public static readonly Token<Color> Error =
			new("theme.color.error", theme => theme.Colors?.Error, "Error");
		public static readonly Token<Color> OnError =
			new("theme.color.onError", theme => theme.Colors?.OnError, "On Error");
		public static readonly Token<Color> ErrorContainer =
			new("theme.color.errorContainer", theme => theme.Colors?.ErrorContainer, "Error Container");
		public static readonly Token<Color> OnErrorContainer =
			new("theme.color.onErrorContainer", theme => theme.Colors?.OnErrorContainer, "On Error Container");

		// Surface
		public static readonly Token<Color> Surface =
			new("theme.color.surface", theme => theme.Colors?.Surface, "Surface");
		public static readonly Token<Color> OnSurface =
			new("theme.color.onSurface", theme => theme.Colors?.OnSurface, "On Surface");
		public static readonly Token<Color> SurfaceVariant =
			new("theme.color.surfaceVariant", theme => theme.Colors?.SurfaceVariant, "Surface Variant");
		public static readonly Token<Color> OnSurfaceVariant =
			new("theme.color.onSurfaceVariant", theme => theme.Colors?.OnSurfaceVariant, "On Surface Variant");
		public static readonly Token<Color> SurfaceContainer =
			new("theme.color.surfaceContainer", theme => theme.Colors?.SurfaceContainer, "Surface Container");
		public static readonly Token<Color> SurfaceContainerLow =
			new("theme.color.surfaceContainerLow", theme => theme.Colors?.SurfaceContainerLow, "Surface Container Low");
		public static readonly Token<Color> SurfaceContainerHigh =
			new("theme.color.surfaceContainerHigh", theme => theme.Colors?.SurfaceContainerHigh, "Surface Container High");

		// Background
		public static readonly Token<Color> Background =
			new("theme.color.background", theme => theme.Colors?.Background, "Background");
		public static readonly Token<Color> OnBackground =
			new("theme.color.onBackground", theme => theme.Colors?.OnBackground, "On Background");

		// Outline
		public static readonly Token<Color> Outline =
			new("theme.color.outline", theme => theme.Colors?.Outline, "Outline");
		public static readonly Token<Color> OutlineVariant =
			new("theme.color.outlineVariant", theme => theme.Colors?.OutlineVariant, "Outline Variant");

		// Inverse
		public static readonly Token<Color> InverseSurface =
			new("theme.color.inverseSurface", theme => theme.Colors?.InverseSurface, "Inverse Surface");
		public static readonly Token<Color> InverseOnSurface =
			new("theme.color.inverseOnSurface", theme => theme.Colors?.InverseOnSurface, "Inverse On Surface");
		public static readonly Token<Color> InversePrimary =
			new("theme.color.inversePrimary", theme => theme.Colors?.InversePrimary, "Inverse Primary");
	}
}
