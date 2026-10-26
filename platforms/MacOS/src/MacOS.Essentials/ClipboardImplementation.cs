using AppKit;
using Foundation;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class ClipboardImplementation : IClipboard
{
	event EventHandler<EventArgs>? _clipboardContentChanged;

	public bool HasText => !string.IsNullOrEmpty(NSPasteboard.GeneralPasteboard.GetStringForType(NSPasteboardType.String.GetConstant()!));

	public Task SetTextAsync(string? text)
	{
		var pb = NSPasteboard.GeneralPasteboard;
		pb.ClearContents();
		if (text is not null)
			pb.SetStringForType(text, NSPasteboardType.String.GetConstant()!);

		_clipboardContentChanged?.Invoke(this, EventArgs.Empty);
		return Task.CompletedTask;
	}

	public Task<string?> GetTextAsync()
	{
		var text = NSPasteboard.GeneralPasteboard.GetStringForType(NSPasteboardType.String.GetConstant()!);
		return Task.FromResult<string?>(text);
	}

	public event EventHandler<EventArgs> ClipboardContentChanged
	{
		add => _clipboardContentChanged += value;
		remove => _clipboardContentChanged -= value;
	}
}
