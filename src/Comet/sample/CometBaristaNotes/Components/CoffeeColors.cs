namespace CometBaristaNotes.Components;

/// <summary>
/// Dynamic color/sizing constants for the coffee palette.
/// Color properties resolve from ThemeManager.Current() so they respond to
/// light/dark theme changes. Non-color constants (spacing, radii, sizes) are fixed.
/// </summary>
public static class CoffeeColors
{
	// Font families
	public const string FontRegular = "Manrope";
	public const string FontSemibold = "ManropeSemibold";

	// ── Theme-aware colors ──────────────────────────────────────────
	// Material 3 colors resolve from theme.Colors; app-specific colors
	// resolve from CoffeeTokens (backed by CoffeeThemeData).

	static Theme T => ThemeManager.Current();

	// Card themed colors
	public static Color CardBackground => T?.Colors?.Surface ?? Color.FromArgb("#FCEFE1");
	public static Color CardStroke => T?.Colors?.Outline ?? Color.FromArgb("#D7C5B2");

	// Material 3 colors
	public static Color Primary => T?.Colors?.Primary ?? Color.FromArgb("#86543F");
	public static Color Background => T?.Colors?.Background ?? Color.FromArgb("#D2BCA5");
	public static Color Surface => T?.Colors?.Surface ?? Color.FromArgb("#FCEFE1");
	public static Color SurfaceVariant => T?.Colors?.SurfaceVariant ?? Color.FromArgb("#ECDAC4");
	public static Color Outline => T?.Colors?.Outline ?? Color.FromArgb("#D7C5B2");

	// App-specific colors (via CoffeeTokens → CoffeeThemeData)
	public static Color SurfaceElevated => CoffeeTokens.SurfaceElevated.Resolve(T);
	public static Color TextPrimary => CoffeeTokens.TextPrimary.Resolve(T);
	public static Color TextSecondary => CoffeeTokens.TextSecondary.Resolve(T);
	public static Color TextMuted => CoffeeTokens.TextMuted.Resolve(T);
	public static Color Success => CoffeeTokens.Success.Resolve(T);
	public static Color Warning => CoffeeTokens.Warning.Resolve(T);
	public static Color Error => CoffeeTokens.Error.Resolve(T);

	// Non-themed colors
	public static readonly Color StarFilled = Color.FromArgb("#FFB800");
	public static Color StarEmpty => T?.Colors?.Outline ?? Color.FromArgb("#D7C5B2");

	// ── Fixed constants ─────────────────────────────────────────────

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
