using System;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Comet.Styles
{
	/// <summary>
	/// Fluent extensions that wire views to the current theme's semantic color tokens.
	/// These read from <see cref="Theme.Current"/> at call time and push values
	/// through the environment. Explicit overrides (e.g. <c>.Background(Colors.Red)</c>)
	/// always win because they write to the same environment keys.
	/// </summary>
	public static class ThemeExtensions
	{
		/// <summary>
		/// Sets the view's background from a semantic theme color key.
		/// <example><c>button.ThemeBackground(EnvironmentKeys.ThemeColor.Primary)</c></example>
		/// </summary>
		public static T ThemeBackground<T>(this T view, string themeColorKey) where T : View
		{
			var color = Theme.Current.GetColor(themeColorKey);
			if (color is not null)
				view.SetEnvironment(nameof(IView.Background), (Paint)new SolidPaint(color), false);
			return view;
		}

		/// <summary>
		/// Shorthand: sets background from <see cref="EnvironmentKeys.ThemeColor.Background"/>.
		/// </summary>
		public static T ThemeBackground<T>(this T view) where T : View
			=> view.ThemeBackground(EnvironmentKeys.ThemeColor.Background);

		/// <summary>
		/// Sets the view's text/foreground color from a semantic theme color key.
		/// <example><c>text.ThemeForeground(EnvironmentKeys.ThemeColor.OnSurface)</c></example>
		/// </summary>
		public static T ThemeForeground<T>(this T view, string themeColorKey) where T : View
		{
			var color = Theme.Current.GetColor(themeColorKey);
			if (color is not null)
				view.SetEnvironment(EnvironmentKeys.Colors.Color, color, false);
			return view;
		}

		/// <summary>
		/// Shorthand: sets foreground from <see cref="EnvironmentKeys.ThemeColor.OnSurface"/>.
		/// </summary>
		public static T ThemeForeground<T>(this T view) where T : View
			=> view.ThemeForeground(EnvironmentKeys.ThemeColor.OnSurface);

		/// <summary>
		/// Sets both background and foreground from the current theme's color scheme
		/// using the given semantic key and its "On" counterpart.
		/// <example><c>button.ThemeColors(EnvironmentKeys.ThemeColor.Primary, EnvironmentKeys.ThemeColor.OnPrimary)</c></example>
		/// </summary>
		public static T ThemeColors<T>(this T view, string backgroundKey, string foregroundKey) where T : View
		{
			view.ThemeBackground(backgroundKey);
			view.ThemeForeground(foregroundKey);
			return view;
		}
	}
}
