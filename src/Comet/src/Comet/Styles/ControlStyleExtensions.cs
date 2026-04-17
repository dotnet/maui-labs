using System;
using Comet.Styles;

// ReSharper disable once CheckNamespace
namespace Comet
{
	/// <summary>
	/// Extension methods for applying per-control-type styles via the environment.
	/// </summary>
	public static class ControlStyleExtensions
	{
		/// <summary>
		/// Sets the ButtonStyle for this view and its subtree.
		/// </summary>
		public static T ButtonStyle<T>(
			this T view,
			IControlStyle<Button, ButtonConfiguration> style) where T : View
		{
			view.SetEnvironment(StyleToken<Button>.Key, style, cascades: true);
			return view;
		}

		/// <summary>
		/// Sets the ToggleStyle for this view and its subtree.
		/// </summary>
		public static T ToggleStyle<T>(
			this T view,
			IControlStyle<Toggle, ToggleConfiguration> style) where T : View
		{
			view.SetEnvironment(StyleToken<Toggle>.Key, style, cascades: true);
			return view;
		}

		/// <summary>
		/// Sets the TextFieldStyle for this view and its subtree.
		/// </summary>
		public static T TextFieldStyle<T>(
			this T view,
			IControlStyle<TextField, TextFieldConfiguration> style) where T : View
		{
			view.SetEnvironment(StyleToken<TextField>.Key, style, cascades: true);
			return view;
		}

		/// <summary>
		/// Sets the SliderStyle for this view and its subtree.
		/// </summary>
		public static T SliderStyle<T>(
			this T view,
			IControlStyle<Slider, SliderConfiguration> style) where T : View
		{
			view.SetEnvironment(StyleToken<Slider>.Key, style, cascades: true);
			return view;
		}

		/// <summary>
		/// Resolves the current button style from environment or theme defaults.
		/// Checks scoped environment first, then falls back to the active theme's
		/// registered control style.
		/// </summary>
		public static ViewModifier ResolveCurrentStyle(this Button button, ButtonConfiguration config)
		{
			var style = button.GetEnvironment<IControlStyle<Button, ButtonConfiguration>>(
				StyleToken<Button>.Key);

			if (style is null)
			{
				var theme = ThemeManager.Current(button);
				style = theme.GetControlStyle<Button, ButtonConfiguration>();
			}

			if (style is null)
				return ViewModifier.Empty;

			return style.Resolve(config);
		}

		/// <summary>
		/// Resolves the current toggle style from environment or theme defaults.
		/// </summary>
		public static ViewModifier ResolveCurrentStyle(this Toggle toggle, ToggleConfiguration config)
		{
			var style = toggle.GetEnvironment<IControlStyle<Toggle, ToggleConfiguration>>(
				StyleToken<Toggle>.Key);

			if (style is null)
			{
				var theme = ThemeManager.Current(toggle);
				style = theme.GetControlStyle<Toggle, ToggleConfiguration>();
			}

			if (style is null)
				return ViewModifier.Empty;

			return style.Resolve(config);
		}

		/// <summary>
		/// Resolves the current text field style from environment or theme defaults.
		/// </summary>
		public static ViewModifier ResolveCurrentStyle(this TextField textField, TextFieldConfiguration config)
		{
			var style = textField.GetEnvironment<IControlStyle<TextField, TextFieldConfiguration>>(
				StyleToken<TextField>.Key);

			if (style is null)
			{
				var theme = ThemeManager.Current(textField);
				style = theme.GetControlStyle<TextField, TextFieldConfiguration>();
			}

			if (style is null)
				return ViewModifier.Empty;

			return style.Resolve(config);
		}

		/// <summary>
		/// Resolves the current slider style from environment or theme defaults.
		/// </summary>
		public static ViewModifier ResolveCurrentStyle(this Slider slider, SliderConfiguration config)
		{
			var style = slider.GetEnvironment<IControlStyle<Slider, SliderConfiguration>>(
				StyleToken<Slider>.Key);

			if (style is null)
			{
				var theme = ThemeManager.Current(slider);
				style = theme.GetControlStyle<Slider, SliderConfiguration>();
			}

			if (style is null)
				return ViewModifier.Empty;

			return style.Resolve(config);
		}
	}
}
