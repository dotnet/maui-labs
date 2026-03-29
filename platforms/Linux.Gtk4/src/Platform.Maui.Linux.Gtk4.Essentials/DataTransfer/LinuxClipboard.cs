using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Platform.Maui.Linux.Gtk4.Essentials.DataTransfer;

public class LinuxClipboard : IClipboard
{
	private EventHandler<EventArgs>? _clipboardContentChanged;

	public bool HasText
	{
		get
		{
			try
			{
				var display = Gdk.Display.GetDefault();
				if (display is null) return false;
				var clipboard = display.GetClipboard();
				return clipboard.GetFormats()?.ContainMimeType("text/plain") ?? false;
			}
			catch { return false; }
		}
	}

	public Task SetTextAsync(string? text)
	{
		var display = Gdk.Display.GetDefault();
		if (display is null) return Task.CompletedTask;
		var clipboard = display.GetClipboard();
		if (text is not null)
			clipboard.SetText(text);
		_clipboardContentChanged?.Invoke(this, EventArgs.Empty);
		return Task.CompletedTask;
	}

	public async Task<string?> GetTextAsync()
	{
		var display = Gdk.Display.GetDefault();
		if (display is null) return null;
		var clipboard = display.GetClipboard();
		try
		{
			var text = await clipboard.ReadTextAsync();
			return text;
		}
		catch { return null; }
	}

	public event EventHandler<EventArgs>? ClipboardContentChanged
	{
		add => _clipboardContentChanged += value;
		remove => _clipboardContentChanged -= value;
	}
}
