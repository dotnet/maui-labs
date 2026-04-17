using Microsoft.Maui.Graphics;

namespace Comet.Styles
{
	// Built-in Material 3 button styles.
	//
	// Per STYLE_THEME_SPEC.md §3.6 (Static Modifier Instances) and §11.4 (Modifier
	// Application Cost): appearances carry no per-instance state, so each (state ×
	// style) combination is a singleton `ViewModifier<Button>`. Resolve() selects
	// the right singleton for the current ButtonConfiguration — zero allocation.
	//
	// Theme-aware colors are resolved at Apply() time via ColorTokens.X.Resolve(view),
	// so a single singleton correctly renders in whichever theme scope the button
	// lives in. Shape objects (RoundedRectangle) are cached as static readonly
	// fields; Thickness is a struct so it doesn't allocate. Per §11.5, the only
	// remaining per-Apply allocation is the MAUI paint/shadow types that wrap the
	// theme-dependent Color — which is unavoidable given the MAUI type shape.

	/// <summary>
	/// Filled button style — solid primary background with state-aware color shifts.
	/// </summary>
	public sealed class FilledButtonStyle : IControlStyle<Button, ButtonConfiguration>
	{
		public ViewModifier Resolve(ButtonConfiguration config)
		{
			if (!config.IsEnabled) return Disabled.Instance;
			if (config.IsPressed) return Pressed.Instance;
			if (config.IsHovered) return Hovered.Instance;
			return Default.Instance;
		}

		static readonly RoundedRectangle s_shape = new RoundedRectangle(20);
		static readonly Thickness s_padding = new Thickness(24, 12);

		sealed class Default : ViewModifier<Button>
		{
			public static readonly Default Instance = new Default();
			public override Button Apply(Button view) => view
				.Background(ColorTokens.Primary.Resolve(view))
				.Color(ColorTokens.OnPrimary.Resolve(view))
				.Opacity(1.0)
				.ClipShape(s_shape)
				.Padding(s_padding);
		}

		sealed class Pressed : ViewModifier<Button>
		{
			public static readonly Pressed Instance = new Pressed();
			public override Button Apply(Button view) => view
				.Background(ColorTokens.Primary.Resolve(view).WithAlpha(0.88f))
				.Color(ColorTokens.OnPrimary.Resolve(view))
				.Opacity(1.0)
				.ClipShape(s_shape)
				.Padding(s_padding);
		}

		sealed class Hovered : ViewModifier<Button>
		{
			public static readonly Hovered Instance = new Hovered();
			public override Button Apply(Button view) => view
				.Background(ColorTokens.Primary.Resolve(view).WithAlpha(0.92f))
				.Color(ColorTokens.OnPrimary.Resolve(view))
				.Opacity(1.0)
				.ClipShape(s_shape)
				.Padding(s_padding);
		}

		sealed class Disabled : ViewModifier<Button>
		{
			public static readonly Disabled Instance = new Disabled();
			static readonly Color s_bg = Colors.Grey.WithAlpha(0.12f);
			static readonly Color s_fg = Colors.Grey.WithAlpha(0.38f);
			public override Button Apply(Button view) => view
				.Background(s_bg)
				.Color(s_fg)
				.Opacity(0.38)
				.ClipShape(s_shape)
				.Padding(s_padding);
		}
	}

	/// <summary>
	/// Outlined button style — transparent background with primary-colored border.
	/// </summary>
	public sealed class OutlinedButtonStyle : IControlStyle<Button, ButtonConfiguration>
	{
		public ViewModifier Resolve(ButtonConfiguration config)
		{
			if (!config.IsEnabled) return Disabled.Instance;
			if (config.IsPressed) return Pressed.Instance;
			if (config.IsHovered) return Hovered.Instance;
			return Default.Instance;
		}

		static readonly Thickness s_padding = new Thickness(24, 12);
		const float s_radius = 20f;

		sealed class Default : ViewModifier<Button>
		{
			public static readonly Default Instance = new Default();
			public override Button Apply(Button view) => view
				.Background(Colors.Transparent)
				.Color(ColorTokens.Primary.Resolve(view))
				.Opacity(1.0)
				.RoundedBorder(radius: s_radius, color: ColorTokens.Outline.Resolve(view), strokeSize: 1)
				.Padding(s_padding);
		}

		sealed class Pressed : ViewModifier<Button>
		{
			public static readonly Pressed Instance = new Pressed();
			public override Button Apply(Button view) => view
				.Background(Colors.Transparent)
				.Color(ColorTokens.Primary.Resolve(view))
				.Opacity(1.0)
				.RoundedBorder(radius: s_radius, color: ColorTokens.Primary.Resolve(view).WithAlpha(0.88f), strokeSize: 1)
				.Padding(s_padding);
		}

		sealed class Hovered : ViewModifier<Button>
		{
			public static readonly Hovered Instance = new Hovered();
			public override Button Apply(Button view) => view
				.Background(Colors.Transparent)
				.Color(ColorTokens.Primary.Resolve(view))
				.Opacity(1.0)
				.RoundedBorder(radius: s_radius, color: ColorTokens.Primary.Resolve(view).WithAlpha(0.92f), strokeSize: 1)
				.Padding(s_padding);
		}

		sealed class Disabled : ViewModifier<Button>
		{
			public static readonly Disabled Instance = new Disabled();
			static readonly Color s_fg = Colors.Grey.WithAlpha(0.38f);
			static readonly Color s_border = Colors.Grey.WithAlpha(0.12f);
			public override Button Apply(Button view) => view
				.Background(Colors.Transparent)
				.Color(s_fg)
				.Opacity(0.38)
				.RoundedBorder(radius: s_radius, color: s_border, strokeSize: 1)
				.Padding(s_padding);
		}
	}

	/// <summary>
	/// Text button style — no background, no border, primary-colored text only.
	/// </summary>
	public sealed class TextButtonStyle : IControlStyle<Button, ButtonConfiguration>
	{
		public ViewModifier Resolve(ButtonConfiguration config)
		{
			if (!config.IsEnabled) return Disabled.Instance;
			if (config.IsPressed) return Pressed.Instance;
			if (config.IsHovered) return Hovered.Instance;
			return Default.Instance;
		}

		static readonly Thickness s_padding = new Thickness(12, 8);

		sealed class Default : ViewModifier<Button>
		{
			public static readonly Default Instance = new Default();
			public override Button Apply(Button view) => view
				.Background(Colors.Transparent)
				.Color(ColorTokens.Primary.Resolve(view))
				.Opacity(1.0)
				.Padding(s_padding);
		}

		sealed class Pressed : ViewModifier<Button>
		{
			public static readonly Pressed Instance = new Pressed();
			public override Button Apply(Button view) => view
				.Background(Colors.Transparent)
				.Color(ColorTokens.Primary.Resolve(view).WithAlpha(0.88f))
				.Opacity(1.0)
				.Padding(s_padding);
		}

		sealed class Hovered : ViewModifier<Button>
		{
			public static readonly Hovered Instance = new Hovered();
			public override Button Apply(Button view) => view
				.Background(Colors.Transparent)
				.Color(ColorTokens.Primary.Resolve(view).WithAlpha(0.92f))
				.Opacity(1.0)
				.Padding(s_padding);
		}

		sealed class Disabled : ViewModifier<Button>
		{
			public static readonly Disabled Instance = new Disabled();
			static readonly Color s_fg = Colors.Grey.WithAlpha(0.38f);
			public override Button Apply(Button view) => view
				.Background(Colors.Transparent)
				.Color(s_fg)
				.Opacity(0.38)
				.Padding(s_padding);
		}
	}

	/// <summary>
	/// Elevated button style — surface-container background with elevation shadow.
	/// </summary>
	public sealed class ElevatedButtonStyle : IControlStyle<Button, ButtonConfiguration>
	{
		public ViewModifier Resolve(ButtonConfiguration config)
		{
			if (!config.IsEnabled) return Disabled.Instance;
			if (config.IsPressed) return Pressed.Instance;
			if (config.IsHovered) return Hovered.Instance;
			return Default.Instance;
		}

		static readonly RoundedRectangle s_shape = new RoundedRectangle(20);
		static readonly Thickness s_padding = new Thickness(24, 12);
		static readonly Color s_shadow = Colors.Black.WithAlpha(0.15f);

		sealed class Default : ViewModifier<Button>
		{
			public static readonly Default Instance = new Default();
			public override Button Apply(Button view) => view
				.Background(ColorTokens.SurfaceContainer.Resolve(view))
				.Color(ColorTokens.Primary.Resolve(view))
				.Opacity(1.0)
				.ClipShape(s_shape)
				.Shadow(s_shadow, radius: 2f, x: 0f, y: 1f)
				.Padding(s_padding);
		}

		sealed class Pressed : ViewModifier<Button>
		{
			public static readonly Pressed Instance = new Pressed();
			public override Button Apply(Button view) => view
				.Background(ColorTokens.SurfaceContainer.Resolve(view))
				.Color(ColorTokens.Primary.Resolve(view))
				.Opacity(1.0)
				.ClipShape(s_shape)
				.Shadow(s_shadow, radius: 1f, x: 0f, y: 0f)
				.Padding(s_padding);
		}

		sealed class Hovered : ViewModifier<Button>
		{
			public static readonly Hovered Instance = new Hovered();
			public override Button Apply(Button view) => view
				.Background(ColorTokens.SurfaceContainer.Resolve(view))
				.Color(ColorTokens.Primary.Resolve(view))
				.Opacity(1.0)
				.ClipShape(s_shape)
				.Shadow(s_shadow, radius: 4f, x: 0f, y: 2f)
				.Padding(s_padding);
		}

		sealed class Disabled : ViewModifier<Button>
		{
			public static readonly Disabled Instance = new Disabled();
			static readonly Color s_bg = Colors.Grey.WithAlpha(0.12f);
			static readonly Color s_fg = Colors.Grey.WithAlpha(0.38f);
			public override Button Apply(Button view) => view
				.Background(s_bg)
				.Color(s_fg)
				.Opacity(0.38)
				.ClipShape(s_shape)
				.Padding(s_padding);
		}
	}

	/// <summary>
	/// Static instances of built-in button styles. Each style itself is stateless;
	/// use these singletons anywhere you would otherwise allocate.
	/// </summary>
	public static class ButtonStyles
	{
		public static readonly IControlStyle<Button, ButtonConfiguration> Filled = new FilledButtonStyle();
		public static readonly IControlStyle<Button, ButtonConfiguration> Outlined = new OutlinedButtonStyle();
		public static readonly IControlStyle<Button, ButtonConfiguration> Text = new TextButtonStyle();
		public static readonly IControlStyle<Button, ButtonConfiguration> Elevated = new ElevatedButtonStyle();
	}
}
