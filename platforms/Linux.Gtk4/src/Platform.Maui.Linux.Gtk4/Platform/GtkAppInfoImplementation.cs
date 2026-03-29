using System.Diagnostics;
using System.Reflection;
using Microsoft.Maui.ApplicationModel;

namespace Platform.Maui.Linux.Gtk4.Platform;

/// <summary>
/// IAppInfo implementation for GTK4/Linux.
/// Provides RequestedTheme from GTK settings so MAUI's
/// Application.PlatformAppTheme returns the correct value.
/// </summary>
class GtkAppInfoImplementation : IAppInfo
{
	public string PackageName => Assembly.GetEntryAssembly()?.GetName().Name ?? string.Empty;

	public string Name => Assembly.GetEntryAssembly()?.GetName().Name ?? string.Empty;

	public Version Version => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0);

	public string VersionString => Version.ToString();

	public string BuildString => "1";

	public AppPackagingModel PackagingModel => AppPackagingModel.Unpackaged;

	public AppTheme RequestedTheme => GtkThemeManager.GetCurrentTheme();

	public LayoutDirection RequestedLayoutDirection => LayoutDirection.LeftToRight;

	public void ShowSettingsUI()
	{
		try
		{
			Process.Start(new ProcessStartInfo("xdg-open", "settings://") { UseShellExecute = false });
		}
		catch
		{
			// Ignore if no settings app is available
		}
	}

	/// <summary>
	/// Sets this implementation as the static default so AppInfo.RequestedTheme works.
	/// </summary>
	public static void Register()
	{
		var instance = new GtkAppInfoImplementation();

		// Set the static field used by AppInfo's static accessors
		var field = typeof(AppInfo).GetField("currentImplementation", BindingFlags.Static | BindingFlags.NonPublic);
		field?.SetValue(null, instance);
	}
}
