using System.Diagnostics;
using System.Reflection;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

/// <summary>
/// Fallback IAppInfo for GTK4/Linux, registered in DI by UseMauiAppLinuxGtk4.
/// Provides RequestedTheme from GTK settings so MAUI's
/// Application.PlatformAppTheme returns the correct value when the
/// Essentials package (LinuxAppInfo) is not used. MAUI's Essentials
/// initializer assigns the registration to the AppInfo facade.
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
			var psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
			psi.ArgumentList.Add("settings://");
			using var process = Process.Start(psi);
		}
		catch
		{
			// Ignore if no settings app is available
		}
	}
}
