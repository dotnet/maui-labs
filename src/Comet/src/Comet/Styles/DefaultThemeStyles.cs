using Microsoft.Maui.Graphics;

namespace Comet.Styles
{
	/// <summary>
	/// Registers sensible default <see cref="ControlStyle{T}"/> entries for common controls.
	/// Uses the theme's <see cref="ThemeColors"/> when available, falling back to
	/// <see cref="Theme.GetColor"/> for legacy themes.
	/// <para>
	/// Called automatically by <see cref="Theme.Apply()"/> when no custom styles are
	/// registered for a control type. Can also be called explicitly via
	/// <c>DefaultThemeStyles.Register(theme)</c>.
	/// </para>
	/// </summary>
	public static class DefaultThemeStyles
	{
		/// <summary>
		/// Registers default control styles on the given theme for any control types
		/// that don't already have a custom <see cref="ControlStyle{T}"/> registered.
		/// </summary>
		public static void Register(Theme theme)
		{
			RegisterButtonStyle(theme);
			RegisterTextStyle(theme);
			RegisterTextFieldStyle(theme);
			RegisterToggleStyle(theme);
			RegisterSliderStyle(theme);
		}

		static void RegisterButtonStyle(Theme theme)
		{
			if (theme.GetControlStyle<Button>() is not null)
				return;

			var primary = theme.GetColor(EnvironmentKeys.ThemeColor.Primary);
			var onPrimary = theme.GetColor(EnvironmentKeys.ThemeColor.OnPrimary);

			var style = new ControlStyle<Button>();
			if (primary is not null)
				style.Set(EnvironmentKeys.Colors.Background, new SolidPaint(primary));
			if (onPrimary is not null)
				style.Set(EnvironmentKeys.Colors.Color, onPrimary);

			theme.SetControlStyle(style);
		}

		static void RegisterTextStyle(Theme theme)
		{
			if (theme.GetControlStyle<Text>() is not null)
				return;

			var onSurface = theme.GetColor(EnvironmentKeys.ThemeColor.OnSurface);

			var style = new ControlStyle<Text>();
			if (onSurface is not null)
				style.Set(EnvironmentKeys.Colors.Color, onSurface);

			theme.SetControlStyle(style);
		}

		static void RegisterTextFieldStyle(Theme theme)
		{
			if (theme.GetControlStyle<TextField>() is not null)
				return;

			var surfaceVariant = theme.GetColor(EnvironmentKeys.ThemeColor.SurfaceVariant);
			var onSurface = theme.GetColor(EnvironmentKeys.ThemeColor.OnSurface);
			var outline = theme.GetColor(EnvironmentKeys.ThemeColor.Outline);

			var style = new ControlStyle<TextField>();
			if (surfaceVariant is not null)
				style.Set(EnvironmentKeys.Colors.Background, new SolidPaint(surfaceVariant));
			if (onSurface is not null)
				style.Set(EnvironmentKeys.Colors.Color, onSurface);
			if (outline is not null)
				style.Set(EnvironmentKeys.Button.BorderColor, outline);

			theme.SetControlStyle(style);
		}

		static void RegisterToggleStyle(Theme theme)
		{
			if (theme.GetControlStyle<Toggle>() is not null)
				return;

			var primary = theme.GetColor(EnvironmentKeys.ThemeColor.Primary);

			var style = new ControlStyle<Toggle>();
			if (primary is not null)
				style.Set(EnvironmentKeys.Switch.OnColor, primary);

			theme.SetControlStyle(style);
		}

		static void RegisterSliderStyle(Theme theme)
		{
			if (theme.GetControlStyle<Slider>() is not null)
				return;

			var primary = theme.GetColor(EnvironmentKeys.ThemeColor.Primary);

			var style = new ControlStyle<Slider>();
			if (primary is not null)
				style.Set(EnvironmentKeys.Slider.ProgressColor, primary);

			theme.SetControlStyle(style);
		}
	}
}
