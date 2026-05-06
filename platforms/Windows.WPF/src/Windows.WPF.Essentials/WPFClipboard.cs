using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials
{
	public class WPFClipboard : IClipboard
	{
		public bool HasText => System.Windows.Clipboard.ContainsText();

		public event EventHandler<EventArgs>? ClipboardContentChanged;

		public Task<string?> GetTextAsync()
		{
			var text = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null;
			return Task.FromResult(text);
		}

		public Task SetTextAsync(string? text)
		{
			if (text != null)
				System.Windows.Clipboard.SetText(text);
			else
				System.Windows.Clipboard.Clear();

			ClipboardContentChanged?.Invoke(this, EventArgs.Empty);
			return Task.CompletedTask;
		}
	}
}
