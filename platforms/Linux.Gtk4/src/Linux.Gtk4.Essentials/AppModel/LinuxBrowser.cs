using System.Diagnostics;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.AppModel;

public class LinuxBrowser : IBrowser
{
	public async Task<bool> OpenAsync(Uri uri, BrowserLaunchOptions options)
	{
		ArgumentNullException.ThrowIfNull(uri);
		try
		{
			var psi = new ProcessStartInfo("xdg-open")
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			psi.ArgumentList.Add(uri.AbsoluteUri);

			using var process = Process.Start(psi);
			return await Task.FromResult(process is not null);
		}
		catch
		{
			return false;
		}
	}
}
