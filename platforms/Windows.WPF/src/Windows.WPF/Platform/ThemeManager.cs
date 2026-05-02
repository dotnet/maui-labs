using System;
using Microsoft.Win32;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	/// <summary>
	/// Detects Windows dark/light theme, sets PlatformAppTheme on the MAUI Application,
	/// and fires RequestedThemeChanged so AppThemeBinding values resolve correctly.
	/// </summary>
	public static class ThemeManager
	{
		static AppTheme _currentTheme = AppTheme.Unspecified;

		/// <summary>
		/// Fired when theme changes (system or user). Shell chrome subscribes to this.
		/// </summary>
		public static event Action<AppTheme>? ThemeChanged;

		public static AppTheme GetCurrentTheme()
		{
			try
			{
				using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
				if (key != null)
				{
					var value = key.GetValue("AppsUseLightTheme");
					if (value is int i)
						return i == 0 ? AppTheme.Dark : AppTheme.Light;
				}
			}
			catch { }
			return AppTheme.Light;
		}

		public static void Initialize()
		{
			_currentTheme = GetCurrentTheme();

			// Listen for system theme changes
			SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
		}

		/// <summary>
		/// Must be called after the MAUI Application is created to set the initial PlatformAppTheme.
		/// This enables AppThemeBinding to resolve Light/Dark values correctly.
		/// Also hooks into Application.RequestedThemeChanged to detect UserAppTheme changes.
		/// </summary>
		public static void ApplyThemeToApplication()
		{
			var app = Microsoft.Maui.Controls.Application.Current;
			if (app == null) return;

			_currentTheme = GetCurrentTheme();
			SetPlatformAppTheme(app, _currentTheme);

			// Subscribe to RequestedThemeChanged to catch UserAppTheme changes from the app
			app.RequestedThemeChanged += OnRequestedThemeChanged;
		}

		static void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
		{
			_currentTheme = e.RequestedTheme;
			ThemeChanged?.Invoke(e.RequestedTheme);
		}

		/// <summary>
		/// Called when the user sets UserAppTheme from application code.
		/// Updates PlatformAppTheme to match and triggers theme re-evaluation.
		/// </summary>
		public static void SetUserTheme(AppTheme theme)
		{
			var app = Microsoft.Maui.Controls.Application.Current;
			if (app == null) return;

			if (theme == AppTheme.Unspecified)
			{
				// Reset to system theme
				_currentTheme = GetCurrentTheme();
				SetPlatformAppTheme(app, _currentTheme);
			}
			else
			{
				_currentTheme = theme;
				SetPlatformAppTheme(app, theme);
			}
		}

		static void SetPlatformAppTheme(Microsoft.Maui.Controls.Application app, AppTheme theme)
		{
			try
			{
				// PlatformAppTheme has a public setter at runtime but is hidden at compile time
				var prop = typeof(Microsoft.Maui.Controls.Application).GetProperty("PlatformAppTheme",
					System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
				prop?.SetValue(app, theme);
			}
			catch { }
		}

		static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
		{
			if (e.Category == UserPreferenceCategory.General)
			{
				var newTheme = GetCurrentTheme();
				if (newTheme != _currentTheme)
				{
					_currentTheme = newTheme;
					var app = Microsoft.Maui.Controls.Application.Current;
					if (app != null)
					{
						SetPlatformAppTheme(app, newTheme);
					}
					ThemeChanged?.Invoke(newTheme);
				}
			}
		}

		public static void Shutdown()
		{
			SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
			var app = Microsoft.Maui.Controls.Application.Current;
			if (app != null)
				app.RequestedThemeChanged -= OnRequestedThemeChanged;
		}
	}
}
