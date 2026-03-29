using System.Diagnostics;
using Microsoft.Maui.ApplicationModel;

namespace Platform.Maui.Linux.Gtk4.Essentials.AppModel;

public class LinuxLauncher : ILauncher
{
	public Task<bool> CanOpenAsync(Uri uri)
	{
		// On Linux, xdg-open can typically handle any URI scheme
		return Task.FromResult(uri is not null);
	}

	public async Task<bool> OpenAsync(Uri uri)
	{
		ArgumentNullException.ThrowIfNull(uri);
		return await LaunchXdgOpen(uri.AbsoluteUri);
	}

	public async Task<bool> OpenAsync(OpenFileRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var file = request.File;
		if (file is null) return false;
		return await LaunchXdgOpen(file.FullPath);
	}

	public async Task<bool> TryOpenAsync(Uri uri)
	{
		try { return await OpenAsync(uri); }
		catch { return false; }
	}

	private static Task<bool> LaunchXdgOpen(string argument)
	{
		try
		{
			var psi = new ProcessStartInfo("xdg-open", argument)
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			Process.Start(psi);
			return Task.FromResult(true);
		}
		catch
		{
			return Task.FromResult(false);
		}
	}
}
