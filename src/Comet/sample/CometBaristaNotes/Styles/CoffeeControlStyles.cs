using CometButton = Comet.Button;
using CometView = Comet.View;


namespace CometBaristaNotes.Styles;

/// <summary>
/// ControlStyle&lt;T&gt; definitions for the coffee theme.
/// Properties are computed so colors resolve dynamically from the current theme.
/// </summary>
public static class CoffeeControlStyles
{
	/// <summary>
	/// Solid primary-colored button with white text and pill shape.
	/// </summary>
	public static ControlStyle<CometButton> PrimaryButton => new ControlStyle<CometButton>()
		.Set(EnvironmentKeys.Colors.Background, CoffeeColors.Primary)
		.Set(EnvironmentKeys.Colors.Color, Colors.White);

	/// <summary>
	/// Outlined button with primary-colored text, transparent background.
	/// </summary>
	public static ControlStyle<CometButton> SecondaryButton => new ControlStyle<CometButton>()
		.Set(EnvironmentKeys.Colors.Background, Colors.Transparent)
		.Set(EnvironmentKeys.Colors.Color, CoffeeColors.Primary);

	/// <summary>
	/// Destructive action button — solid error background with white text.
	/// </summary>
	public static ControlStyle<CometButton> DangerButton => new ControlStyle<CometButton>()
		.Set(EnvironmentKeys.Colors.Background, CoffeeColors.Error)
		.Set(EnvironmentKeys.Colors.Color, Colors.White);

	/// <summary>
	/// Standard card container: theme surface background, rounded corners, subtle stroke.
	/// Apply to VStack/HStack containers to get a card look.
	/// </summary>
	public static ControlStyle<CometView> CardStyle => new ControlStyle<CometView>()
		.Set(EnvironmentKeys.Colors.Background, CoffeeColors.Surface);
}
