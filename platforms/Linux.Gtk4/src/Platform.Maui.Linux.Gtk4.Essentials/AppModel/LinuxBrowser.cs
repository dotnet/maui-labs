using System.Diagnostics;
using Microsoft.Maui.ApplicationModel;

namespace Platform.Maui.Linux.Gtk4.Essentials.AppModel;

public class LinuxBrowser : IBrowser
{
	public async Task<bool> OpenAsync(Uri uri, BrowserLaunchOptions options)
	{
		ArgumentNullException.ThrowIfNull(uri);
		try
		{
			var psi = new ProcessStartInfo("xdg-open", uri.AbsoluteUri)
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			Process.Start(psi);
			return await Task.FromResult(true);
		}
		catch
		{
			return false;
		}
	}
}
