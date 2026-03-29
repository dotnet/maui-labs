using System.Reflection;
using Microsoft.Maui.ApplicationModel;

namespace Platform.Maui.Linux.Gtk4.Essentials.AppModel;

public class LinuxAppInfo : IAppInfo
{
	private readonly Lazy<Assembly> _entry = new(() =>
		Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly());

	public string PackageName => _entry.Value.GetName().Name ?? "unknown";
	public string Name => _entry.Value.GetCustomAttribute<AssemblyTitleAttribute>()?.Title
		?? _entry.Value.GetName().Name ?? "unknown";
	public string VersionString => _entry.Value.GetName().Version?.ToString() ?? "1.0.0";
	public Version Version => _entry.Value.GetName().Version ?? new Version(1, 0, 0);
	public string BuildString => _entry.Value.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
		?.InformationalVersion ?? VersionString;

	public AppTheme RequestedTheme
	{
		get
		{
			try
			{
				var settings = Gtk.Settings.GetDefault();
				if (settings is null) return AppTheme.Unspecified;
				return settings.GtkApplicationPreferDarkTheme ? AppTheme.Dark : AppTheme.Light;
			}
			catch
			{
				return AppTheme.Unspecified;
			}
		}
	}

	public AppPackagingModel PackagingModel => AppPackagingModel.Unpackaged;
	public LayoutDirection RequestedLayoutDirection => LayoutDirection.LeftToRight;

	public void ShowSettingsUI()
	{
		// Open system settings via xdg-open; best-effort
		try { System.Diagnostics.Process.Start("xdg-open", "gnome-control-center"); }
		catch { }
	}
}
