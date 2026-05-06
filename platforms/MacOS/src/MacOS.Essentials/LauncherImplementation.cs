using AppKit;
using Foundation;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class LauncherImplementation : ILauncher
{
	public Task<bool> CanOpenAsync(Uri uri)
	{
		var nsUrl = new NSUrl(uri.AbsoluteUri);
		var app = NSWorkspace.SharedWorkspace.UrlForApplication(nsUrl);
		return Task.FromResult(app is not null);
	}

	public Task<bool> OpenAsync(Uri uri)
	{
		var nsUrl = new NSUrl(uri.AbsoluteUri);
		var result = NSWorkspace.SharedWorkspace.OpenUrl(nsUrl);
		return Task.FromResult(result);
	}

	public Task<bool> OpenAsync(OpenFileRequest request)
	{
		if (request.File?.FullPath is null)
			return Task.FromResult(false);

		var nsUrl = new NSUrl(request.File.FullPath, false);
		var result = NSWorkspace.SharedWorkspace.OpenUrl(nsUrl);
		return Task.FromResult(result);
	}

	public Task<bool> TryOpenAsync(Uri uri)
	{
		try
		{
			var nsUrl = new NSUrl(uri.AbsoluteUri);
			var result = NSWorkspace.SharedWorkspace.OpenUrl(nsUrl);
			return Task.FromResult(result);
		}
		catch
		{
			return Task.FromResult(false);
		}
	}
}
