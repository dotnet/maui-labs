namespace CometBaristaNotes.Services;

public enum AppThemeMode { Light, Dark, System }

public interface IThemeService
{
	AppThemeMode CurrentMode { get; }
	event Action<AppThemeMode> ThemeChanged;
	void SetTheme(AppThemeMode mode);
	void LoadSavedTheme();
}
