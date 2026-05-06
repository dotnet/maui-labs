using AppKit;
using Foundation;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class ClipboardImplementation : IClipboard
{
	event EventHandler<EventArgs>? _clipboardContentChanged;

	public bool HasText => !string.IsNullOrEmpty(NSPasteboard.GeneralPasteboard.GetStringForType(NSPasteboard.NSPasteboardTypeString));

	public Task SetTextAsync(string? text)
	{
		var pb = NSPasteboard.GeneralPasteboard;
		pb.ClearContents();
		if (text is not null)
			pb.SetStringForType(text, NSPasteboard.NSPasteboardTypeString);

		_clipboardContentChanged?.Invoke(this, EventArgs.Empty);
		return Task.CompletedTask;
	}

	public Task<string?> GetTextAsync()
	{
		var text = NSPasteboard.GeneralPasteboard.GetStringForType(NSPasteboard.NSPasteboardTypeString);
		return Task.FromResult(text);
	}

	public event EventHandler<EventArgs> ClipboardContentChanged
	{
		add => _clipboardContentChanged += value;
		remove => _clipboardContentChanged -= value;
	}
}
