using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Maui.Graphics;
using MauiColors = Microsoft.Maui.Graphics.Colors;

namespace Comet.Styles
{
	public enum AppTheme
	{
		Light,
		Dark,
		System
	}

	public class Theme
	{
		private static Theme _current;
		public static Theme Current
		{
			get => _current ??= new Theme();
			set
			{
				var old = _current;
				_current = value;
				if (old != value && value is not null)
				{
					value.Apply();
					ThemeChanged?.Invoke(value);
				}
			}
		}

		/// <summary>
		/// Raised when <see cref="Current"/> is changed.
		/// </summary>
		public static event Action<Theme> ThemeChanged;

		public AppTheme CurrentTheme { get; set; } = AppTheme.System;

		// Legacy simple color properties — kept for backward compatibility
		public Color PrimaryColor { get; set; } = new Color(0.32f, 0.17f, 0.83f); // #512BD4
		public Color SecondaryColor { get; set; } = new Color(0.87f, 0.32f, 0.15f);
		public Color BackgroundColor { get; set; } = MauiColors.White;
		public Color SurfaceColor { get; set; } = new Color(0.96f, 0.96f, 0.96f);
		public Color TextColor { get; set; } = MauiColors.Black;
		public Color SecondaryTextColor { get; set; } = new Color(0.4f, 0.4f, 0.4f);
		public Color ErrorColor { get; set; } = new Color(0.7f, 0.11f, 0.11f);

		/// <summary>
		/// Rich semantic color scheme following Material Design 3 conventions.
		/// When null, <see cref="Apply"/> uses the legacy simple color properties.
		/// </summary>
		public ThemeColors ColorScheme { get; set; }

		// --- New token-based style system (spec §5.2) ---

		/// <summary>
		/// Human-readable theme name (e.g., "Light", "Dark", "BrandOcean").
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Color tokens — primary, secondary, surface, error, etc.
		/// </summary>
		public ColorTokenSet Colors { get; set; }

		/// <summary>
		/// Typography tokens — display, headline, title, body, label sizes/weights.
		/// </summary>
		public TypographyTokenSet Typography { get; set; }

		/// <summary>
		/// Spacing tokens — compact, standard, comfortable.
		/// </summary>
		public SpacingTokenSet Spacing { get; set; }

		/// <summary>
		/// Shape tokens — corner radii for small, medium, large containers.
		/// </summary>
		public ShapeTokenSet Shapes { get; set; }

		/// <summary>
		/// Per-control type style defaults for the new token system.
		/// Uses ImmutableDictionary so derived themes get independent copies.
		/// </summary>
		ImmutableDictionary<Type, object> _newControlStyles =
			ImmutableDictionary<Type, object>.Empty;

		/// <summary>
		/// Sets a control style in the new token-based system.
		/// </summary>
		public Theme SetControlStyle<TControl, TConfig>(object style)
			where TControl : View
			where TConfig : struct
		{
			_newControlStyles = _newControlStyles.SetItem(typeof(TControl), style);
			return this;
		}

		/// <summary>
		/// Gets a control style from the new token-based system.
		/// </summary>
		public object GetNewControlStyle<TControl>()
			where TControl : View
		{
			return _newControlStyles.TryGetValue(typeof(TControl), out var s) ? s : null;
		}

		readonly Dictionary<Type, object> _controlStyles = new Dictionary<Type, object>();

		/// <summary>
		/// Registers a typed control style for this theme.
		/// </summary>
		public Theme SetControlStyle<T>(ControlStyle<T> style) where T : View
		{
			_controlStyles[typeof(T)] = style;
			return this;
		}

		/// <summary>
		/// Gets the registered control style for a view type, or null if none registered.
		/// </summary>
		public ControlStyle<T> GetControlStyle<T>() where T : View
		{
			if (_controlStyles.TryGetValue(typeof(T), out var style))
				return (ControlStyle<T>)style;
			return null;
		}

		/// <summary>
		/// Pushes this theme's values into the environment system.
		/// When target is null, values are set globally (all views).
		/// When target is provided, values are scoped to that view subtree.
		/// </summary>
		public virtual void Apply(ContextualObject target = null)
		{
			// Push semantic color tokens if a ColorScheme is configured
			ColorScheme?.ApplyToEnvironment(target);

			// Register default control styles for any controls that don't
			// already have a custom style. This ensures themed controls
			// "just work" without explicit setup.
			DefaultThemeStyles.Register(this);

			// Apply registered per-control styles
			foreach (var kvp in _controlStyles)
			{
				if (kvp.Value is IControlStyleApplicable applicable)
					applicable.Apply(target);
			}

			// Notify any IThemeable views
			if (target is null)
			{
				NotifyThemeableViews();
			}
			else if (target is IThemeable themeable)
			{
				themeable.ApplyTheme(this);
			}
		}

		void NotifyThemeableViews()
		{
			List<View> views;
			lock (View.ActiveViewsLock)
				views = View.ActiveViews.OfType<View>().ToList();

			foreach (var view in views)
			{
				if (view is IThemeable themeable)
					themeable.ApplyTheme(this);
			}
		}

		/// <summary>
		/// Resolves a semantic color by its environment key from this theme.
		/// Checks <see cref="ColorScheme"/> first, then falls back to legacy properties.
		/// </summary>
		public Color GetColor(string themeColorKey)
		{
			if (ColorScheme is not null)
			{
				var color = ResolveFromColorScheme(themeColorKey);
				if (color is not null)
					return color;
			}

			return ResolveLegacyColor(themeColorKey);
		}

		Color ResolveFromColorScheme(string key) => key switch
		{
			EnvironmentKeys.ThemeColor.Primary => ColorScheme.Primary,
			EnvironmentKeys.ThemeColor.OnPrimary => ColorScheme.OnPrimary,
			EnvironmentKeys.ThemeColor.PrimaryContainer => ColorScheme.PrimaryContainer,
			EnvironmentKeys.ThemeColor.OnPrimaryContainer => ColorScheme.OnPrimaryContainer,
			EnvironmentKeys.ThemeColor.Secondary => ColorScheme.Secondary,
			EnvironmentKeys.ThemeColor.OnSecondary => ColorScheme.OnSecondary,
			EnvironmentKeys.ThemeColor.SecondaryContainer => ColorScheme.SecondaryContainer,
			EnvironmentKeys.ThemeColor.OnSecondaryContainer => ColorScheme.OnSecondaryContainer,
			EnvironmentKeys.ThemeColor.Tertiary => ColorScheme.Tertiary,
			EnvironmentKeys.ThemeColor.OnTertiary => ColorScheme.OnTertiary,
			EnvironmentKeys.ThemeColor.TertiaryContainer => ColorScheme.TertiaryContainer,
			EnvironmentKeys.ThemeColor.OnTertiaryContainer => ColorScheme.OnTertiaryContainer,
			EnvironmentKeys.ThemeColor.Error => ColorScheme.Error,
			EnvironmentKeys.ThemeColor.OnError => ColorScheme.OnError,
			EnvironmentKeys.ThemeColor.ErrorContainer => ColorScheme.ErrorContainer,
			EnvironmentKeys.ThemeColor.OnErrorContainer => ColorScheme.OnErrorContainer,
			EnvironmentKeys.ThemeColor.Background => ColorScheme.Background,
			EnvironmentKeys.ThemeColor.OnBackground => ColorScheme.OnBackground,
			EnvironmentKeys.ThemeColor.Surface => ColorScheme.Surface,
			EnvironmentKeys.ThemeColor.OnSurface => ColorScheme.OnSurface,
			EnvironmentKeys.ThemeColor.SurfaceVariant => ColorScheme.SurfaceVariant,
			EnvironmentKeys.ThemeColor.OnSurfaceVariant => ColorScheme.OnSurfaceVariant,
			EnvironmentKeys.ThemeColor.Outline => ColorScheme.Outline,
			EnvironmentKeys.ThemeColor.OutlineVariant => ColorScheme.OutlineVariant,
			EnvironmentKeys.ThemeColor.InverseSurface => ColorScheme.InverseSurface,
			EnvironmentKeys.ThemeColor.InverseOnSurface => ColorScheme.InverseOnSurface,
			EnvironmentKeys.ThemeColor.InversePrimary => ColorScheme.InversePrimary,
			_ => null,
		};

		Color ResolveLegacyColor(string key) => key switch
		{
			EnvironmentKeys.ThemeColor.Primary => PrimaryColor,
			EnvironmentKeys.ThemeColor.Secondary => SecondaryColor,
			EnvironmentKeys.ThemeColor.Background => BackgroundColor,
			EnvironmentKeys.ThemeColor.Surface => SurfaceColor,
			EnvironmentKeys.ThemeColor.OnBackground => TextColor,
			EnvironmentKeys.ThemeColor.OnSurface => TextColor,
			EnvironmentKeys.ThemeColor.OnSurfaceVariant => SecondaryTextColor,
			EnvironmentKeys.ThemeColor.Error => ErrorColor,
			_ => null,
		};

		public static Theme Light => new Theme
		{
			CurrentTheme = AppTheme.Light,
			BackgroundColor = MauiColors.White,
			SurfaceColor = new Color(0.96f, 0.96f, 0.96f),
			TextColor = MauiColors.Black,
			SecondaryTextColor = new Color(0.4f, 0.4f, 0.4f),
			ColorScheme = ThemeColors.LightScheme,
		};

		public static Theme Dark => new Theme
		{
			CurrentTheme = AppTheme.Dark,
			BackgroundColor = new Color(0.07f, 0.07f, 0.07f),
			SurfaceColor = new Color(0.15f, 0.15f, 0.15f),
			TextColor = MauiColors.White,
			SecondaryTextColor = new Color(0.7f, 0.7f, 0.7f),
			ColorScheme = ThemeColors.DarkScheme,
		};
	}

	/// <summary>
	/// Internal interface so Theme.Apply can invoke untyped ControlStyle instances.
	/// </summary>
	internal interface IControlStyleApplicable
	{
		void Apply(ContextualObject target);
	}
}

