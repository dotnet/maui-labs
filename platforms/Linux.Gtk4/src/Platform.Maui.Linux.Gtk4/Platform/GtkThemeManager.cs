using Microsoft.Maui;

namespace Platform.Maui.Linux.Gtk4.Platform;

/// <summary>
/// Theme/dark mode support for GTK4.
/// Maps MAUI AppTheme to GTK4 settings.
/// </summary>
public static class GtkThemeManager
{
	static bool _monitoring;
	static string? _systemThemeName;
	static AppTheme _systemTheme = AppTheme.Unspecified;
	static bool _suppressNotify;

	/// <summary>
	/// Sets the GTK4 application theme based on MAUI AppTheme.
	/// Switches both the prefer-dark-theme flag and the actual GTK theme name
	/// (e.g., Adwaita ↔ Adwaita-dark, Yaru ↔ Yaru-dark).
	/// </summary>
	public static void SetTheme(AppTheme theme)
	{
		var settings = Gtk.Settings.GetDefault();
		if (settings == null) return;

		// Save original system theme on first call
		if (_systemThemeName == null)
		{
			_systemThemeName = settings.GtkThemeName;
			_systemTheme = DetectThemeFromName(_systemThemeName);
		}

		_suppressNotify = true;
		try
		{
			settings.GtkApplicationPreferDarkTheme = theme == AppTheme.Dark;

			var baseName = (_systemThemeName ?? "Adwaita").Replace("-dark", "");
			var targetTheme = theme == AppTheme.Dark ? $"{baseName}-dark" : baseName;
			settings.GtkThemeName = targetTheme;
		}
		finally
		{
			_suppressNotify = false;
		}
	}

	/// <summary>
	/// Reverts to the system default theme.
	/// </summary>
	public static void RevertToSystemTheme()
	{
		if (_systemThemeName == null) return;

		var settings = Gtk.Settings.GetDefault();
		if (settings == null) return;

		_suppressNotify = true;
		try
		{
			settings.GtkThemeName = _systemThemeName;
			settings.GtkApplicationPreferDarkTheme = _systemThemeName.Contains("-dark");
		}
		finally
		{
			_suppressNotify = false;
		}
	}

	/// <summary>
	/// Gets the current SYSTEM theme as a MAUI AppTheme.
	/// Always returns the system/desktop theme, not any app-level override.
	/// This is used by AppInfo.RequestedTheme so MAUI's PlatformAppTheme
	/// always reflects the OS preference (same pattern as iOS/macOS/Windows).
	/// </summary>
	public static AppTheme GetCurrentTheme()
	{
		// If we've captured the system theme, return it.
		// It only changes via StartMonitoring when the desktop theme changes.
		if (_systemTheme != AppTheme.Unspecified)
			return _systemTheme;

		// First call — detect from GTK settings
		var settings = Gtk.Settings.GetDefault();
		if (settings == null)
			return AppTheme.Unspecified;

		_systemThemeName ??= settings.GtkThemeName;
		_systemTheme = DetectThemeFromName(settings.GtkThemeName);
		return _systemTheme;
	}

	static AppTheme DetectThemeFromName(string? themeName)
	{
		if (string.IsNullOrEmpty(themeName))
			return AppTheme.Light;

		return themeName.EndsWith("-dark", StringComparison.OrdinalIgnoreCase)
			? AppTheme.Dark
			: AppTheme.Light;
	}

	/// <summary>
	/// Starts monitoring GTK settings for system theme changes.
	/// Fires IApplication.ThemeChanged() when the system theme switches.
	/// </summary>
	public static void StartMonitoring()
	{
		if (_monitoring) return;
		_monitoring = true;

		var settings = Gtk.Settings.GetDefault();
		if (settings == null) return;

		settings.OnNotify += (sender, args) =>
		{
			if (_suppressNotify) return;

			if (args.Pspec.GetName() is "gtk-application-prefer-dark-theme" or "gtk-theme-name")
			{
				// System theme actually changed (not our override) — update cached value
				_systemThemeName = settings.GtkThemeName;
				_systemTheme = DetectThemeFromName(_systemThemeName);

				var app = IPlatformApplication.Current as GtkMauiApplication;
				(app?.Application as IApplication)?.ThemeChanged();
			}
		};
	}

	/// <summary>
	/// Applies custom CSS to the entire application.
	/// </summary>
	public static void ApplyCustomCss(string css)
	{
		var provider = Gtk.CssProvider.New();
		provider.LoadFromString(css);
		var display = Gdk.Display.GetDefault();
		if (display != null)
		{
			Gtk.StyleContext.AddProviderForDisplay(display, provider, 800);
		}
	}
}
