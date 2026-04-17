using System;
using System.Collections.Immutable;

namespace Comet.Styles
{
	/// <summary>
	/// A theme is a named, self-contained collection of design tokens
	/// (colors, typography, spacing, shapes) plus per-control style defaults.
	/// Themes are immutable value records — compose new themes with the
	/// <c>with</c> expression, activate via <see cref="ThemeManager.SetTheme"/>.
	/// </summary>
	/// <remarks>
	/// Spec: docs/architecture/STYLE_THEME_SPEC.md §5.2.
	/// </remarks>
	public record Theme
	{
		/// <summary>Identifies this theme (e.g., "Light", "Dark", "BrandOcean").</summary>
		public required string Name { get; init; }

		/// <summary>Color tokens — primary, secondary, surface, error, etc.</summary>
		public required ColorTokenSet Colors { get; init; }

		/// <summary>Typography tokens — display, headline, title, body, label sizes/weights.</summary>
		public required TypographyTokenSet Typography { get; init; }

		/// <summary>Spacing tokens — compact, standard, comfortable.</summary>
		public required SpacingTokenSet Spacing { get; init; }

		/// <summary>Shape tokens — corner radii for small, medium, large containers.</summary>
		public required ShapeTokenSet Shapes { get; init; }

		/// <summary>
		/// Per-control type style defaults.
		/// Stored as an <see cref="ImmutableDictionary{TKey,TValue}"/> so that
		/// <c>with</c> expressions produce independent copies — mutating a derived
		/// theme's control styles cannot alias or corrupt the base theme's styles.
		/// </summary>
		ImmutableDictionary<Type, object> _controlStyles =
			ImmutableDictionary<Type, object>.Empty;

		/// <summary>
		/// Sets the default style for a control type within this theme.
		/// Returns <c>this</c> for fluent chaining. Internally replaces the
		/// immutable dictionary, so this is safe on <c>with</c>-derived themes.
		/// </summary>
		public Theme SetControlStyle<TControl, TConfig>(
			IControlStyle<TControl, TConfig> style)
			where TControl : View
			where TConfig : struct
		{
			_controlStyles = _controlStyles.SetItem(typeof(TControl), style);
			return this;
		}

		/// <summary>
		/// Gets the control style for <typeparamref name="TControl"/>,
		/// or <c>null</c> if no default has been registered.
		/// </summary>
		public IControlStyle<TControl, TConfig> GetControlStyle<TControl, TConfig>()
			where TControl : View
			where TConfig : struct
		{
			return _controlStyles.TryGetValue(typeof(TControl), out var s)
				? (IControlStyle<TControl, TConfig>)s
				: null;
		}
	}
}
