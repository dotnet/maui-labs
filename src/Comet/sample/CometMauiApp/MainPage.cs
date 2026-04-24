using System;
using Comet;
using Comet.Styles;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometMauiApp
{
	// ── Custom ViewModifier ──────────────────────────────────────
	// A reusable "card" appearance that can be applied to any view.
	public class CardModifier : ViewModifier
	{
		public override View Apply(View view)
		{
			view
				.Background(new SolidPaint(ColorTokens.Surface.Resolve(ThemeManager.Current())))
				.ClipShape(new RoundedRectangle(16))
				.Padding(new Thickness(20));
			return view;
		}
	}

	// ── State ────────────────────────────────────────────────────
	public class StyleDemoState
	{
		public int TapCount { get; set; }
		public bool IsActionEnabled { get; set; } = true;
	}

	// ── Main Page ────────────────────────────────────────────────
	public class MainPage : Component<StyleDemoState>
	{
		static readonly CardModifier Card = new();

		public override View Render()
		{
			return NavigationView(
				ScrollView(
					VStack(24,
						BuildTokenShowcase(),
						BuildButtonStyleShowcase(),
						BuildViewModifierShowcase(),
						BuildControlStateShowcase(),
						BuildInfoCard()
					)
					.Padding(new Thickness(24))
				)
				.Background(ColorTokens.Background)
			)
			.Title("Style System");
		}

		// ── 1. Token Usage ───────────────────────────────────────
		// Direct use of ColorTokens and TypographyTokens on views.
		View BuildTokenShowcase()
		{
			return Border(
				VStack(12,
					Text("Token Usage")
						.Typography(TypographyTokens.TitleLarge)
						.Color(ColorTokens.OnSurface),

					Text("Colors and typography resolve from the active theme.")
						.LineBreakMode(LineBreakMode.WordWrap)
						.Typography(TypographyTokens.BodyMedium)
						.Color(ColorTokens.OnSurfaceVariant),

					HStack(12,
						Border(
							Text("Primary")
								.Color(ColorTokens.OnPrimary)
								.FontSize(13)
								.HorizontalTextAlignment(TextAlignment.Center)
						)
						.Background(ColorTokens.Primary)
						.CornerRadius(12)
						.Padding(new Thickness(16, 10))
						.Frame(width: 100),

						Border(
							Text("Secondary")
								.Color(ColorTokens.OnSecondary)
								.FontSize(13)
								.HorizontalTextAlignment(TextAlignment.Center)
						)
						.Background(ColorTokens.Secondary)
						.CornerRadius(12)
						.Padding(new Thickness(16, 10))
						.Frame(width: 100),

						Border(
							Text("Error")
								.Color(ColorTokens.OnError)
								.FontSize(13)
								.HorizontalTextAlignment(TextAlignment.Center)
						)
						.Background(ColorTokens.Error)
						.CornerRadius(12)
						.Padding(new Thickness(16, 10))
						.Frame(width: 100)
					)
				)
			)
			.Modifier(Card);
		}

		// ── 2. Built-in Button Styles ────────────────────────────
		// ButtonStyles.Filled, .Outlined, .Text, .Elevated applied via .ButtonStyle().
		View BuildButtonStyleShowcase()
		{
			return Border(
				VStack(12,
					Text("Built-in Button Styles")
						.Typography(TypographyTokens.TitleLarge)
						.Color(ColorTokens.OnSurface),

					Text("Each button uses a different IControlStyle<Button> from ButtonStyles.")
						.Typography(TypographyTokens.BodyMedium)
						.Color(ColorTokens.OnSurfaceVariant),

					Button("Filled Button", () => SetState(s => s.TapCount++))
						.ButtonStyle(ButtonStyles.Filled)
						.AutomationId("btn-filled"),

					Button("Outlined Button", () => SetState(s => s.TapCount++))
						.ButtonStyle(ButtonStyles.Outlined)
						.AutomationId("btn-outlined"),

					Button("Text Button", () => SetState(s => s.TapCount++))
						.ButtonStyle(ButtonStyles.Text)
						.AutomationId("btn-text"),

					Button("Elevated Button", () => SetState(s => s.TapCount++))
						.ButtonStyle(ButtonStyles.Elevated)
						.AutomationId("btn-elevated"),

					Text($"Total taps: {State.TapCount}")
						.Typography(TypographyTokens.LabelLarge)
						.Color(ColorTokens.Primary)
				)
			)
			.Modifier(Card);
		}

		// ── 3. ViewModifier ──────────────────────────────────────
		// Shows the reusable CardModifier applied to a section, plus a composed modifier.
		View BuildViewModifierShowcase()
		{
			var highlightCard = Card.Then(new HighlightModifier());

			return Border(
				VStack(12,
					Text("ViewModifier")
						.Typography(TypographyTokens.TitleLarge)
						.Color(ColorTokens.OnSurface),

					Text("The CardModifier is reused across every section. Below, a composed modifier (Card + Highlight) colors the border with the primary token.")
						.Typography(TypographyTokens.BodyMedium)
						.Color(ColorTokens.OnSurfaceVariant),

					Border(
						Text("Card + Highlight composed modifier")
							.Typography(TypographyTokens.BodyLarge)
							.Color(ColorTokens.OnPrimaryContainer)
					)
					.Modifier(highlightCard)
				)
			)
			.Modifier(Card);
		}

		// ── 4. Control State ─────────────────────────────────────
		// Toggle enables/disables a button to show disabled state rendering.
		View BuildControlStateShowcase()
		{
			return Border(
				VStack(12,
					Text("Control State")
						.Typography(TypographyTokens.TitleLarge)
						.Color(ColorTokens.OnSurface),

					Text("Toggle the switch to enable or disable the button. The filled style renders a muted appearance when disabled.")
						.Typography(TypographyTokens.BodyMedium)
						.Color(ColorTokens.OnSurfaceVariant),

					HStack(12,
						Toggle(State.IsActionEnabled)
							.OnColor(ColorTokens.Primary.Resolve(ThemeManager.Current()))
							.OnToggled(isOn => SetState(s => s.IsActionEnabled = isOn))
							.AutomationId("toggle-enabled"),

						Text(State.IsActionEnabled ? "Enabled" : "Disabled")
							.Typography(TypographyTokens.LabelLarge)
							.Color(ColorTokens.OnSurface)
							.VerticalTextAlignment(TextAlignment.Center)
					),

					Button("Stateful Button", () => SetState(s => s.TapCount++))
						.ButtonStyle(ButtonStyles.Filled)
						.IsEnabled(State.IsActionEnabled)
						.AutomationId("btn-stateful")
				)
			)
			.Modifier(Card);
		}

		// ── 5. Info ──────────────────────────────────────────────
		View BuildInfoCard()
		{
			return Border(
				VStack(10,
					Text("What this sample demonstrates")
						.Typography(TypographyTokens.TitleMedium)
						.Color(ColorTokens.OnSurface),

					Text("• Token<T> — ColorTokens.Primary, TypographyTokens.TitleLarge")
						.Typography(TypographyTokens.BodySmall)
						.Color(ColorTokens.OnSurfaceVariant),
					Text("• Theme — Defaults.Light applied at startup via Theme.Current")
						.Typography(TypographyTokens.BodySmall)
						.Color(ColorTokens.OnSurfaceVariant),
					Text("• ButtonStyles — Filled, Outlined, Text, Elevated")
						.Typography(TypographyTokens.BodySmall)
						.Color(ColorTokens.OnSurfaceVariant),
					Text("• ViewModifier — CardModifier reused across sections")
						.Typography(TypographyTokens.BodySmall)
						.Color(ColorTokens.OnSurfaceVariant),
					Text("• ControlState — IsEnabled toggle shows disabled rendering")
						.Typography(TypographyTokens.BodySmall)
						.Color(ColorTokens.OnSurfaceVariant)
				)
			)
			.Modifier(Card);
		}
	}

	// ── Highlight Modifier ───────────────────────────────────────
	// Composes with CardModifier to add a primary container background.
	public class HighlightModifier : ViewModifier
	{
		public override View Apply(View view)
		{
			view
				.Background(new SolidPaint(ColorTokens.PrimaryContainer.Resolve(ThemeManager.Current())))
				.ClipShape(new RoundedRectangle(12))
				.Padding(new Thickness(16, 12));
			return view;
		}
	}
}
