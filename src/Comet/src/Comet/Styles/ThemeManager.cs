using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Graphics;

namespace Comet.Styles
{
	/// <summary>
	/// Manages the active theme reference via the environment system.
	/// Supports both global and scoped (per-subtree) theme overrides.
	/// </summary>
	public static class ThemeManager
	{
		/// <summary>
		/// Environment key for the active theme reference.
		/// </summary>
		internal static readonly string ActiveThemeKey = "Comet.Theme.Active";

		/// <summary>
		/// Gets the current theme from the nearest environment scope for a view.
		/// Walks the parent chain to find scoped .Theme() overrides.
		/// </summary>
		public static Theme Current(View view)
		{
			if (view is null)
				return Current();

			var theme = view.GetEnvironment<Theme>(ActiveThemeKey);
			return theme ?? Current();
		}

		/// <summary>
		/// Gets the current theme from the global environment.
		/// </summary>
		public static Theme Current()
		{
			var theme = View.GetGlobalEnvironment<Theme>(ActiveThemeKey);
			if (theme is not null)
				return theme;

			// Fall back to the legacy Theme.Current if no new-style theme is set
			return Theme.Current ?? Defaults.Light;
		}

		/// <summary>
		/// Sets the active theme globally. Reactive — views that read tokens update.
		/// Also syncs MAUI's Application.Current.UserAppTheme so platform chrome
		/// (navigation bar, status bar, page backgrounds) follows the theme.
		/// </summary>
		public static void SetTheme(Theme theme)
		{
			View.SetGlobalEnvironment(ActiveThemeKey, theme);

			// Sync with MAUI's theme system so native chrome respects the theme
			SyncMauiAppTheme(theme);

			// Mark all views with a render body dirty so they re-render with new theme
			// colors. This includes Component<T> (IComponentWithState) and CometApp
			// subclasses that use Body = func. CoffeeColors properties resolve from
			// ThemeManager.Current() so a full re-render picks up new values.
			ThreadHelper.RunOnMainThread(() =>
			{
				List<View> views;
				lock (View.ActiveViewsLock)
					views = View.ActiveViews.OfType<View>().ToList();
				foreach (var v in views)
				{
					if (v is IComponentWithState || v.Body is not null)
						Reactive.ReactiveScheduler.MarkViewDirty(v);
				}
			});
		}

		/// <summary>
		/// Maps the Comet theme's CurrentTheme to MAUI's UserAppTheme property
		/// so navigation bars, status bars and other platform chrome follow along.
		/// </summary>
		static void SyncMauiAppTheme(Theme theme)
		{
			if (theme is null)
				return;

			try
			{
				var app = Microsoft.Maui.Controls.Application.Current;
				if (app is null)
					return;

				switch (theme.CurrentTheme)
				{
					case AppTheme.Dark:
						app.UserAppTheme = Microsoft.Maui.ApplicationModel.AppTheme.Dark;
						break;
					case AppTheme.Light:
						app.UserAppTheme = Microsoft.Maui.ApplicationModel.AppTheme.Light;
						break;
					default:
						app.UserAppTheme = Microsoft.Maui.ApplicationModel.AppTheme.Unspecified;
						break;
				}
			}
			catch
			{
				// Application.Current may not be available during startup or tests
			}
		}

		/// <summary>
		/// Sets a scoped theme override on a subtree.
		/// Children resolve tokens from this theme instead of the global one.
		/// </summary>
		public static T UseTheme<T>(this T view, Theme theme) where T : View
		{
			view.SetEnvironment(ActiveThemeKey, theme, cascades: true);
			return view;
		}

		/// <summary>
		/// Returns a func that lazily resolves a color token from the global theme.
		/// When the theme changes, the func re-evaluates.
		/// </summary>
		public static Func<Color> TokenBinding(Token<Color> token)
			=> () =>
			{
				var theme = Current();
				return token.Resolve(theme);
			};

		/// <summary>
		/// Returns a func that lazily resolves a color token from the nearest
		/// scoped theme for a specific view.
		/// </summary>
		public static Func<Color> TokenBinding(View view, Token<Color> token)
			=> () =>
			{
				var theme = Current(view);
				return token.Resolve(theme);
			};

		/// <summary>
		/// Returns a func that lazily resolves a double token from the global theme.
		/// </summary>
		public static Func<double> TokenBinding(Token<double> token)
			=> () =>
			{
				var theme = Current();
				return token.Resolve(theme);
			};

		/// <summary>
		/// Returns a func that lazily resolves a FontSpec token from the global theme.
		/// </summary>
		public static Func<FontSpec> TokenBinding(Token<FontSpec> token)
			=> () =>
			{
				var theme = Current();
				return token.Resolve(theme);
			};
	}

	/// <summary>
	/// Extension methods for token overrides on views.
	/// </summary>
	public static class TokenOverrideExtensions
	{
		/// <summary>
		/// Overrides a single color token for this view's subtree.
		/// </summary>
		public static T OverrideToken<T>(this T view, Token<Color> token, Color value) where T : View
		{
			view.SetEnvironment(token.Key, value, cascades: true);
			return view;
		}

		/// <summary>
		/// Overrides a single double token for this view's subtree.
		/// </summary>
		public static T OverrideToken<T>(this T view, Token<double> token, double value) where T : View
		{
			view.SetEnvironment(token.Key, value, cascades: true);
			return view;
		}

		/// <summary>
		/// Overrides a single FontSpec token for this view's subtree.
		/// </summary>
		public static T OverrideToken<T>(this T view, Token<FontSpec> token, FontSpec value) where T : View
		{
			view.SetEnvironment(token.Key, value, cascades: true);
			return view;
		}
	}

	/// <summary>
	/// Typography convenience extension (spec Decision D3).
	/// Applies all font properties from a FontSpec token in a single call.
	/// </summary>
	public static class TypographyExtensions
	{
		/// <summary>
		/// Applies size, weight, and family from a FontSpec token.
		/// </summary>
		public static T Typography<T>(this T view, Token<FontSpec> token) where T : View
		{
			view.FontSize((Func<double>)(() => view.GetToken(token).Size));
			view.FontWeight((Func<Microsoft.Maui.FontWeight>)(() => view.GetToken(token).Weight));
			view.FontFamily((Func<string>)(() =>
			{
				var spec = view.GetToken(token);
				return spec.Family;
			}));
			return view;
		}
	}
}
