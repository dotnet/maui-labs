namespace Platform.Maui.Linux.Gtk4.Platform;

internal static class GtkDesktopIntegration
{
	public static void ApplyAppIcon(Gtk.Window gtkWindow, string appBaseDirectory)
	{
		var appIconPath = GetPreferredAppIconPath(appBaseDirectory);
		if (string.IsNullOrWhiteSpace(appIconPath))
			return;

		var iconName = Path.GetFileNameWithoutExtension(appIconPath);
		if (string.IsNullOrWhiteSpace(iconName))
			return;

		if (Gdk.Display.GetDefault() is Gdk.Display display)
		{
			var appIconDirectory = Path.Combine(appBaseDirectory, "hicolor", "scalable", "apps");
			var iconTheme = Gtk.IconTheme.GetForDisplay(display);
			iconTheme.AddSearchPath(appBaseDirectory);
			iconTheme.AddSearchPath(Path.Combine(appBaseDirectory, "hicolor"));
			iconTheme.AddSearchPath(Path.Combine(appBaseDirectory, "hicolor", "scalable"));
			iconTheme.AddSearchPath(appIconDirectory);
		}

		Gtk.Window.SetDefaultIconName(iconName);
		gtkWindow.SetIconName(iconName);
	}

	public static void EnsureDesktopEntry(string applicationId, string applicationName, string appBaseDirectory)
	{
		if (string.IsNullOrWhiteSpace(applicationId))
			return;

		var iconPath = GetPreferredAppIconPath(appBaseDirectory);
		var executablePath = Environment.ProcessPath;
		if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath) ||
			string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
		{
			return;
		}

		var applicationsDirectory = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			".local",
			"share",
			"applications");
		Directory.CreateDirectory(applicationsDirectory);

		var desktopFilePath = Path.Combine(applicationsDirectory, $"{applicationId}.desktop");
		var desktopFileContents = string.Join('\n', new[]
		{
			"[Desktop Entry]",
			"Type=Application",
			$"Name={applicationName}",
			$"Exec={executablePath}",
			$"Icon={iconPath}",
			"Terminal=false",
			$"StartupWMClass={applicationId}",
			$"X-GNOME-WMClass={applicationId}",
			"Categories=Utility;",
			string.Empty,
		});

		File.WriteAllText(desktopFilePath, desktopFileContents);
	}

	public static string? GetPreferredAppIconPath(string appBaseDirectory)
	{
		var appIconDirectory = Path.Combine(appBaseDirectory, "hicolor", "scalable", "apps");
		if (!Directory.Exists(appIconDirectory))
			return null;

		return Directory.EnumerateFiles(appIconDirectory)
			.Where(static iconFilePath =>
			{
				var extension = Path.GetExtension(iconFilePath);
				return string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase)
					|| string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
					|| string.Equals(extension, ".ico", StringComparison.OrdinalIgnoreCase);
			})
			.Select(iconFilePath => new FileInfo(iconFilePath))
			.OrderByDescending(static info => info.LastWriteTimeUtc)
			.Select(static info => info.FullName)
			.FirstOrDefault();
	}
}
