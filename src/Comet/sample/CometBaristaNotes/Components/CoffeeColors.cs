using Microsoft.Maui.Graphics;

namespace CometBaristaNotes.Components;

/// <summary>
/// Static color/sizing constants for the coffee palette.
/// Bridge class — pages reference these during the migration to Comet tokens.
/// Will be replaced by CoffeeTokens + ThemeManager resolution in Phase 4.
/// </summary>
public static class CoffeeColors
{
	// Font families
	public const string FontRegular = "Manrope";
	public const string FontSemibold = "ManropeSemibold";

	// Card themed colors (original BaristaNotes uses ThemeKey(ThemeKeys.CardBorder))
	public static readonly Color CardBackground = Color.FromArgb("#FCEFE1");
	public static readonly Color CardStroke = Color.FromArgb("#D7C5B2");

	// Colors
	public static readonly Color Primary = Color.FromArgb("#86543F");
	public static readonly Color Background = Color.FromArgb("#D2BCA5");
	public static readonly Color Surface = Color.FromArgb("#FCEFE1");
	public static readonly Color SurfaceVariant = Color.FromArgb("#ECDAC4");
	public static readonly Color SurfaceElevated = Color.FromArgb("#FFF7EC");
	public static readonly Color TextPrimary = Color.FromArgb("#352B23");
	public static readonly Color TextSecondary = Color.FromArgb("#7C7067");
	public static readonly Color TextMuted = Color.FromArgb("#A38F7D");
	public static readonly Color Outline = Color.FromArgb("#D7C5B2");
	public static readonly Color Success = Color.FromArgb("#4CAF50");
	public static readonly Color Warning = Color.FromArgb("#FFA726");
	public static readonly Color Error = Color.FromArgb("#EF5350");
	public static readonly Color StarFilled = Color.FromArgb("#FFB800");
	public static readonly Color StarEmpty = Color.FromArgb("#D7C5B2");

	// Spacing
	public const int SpacingXS = 4;
	public const int SpacingS = 8;
	public const int SpacingM = 16;
	public const int SpacingL = 24;
	public const int SpacingXL = 32;

	// Radii
	public const float RadiusPill = 25;
	public const float RadiusCard = 12;
	public const float RadiusEditor = 16;
	public const float RadiusCircular = 999;

	// Sizes
	public const float FormFieldHeight = 50;
	public const float ButtonHeight = 48;
	public const float IconSizeSmall = 14;
	public const float IconSizeMedium = 24;
	public const float IconSizeLarge = 64;
	public const float GaugeSize = 140;
	public const float EquipmentButtonSize = 56;
}
