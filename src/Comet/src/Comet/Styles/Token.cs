using System;

namespace Comet.Styles
{
	/// <summary>
	/// A strongly-typed environment key that can resolve its value from a Theme.
	/// Zero-allocation on the read path — uses a pre-built resolver delegate.
	/// </summary>
	/// <typeparam name="T">The type of value stored at this key.</typeparam>
	public sealed class Token<T>
	{
		/// <summary>
		/// The underlying string key used in EnvironmentData storage.
		/// Internal to prevent direct string-key usage in consuming code.
		/// </summary>
		internal string Key { get; }

		/// <summary>
		/// Human-readable name for debugging and IntelliSense.
		/// </summary>
		public string Name { get; }

		/// <summary>
		/// Default value when no theme or environment provides one.
		/// </summary>
		public T DefaultValue { get; }

		/// <summary>
		/// Resolver function that extracts this token's value from a Theme.
		/// Set via init-only property at construction time.
		/// </summary>
		internal Func<Theme, T> Resolver { get; init; }

		public Token(string key, string name = null, T defaultValue = default)
		{
			Key = key;
			Name = name ?? key;
			DefaultValue = defaultValue;
		}

		/// <summary>
		/// Constructor with resolver for token identifier classes.
		/// </summary>
		public Token(string key, Func<Theme, T> resolver, string name = null, T defaultValue = default)
		{
			Key = key;
			Resolver = resolver;
			Name = name ?? key;
			DefaultValue = defaultValue;
		}

		/// <summary>
		/// Extract this token's value from the given theme.
		/// Falls back to DefaultValue if no resolver is set or theme is null.
		/// </summary>
		public T Resolve(Theme theme)
		{
			if (theme is not null && Resolver is not null)
				return Resolver(theme);
			return DefaultValue;
		}

		/// <summary>
		/// Resolves this token from the nearest scoped theme for a view.
		/// Walks the parent chain to find scoped .Theme() overrides.
		/// </summary>
		public T Resolve(View view)
		{
			if (view is null)
				return Resolve(ThemeManager.Current());
			return Resolve(ThemeManager.Current(view));
		}

		/// <summary>
		/// Projects this token through a transform, returning a Func.
		/// Useful for extracting individual properties from composite tokens.
		/// </summary>
		public Func<TResult> Map<TResult>(Func<T, TResult> transform)
			=> () => transform(Resolve(ThemeManager.Current()));
	}

	/// <summary>
	/// Extension methods for view-aware token resolution.
	/// </summary>
	public static class TokenExtensions
	{
		/// <summary>
		/// Resolves a token from the nearest scoped theme, with support for
		/// direct token overrides in the environment.
		/// </summary>
		public static T GetToken<T>(this View view, Token<T> token)
		{
			// Check for direct token override in the environment (presence detection)
			if (view.TryGetEnvironment<T>(token.Key, out var directOverride))
				return directOverride;

			// Resolve from the nearest scoped theme
			var theme = ThemeManager.Current(view);
			return token.Resolve(theme);
		}
	}
}
