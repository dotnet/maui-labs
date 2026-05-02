using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Graphics;

namespace Comet.Styles
{
	/// <summary>
	/// Owns the active theme reference and exposes it via the environment system.
	/// Supports a global theme plus scoped (per-subtree) theme overrides.
	/// </summary>
	/// <remarks>
	/// Spec: docs/architecture/STYLE_THEME_SPEC.md §6, §7.
	/// </remarks>
	public static class ThemeManager
	{
		/// <summary>
		/// Environment key for the active theme reference. Internal —
		/// consumers should go through <see cref="Current()"/> / <see cref="SetTheme"/>
		/// or <see cref="UseTheme{T}"/>.
		/// </summary>
		internal const string ActiveThemeKey = "Comet.Theme.Active";

		/// <summary>
		/// Fallback theme returned when no theme has been set. Set internally by
		/// the framework startup (see <c>AppHostBuilderExtensions.UseCometHandlers</c>).
		/// </summary>
		static Theme _defaultTheme;

		/// <summary>
		/// Gets the current theme from the nearest environment scope for a view.
		/// Walks the parent chain to find scoped <see cref="UseTheme{T}"/> overrides,
		/// then falls back to the global theme.
		/// </summary>
		public static Theme Current(View view)
		{
			if (view is null)
				return Current();

			var theme = view.GetEnvironment<Theme>(ActiveThemeKey);
			return theme ?? Current();
		}

		/// <summary>
		/// Gets the current theme from the global environment, or the registered
		/// default theme if none has been set.
		/// </summary>
		public static Theme Current()
		{
			var theme = View.GetGlobalEnvironment<Theme>(ActiveThemeKey);
			return theme ?? _defaultTheme;
		}

		/// <summary>
		/// Sets the active theme globally. Reactive — views that read tokens update.
		/// </summary>
		public static void SetTheme(Theme theme)
		{
			if (theme is null)
				throw new ArgumentNullException(nameof(theme));

			// Remember the latest applied theme as the default fallback so early
			// reads (before the environment is populated) still see a value.
			_defaultTheme = theme;

			View.SetGlobalEnvironment(ActiveThemeKey, theme);

			// Mark all views with a render body dirty so they re-render with new
			// theme values. Token reads resolve from ThemeManager.Current() so
			// a full re-render picks up the change.
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
			=> () => token.Resolve(Current());

		/// <summary>
		/// Returns a func that lazily resolves a color token from the nearest
		/// scoped theme for a specific view.
		/// </summary>
		public static Func<Color> TokenBinding(View view, Token<Color> token)
			=> () => token.Resolve(Current(view));

		/// <summary>
		/// Returns a func that lazily resolves a double token from the global theme.
		/// </summary>
		public static Func<double> TokenBinding(Token<double> token)
			=> () => token.Resolve(Current());

		/// <summary>
		/// Returns a func that lazily resolves a FontSpec token from the global theme.
		/// </summary>
		public static Func<FontSpec> TokenBinding(Token<FontSpec> token)
			=> () => token.Resolve(Current());
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
