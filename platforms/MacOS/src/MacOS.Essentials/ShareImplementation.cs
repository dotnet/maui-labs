using AppKit;
using Foundation;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class ShareImplementation : IShare
{
	public Task RequestAsync(ShareTextRequest request)
	{
		var items = new List<NSObject>();

		if (!string.IsNullOrEmpty(request.Text))
			items.Add(new NSString(request.Text));

		if (!string.IsNullOrEmpty(request.Uri))
			items.Add(new NSUrl(request.Uri));

		if (items.Count == 0)
			return Task.CompletedTask;

		var picker = new NSSharingServicePicker(items.ToArray());
		var window = NSApplication.SharedApplication.KeyWindow;
		if (window?.ContentView is not null)
			picker.ShowRelativeToRect(window.ContentView.Bounds, window.ContentView, NSRectEdge.MinYEdge);

		return Task.CompletedTask;
	}

	public Task RequestAsync(ShareFileRequest request)
	{
		var items = new List<NSObject>();

		if (request.File is not null)
			items.Add(new NSUrl(request.File.FullPath, false));

		if (items.Count == 0)
			return Task.CompletedTask;

		var picker = new NSSharingServicePicker(items.ToArray());
		var window = NSApplication.SharedApplication.KeyWindow;
		if (window?.ContentView is not null)
			picker.ShowRelativeToRect(window.ContentView.Bounds, window.ContentView, NSRectEdge.MinYEdge);

		return Task.CompletedTask;
	}

	public Task RequestAsync(ShareMultipleFilesRequest request)
	{
		var items = request.Files?
			.Select(f => (NSObject)new NSUrl(f.FullPath, false))
			.ToArray() ?? [];

		if (items.Length == 0)
			return Task.CompletedTask;

		var picker = new NSSharingServicePicker(items);
		var window = NSApplication.SharedApplication.KeyWindow;
		if (window?.ContentView is not null)
			picker.ShowRelativeToRect(window.ContentView.Bounds, window.ContentView, NSRectEdge.MinYEdge);

		return Task.CompletedTask;
	}
}
