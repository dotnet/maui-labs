using System;
using Comet;
using Comet.Styles;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Primitives;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class StyleSystemState
	{
		public string ActiveThemeName { get; set; } = "Light (Default)";
		public string ActiveButtonStyleName { get; set; } = "Text";
		public string TokenLog { get; set; } = "";
	}

	/// <summary>
	/// Comprehensive test page for Comet's Style/Theme system.
	/// Exercises design tokens, view modifiers, control styles, theme switching,
	/// and implicit vs explicit style resolution.
	/// </summary>
	public class StyleSystemPage : Component<StyleSystemState>
	{
		public StyleSystemPage()
		{
			RefreshTokenLog();
		}

		public override View Render()
		{
			return ScrollView(
				VStack(20,
					// --- Title ---
					Text("Style System Test")
						.FontSize(28)
						.FontWeight(FontWeight.Bold)
						.HorizontalTextAlignment(TextAlignment.Center),

					Text("Exercises the full Comet.Styles pipeline")
						.FontSize(14)
						.Color(Colors.Grey)
						.HorizontalTextAlignment(TextAlignment.Center),

					// --- Section 1: Design Tokens ---
					BuildDesignTokensSection(),

					// --- Section 2: Button Style Variants ---
					BuildButtonStylesSection(),

					// --- Section 3: ViewModifier ---
					BuildViewModifierSection(),

					// --- Section 4: Theme Switching ---
					BuildThemeSwitchingSection(),

					// --- Section 5: Scoped Style Override ---
					BuildScopedStyleSection(),

					// --- Section 6: Implicit vs Explicit ---
					BuildImplicitExplicitSection(),

					// --- Section 7: Token Log ---
					BuildTokenLogSection()
				)
				.Padding(new Thickness(GalleryPageHelpers.IsPhone ? 16 : 24))
			)
			.Background(Colors.White)
			.Title("Style System");
		}

		View BuildDesignTokensSection()
		{
			var theme = ThemeManager.Current();

			// Resolve color tokens
			var primary = ColorTokens.Primary.Resolve(theme);
			var onPrimary = ColorTokens.OnPrimary.Resolve(theme);
			var secondary = ColorTokens.Secondary.Resolve(theme);
			var surface = ColorTokens.Surface.Resolve(theme);
			var onSurface = ColorTokens.OnSurface.Resolve(theme);
			var error = ColorTokens.Error.Resolve(theme);
			var background = ColorTokens.Background.Resolve(theme);
			var outline = ColorTokens.Outline.Resolve(theme);
			var primaryContainer = ColorTokens.PrimaryContainer.Resolve(theme);
			var surfaceVariant = ColorTokens.SurfaceVariant.Resolve(theme);

			// Resolve spacing tokens
			var spacingSm = SpacingTokens.Small.Resolve(theme);
			var spacingMd = SpacingTokens.Medium.Resolve(theme);
			var spacingLg = SpacingTokens.Large.Resolve(theme);

			// Resolve shape tokens
			var shapeSm = ShapeTokens.Small.Resolve(theme);
			var shapeMd = ShapeTokens.Medium.Resolve(theme);
			var shapeLg = ShapeTokens.Large.Resolve(theme);

			return VStack(12,
				SectionHeader("1. Design Tokens"),
				Text("Color tokens resolved from current theme:")
					.FontSize(13).Color(Colors.DimGrey),

				// Color swatches
				SwatchRow(
					ColorSwatch("Primary", primary, onPrimary),
					ColorSwatch("OnPrimary", onPrimary, primary),
					ColorSwatch("Secondary", secondary, Colors.White),
					ColorSwatch("Surface", surface, onSurface),
					ColorSwatch("Error", error, Colors.White)
				),
				SwatchRow(
					ColorSwatch("Background", background, onSurface),
					ColorSwatch("Outline", outline, Colors.White),
					ColorSwatch("PrimaryContainer", primaryContainer, Colors.Black),
					ColorSwatch("SurfaceVariant", surfaceVariant, Colors.Black),
					ColorSwatch("OnSurface", onSurface, surface)
				),

				Text($"Spacing tokens: Small={spacingSm}, Medium={spacingMd}, Large={spacingLg}")
					.FontSize(12).Color(Colors.DimGrey),
				Text($"Shape tokens: Small={shapeSm}, Medium={shapeMd}, Large={shapeLg}")
					.FontSize(12).Color(Colors.DimGrey),

				// Typography tokens
				BuildTypographyPreview(theme),

				Separator()
			);
		}

		View BuildTypographyPreview(Theme theme)
		{
			var displayLg = TypographyTokens.DisplayLarge.Resolve(theme);
			var headlineMd = TypographyTokens.HeadlineMedium.Resolve(theme);
			var bodyLg = TypographyTokens.BodyLarge.Resolve(theme);
			var labelSm = TypographyTokens.LabelSmall.Resolve(theme);

			return VStack(4,
				Text("Typography tokens:")
					.FontSize(13).Color(Colors.DimGrey),
				Text($"DisplayLarge: size={displayLg.Size}, weight={displayLg.Weight}")
					.FontSize(12).Color(Colors.DimGrey),
				Text($"HeadlineMedium: size={headlineMd.Size}, weight={headlineMd.Weight}")
					.FontSize(12).Color(Colors.DimGrey),
				Text($"BodyLarge: size={bodyLg.Size}, weight={bodyLg.Weight}")
					.FontSize(12).Color(Colors.DimGrey),
				Text($"LabelSmall: size={labelSm.Size}, weight={labelSm.Weight}")
					.FontSize(12).Color(Colors.DimGrey)
			);
		}

		View BuildButtonStylesSection()
		{
			return VStack(12,
				SectionHeader("2. Button Style Variants (IControlStyle)"),
				Text("Each button uses a different ButtonStyle via .ButtonStyle():")
					.FontSize(13).Color(Colors.DimGrey),

				GalleryPageHelpers.ButtonRow(12,
					Button("Filled", () => { })
						.ButtonStyle(ButtonStyles.Filled),
					Button("Outlined", () => { })
						.ButtonStyle(ButtonStyles.Outlined),
					Button("Text", () => { })
						.ButtonStyle(ButtonStyles.Text),
					Button("Elevated", () => { })
						.ButtonStyle(ButtonStyles.Elevated)
				),

				Text($"Active global style: {State.ActiveButtonStyleName}")
					.FontSize(12).Color(Colors.DimGrey),
				Text("The buttons above override the global style per-control.")
					.FontSize(12).Color(Colors.DimGrey),

				// Global style switcher
				Text("Switch global button style:")
					.FontSize(13).FontWeight(FontWeight.Semibold),
				GalleryPageHelpers.ButtonRow(8,
					Button("Set Filled", () => SwitchGlobalButtonStyle("Filled", ButtonStyles.Filled)),
					Button("Set Outlined", () => SwitchGlobalButtonStyle("Outlined", ButtonStyles.Outlined)),
					Button("Set Text", () => SwitchGlobalButtonStyle("Text", ButtonStyles.Text)),
					Button("Set Elevated", () => SwitchGlobalButtonStyle("Elevated", ButtonStyles.Elevated))
				),

				Text("These buttons use the global default (from ThemeManager).")
					.FontSize(12).Color(Colors.DimGrey),
				GalleryPageHelpers.ButtonRow(12,
					Button("Global A", () => { }),
					Button("Global B", () => { }),
					Button("Global C", () => { })
				),
				Separator()
			);
		}

		View BuildViewModifierSection()
		{
			return VStack(12,
				SectionHeader("3. ViewModifier"),
				Text("SectionCard modifier applies surface background, rounded corners, and padding:")
					.FontSize(13).Color(Colors.DimGrey),

				// Apply the SectionCard ViewModifier
				VStack(8,
					Text("Inside SectionCard modifier")
						.FontSize(16).FontWeight(FontWeight.Bold),
					Text("This VStack has .Modifier(new SectionCard()) applied.")
						.FontSize(13),
					Button("Button inside modifier", () => { })
				).Modifier(new SectionCard()),

				// Custom ViewModifier
				Text("Custom HighlightModifier (yellow bg, border, padding):")
					.FontSize(13).Color(Colors.DimGrey),

				Text("Highlighted text")
					.FontSize(16)
					.Modifier(new HighlightModifier()),

				// Composed modifier
				Text("Composed modifier (SectionCard + Highlight):")
					.FontSize(13).Color(Colors.DimGrey),

				Text("SectionCard then Highlight")
					.FontSize(14)
					.Modifier(new SectionCard().Then(new HighlightModifier())),

				Separator()
			);
		}

		View BuildThemeSwitchingSection()
		{
			return VStack(12,
				SectionHeader("4. Theme Switching"),
				Text($"Active theme: {State.ActiveThemeName}")
					.FontSize(14).FontWeight(FontWeight.Semibold),

				GalleryPageHelpers.ButtonRow(8,
					Button("Light Theme", () => SwitchTheme("Light (Default)", Defaults.Light)),
					Button("Dark Theme", () => SwitchTheme("Dark", Defaults.Dark)),
					Button("Custom (Purple)", () => SwitchTheme("Custom Purple", CreatePurpleTheme()))
				),

				// Token readout updates when theme changes
				Text("Token readout after theme switch:")
					.FontSize(13).Color(Colors.DimGrey),
				BuildThemeColorReadout(),

				Separator()
			);
		}

		View BuildThemeColorReadout()
		{
			var theme = ThemeManager.Current();
			var primary = ColorTokens.Primary.Resolve(theme);
			var secondary = ColorTokens.Secondary.Resolve(theme);
			var surface = ColorTokens.Surface.Resolve(theme);

			return SwatchRow(
				ColorSwatch("Primary", primary, Colors.White),
				ColorSwatch("Secondary", secondary, Colors.White),
				ColorSwatch("Surface", surface, Colors.Black)
			);
		}

		View BuildScopedStyleSection()
		{
			var purpleTheme = CreatePurpleTheme();

			return VStack(12,
				SectionHeader("5. Scoped Style Override"),
				Text("The container below uses .UseTheme(purpleTheme) to override tokens for its subtree:")
					.FontSize(13).Color(Colors.DimGrey),

				// Scoped theme on a subtree
				VStack(8,
					Text("Inside scoped purple theme")
						.FontSize(16).FontWeight(FontWeight.Bold),
					Text("ColorTokens.Primary resolves to purple here")
						.FontSize(13),
					Button("Filled in scoped theme", () => { })
						.ButtonStyle(ButtonStyles.Filled)
				)
				.UseTheme(purpleTheme)
				.Background(new SolidPaint(Color.FromArgb("#F3E5F5")))
				.Padding(new Thickness(16))
				.ClipShape(new RoundedRectangle(8)),

				// Scoped button style override
				Text("Scoped .ButtonStyle() override — only children get Outlined:")
					.FontSize(13).Color(Colors.DimGrey),

				VStack(8,
					Button("Inherits Outlined from parent", () => { }),
					Button("Also Outlined", () => { })
				)
				.ButtonStyle(ButtonStyles.Outlined)
				.Padding(new Thickness(16))
				.Background(new SolidPaint(Color.FromArgb("#E8F5E9")))
				.ClipShape(new RoundedRectangle(8)),

				Separator()
			);
		}

		View BuildImplicitExplicitSection()
		{
			return VStack(12,
				SectionHeader("6. Implicit vs Explicit Resolution"),
				Text("Implicit: Buttons auto-resolve their style from the theme or environment.")
					.FontSize(13).Color(Colors.DimGrey),

				Button("Implicit (uses global style)", () => { }),

				Text("Explicit: .ButtonStyle(ButtonStyles.Filled) overrides the implicit style.")
					.FontSize(13).Color(Colors.DimGrey),

				Button("Explicit Filled", () => { })
					.ButtonStyle(ButtonStyles.Filled),

				Text("Explicit + manual override: .Background(Colors.Red) wins over style.")
					.FontSize(13).Color(Colors.DimGrey),

				Button("Explicit Filled + Red BG", () => { })
					.ButtonStyle(ButtonStyles.Filled)
					.Background(Colors.Red),

				Separator()
			);
		}

		View BuildTokenLogSection()
		{
			return VStack(12,
				SectionHeader("7. Token Resolution Log"),
				Text("Resolved values from current ThemeManager.Current():")
					.FontSize(13).Color(Colors.DimGrey),
				Text(State.TokenLog)
					.FontSize(11)
					.Color(Colors.DimGrey),
				Button("Refresh Token Log", () => RefreshTokenLog())
			);
		}

		// --- Helpers ---

		void RefreshTokenLog()
		{
			var theme = ThemeManager.Current();
			var lines = new System.Text.StringBuilder();
			lines.AppendLine($"Theme Name: {theme.Name ?? "(unnamed)"}");
			lines.AppendLine($"Primary: {ColorTokens.Primary.Resolve(theme)}");
			lines.AppendLine($"OnPrimary: {ColorTokens.OnPrimary.Resolve(theme)}");
			lines.AppendLine($"Secondary: {ColorTokens.Secondary.Resolve(theme)}");
			lines.AppendLine($"Surface: {ColorTokens.Surface.Resolve(theme)}");
			lines.AppendLine($"Error: {ColorTokens.Error.Resolve(theme)}");
			lines.AppendLine($"Background: {ColorTokens.Background.Resolve(theme)}");
			lines.AppendLine($"Outline: {ColorTokens.Outline.Resolve(theme)}");
			lines.AppendLine($"Spacing.Small: {SpacingTokens.Small.Resolve(theme)}");
			lines.AppendLine($"Spacing.Medium: {SpacingTokens.Medium.Resolve(theme)}");
			lines.AppendLine($"Spacing.Large: {SpacingTokens.Large.Resolve(theme)}");
			lines.AppendLine($"Shape.Medium: {ShapeTokens.Medium.Resolve(theme)}");
			lines.AppendLine($"Typography.BodyLarge: size={TypographyTokens.BodyLarge.Resolve(theme).Size}");
			lines.AppendLine($"Has Button style: {theme.GetControlStyle<Button, ButtonConfiguration>() != null}");

			SetState(s => s.TokenLog = lines.ToString());
		}

		void SwitchTheme(string name, Theme theme)
		{
			// Preserve the current button style when switching themes
			var currentBtnStyle = State.ActiveButtonStyleName;
			var btnStyle = currentBtnStyle switch
			{
				"Filled" => ButtonStyles.Filled,
				"Outlined" => ButtonStyles.Outlined,
				"Elevated" => ButtonStyles.Elevated,
				_ => ButtonStyles.Text,
			};
			theme = theme.SetControlStyle<Button, ButtonConfiguration>(btnStyle);
			ThemeManager.SetTheme(theme);

			SetState(s =>
			{
				s.ActiveThemeName = name;
			});
			RefreshTokenLog();
		}

		void SwitchGlobalButtonStyle(string name, IControlStyle<Button, ButtonConfiguration> style)
		{
			var theme = ThemeManager.Current();
			theme = theme.SetControlStyle<Button, ButtonConfiguration>(style);
			ThemeManager.SetTheme(theme);

			SetState(s => s.ActiveButtonStyleName = name);
			RefreshTokenLog();
		}

		static Theme CreatePurpleTheme()
		{
			return new Theme
			{
				Name = "Purple",
				Colors = new ColorTokenSet
				{
					Primary = Color.FromArgb("#7B1FA2"),
					OnPrimary = Colors.White,
					PrimaryContainer = Color.FromArgb("#E1BEE7"),
					OnPrimaryContainer = Color.FromArgb("#4A0072"),
					Secondary = Color.FromArgb("#CE93D8"),
					OnSecondary = Colors.White,
					SecondaryContainer = Color.FromArgb("#F3E5F5"),
					OnSecondaryContainer = Color.FromArgb("#4A0072"),
					Surface = Color.FromArgb("#FFFBFE"),
					OnSurface = Color.FromArgb("#1C1B1F"),
					SurfaceVariant = Color.FromArgb("#F5EFF7"),
					SurfaceContainer = Color.FromArgb("#EDE7F6"),
					Error = Color.FromArgb("#B3261E"),
					OnError = Colors.White,
					Background = Color.FromArgb("#FFFBFE"),
					OnBackground = Color.FromArgb("#1C1B1F"),
					Outline = Color.FromArgb("#79747E"),
				},
				Typography = TypographyDefaults.Material3,
				Spacing = SpacingDefaults.Standard,
				Shapes = ShapeDefaults.Rounded,
			};
		}

		static View SectionHeader(string title) =>
			Text(title)
				.FontSize(18)
				.FontWeight(FontWeight.Bold)
				.Color(Colors.CornflowerBlue);

		static View Separator() =>
			new ShapeView(new Rectangle())
				.Background(Colors.Grey)
				.Frame(height: 1)
				.Opacity(0.3f);

		static View SwatchRow(params View[] swatches) =>
			GalleryPageHelpers.IsPhone
				? ScrollView(Orientation.Horizontal, HStack(6, swatches))
				: (View)HStack(8, swatches);

		static View ColorSwatch(string label, Color bg, Color fg)
		{
			var isPhone = GalleryPageHelpers.IsPhone;
			return Border(
				Text(label)
					.FontSize(isPhone ? 8 : 10)
					.FontWeight(FontWeight.Bold)
					.Color(fg)
					.HorizontalTextAlignment(TextAlignment.Center)
			)
			.Background(bg)
			.CornerRadius(8)
			.Frame(width: isPhone ? 62 : 90, height: isPhone ? 36 : 44)
			.Padding(new Thickness(4, 2));
		}
	}

	/// <summary>
	/// Custom ViewModifier for testing: yellow highlight with border.
	/// </summary>
	public class HighlightModifier : ViewModifier
	{
		public override View Apply(View view)
		{
			view
				.Background(new SolidPaint(Color.FromArgb("#FFF9C4")))
				.Padding(new Thickness(12, 8))
				.ClipShape(new RoundedRectangle(6));
			return view;
		}
	}
}
