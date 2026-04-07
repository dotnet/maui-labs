using Comet;
using Comet.Styles;
using CometBaristaNotes.Components;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using CometButton = Comet.Button;
using CometView = Comet.View;


namespace CometBaristaNotes.Styles;

/// <summary>
/// ControlStyle&lt;T&gt; definitions for the coffee theme.
/// Uses hex constants from CoffeeColors; Holden's design tokens will be wired later.
/// </summary>
public static class CoffeeControlStyles
{
	/// <summary>
	/// Solid primary-colored button with white text and pill shape.
	/// </summary>
	public static ControlStyle<CometButton> PrimaryButton { get; } = new ControlStyle<CometButton>()
		.Set(EnvironmentKeys.Colors.Background, CoffeeColors.Primary)
		.Set(EnvironmentKeys.Colors.Color, Colors.White);

	/// <summary>
	/// Outlined button with primary-colored text, transparent background.
	/// </summary>
	public static ControlStyle<CometButton> SecondaryButton { get; } = new ControlStyle<CometButton>()
		.Set(EnvironmentKeys.Colors.Background, Colors.Transparent)
		.Set(EnvironmentKeys.Colors.Color, CoffeeColors.Primary);

	/// <summary>
	/// Destructive action button — solid error background with white text.
	/// </summary>
	public static ControlStyle<CometButton> DangerButton { get; } = new ControlStyle<CometButton>()
		.Set(EnvironmentKeys.Colors.Background, CoffeeColors.Error)
		.Set(EnvironmentKeys.Colors.Color, Colors.White);

	/// <summary>
	/// Standard card container: theme surface background, rounded corners, subtle stroke.
	/// Apply to VStack/HStack containers to get a card look.
	/// </summary>
	public static ControlStyle<CometView> CardStyle { get; } = new ControlStyle<CometView>()
		.Set(EnvironmentKeys.Colors.Background, CoffeeColors.Surface);
}
