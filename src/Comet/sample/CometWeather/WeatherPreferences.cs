namespace CometWeather;

public enum TemperatureUnit { Imperial, Metric, Hybrid }
public enum ThemeMode { Default, Dark, Light }

/// <summary>
/// Shared settings that affect all pages. Pages subscribe to SettingsChanged
/// and call SetState to re-render with updated colors/temperatures.
/// </summary>
public static class WeatherPreferences
{
	public static TemperatureUnit CurrentUnit { get; private set; } = TemperatureUnit.Imperial;
	public static ThemeMode CurrentTheme { get; private set; } = ThemeMode.Dark;

	public static event Action? SettingsChanged;

	public static void SetUnit(TemperatureUnit unit)
	{
		if (CurrentUnit == unit) return;
		CurrentUnit = unit;
		SettingsChanged?.Invoke();
	}

	public static void SetTheme(ThemeMode theme)
	{
		if (CurrentTheme == theme) return;
		CurrentTheme = theme;

		if (Microsoft.Maui.Controls.Application.Current != null)
		{
			Microsoft.Maui.Controls.Application.Current.UserAppTheme = theme switch
			{
				ThemeMode.Light => Microsoft.Maui.ApplicationModel.AppTheme.Light,
				ThemeMode.Dark => Microsoft.Maui.ApplicationModel.AppTheme.Dark,
				_ => Microsoft.Maui.ApplicationModel.AppTheme.Unspecified
			};
		}

		SettingsChanged?.Invoke();
	}

	public static string FormatTemperature(int fahrenheit) => CurrentUnit switch
	{
		TemperatureUnit.Metric => $"{FtoC(fahrenheit)}°C",
		TemperatureUnit.Hybrid => $"{fahrenheit}°F / {FtoC(fahrenheit)}°C",
		_ => $"{fahrenheit}°F"
	};

	public static string FormatTemperatureShort(int fahrenheit) => CurrentUnit switch
	{
		TemperatureUnit.Metric => $"{FtoC(fahrenheit)}°",
		_ => $"{fahrenheit}°"
	};

	static int FtoC(int f) => (int)Math.Round((f - 32) * 5.0 / 9.0);

	// Theme-aware colors
	public static Color Background => CurrentTheme == ThemeMode.Light
		? Color.FromArgb("#F0F4F8") : Color.FromArgb("#081B25");
	public static Color CardBg => CurrentTheme == ThemeMode.Light
		? Colors.White : Color.FromArgb("#0D2B3E");
	public static Color TextPrimary => CurrentTheme == ThemeMode.Light
		? Color.FromArgb("#1A2B3C") : Colors.White;
	public static Color TextSecondary => CurrentTheme == ThemeMode.Light
		? Color.FromArgb("#5A6B7C") : Color.FromArgb("#8BA3B4");
	public static Color Accent => Color.FromArgb("#1A6EBD");
	public static Color DividerColor => CurrentTheme == ThemeMode.Light
		? Color.FromArgb("#D0D8E0") : Color.FromArgb("#1E3A50");
}
