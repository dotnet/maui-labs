using System.Runtime.Versioning;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

/// <summary>
/// Clipboard backed by the async Clipboard API (navigator.clipboard).
/// Browsers gate clipboard reads behind a user permission prompt, and there is no
/// synchronous "has text" query — <see cref="HasText"/> reflects the last value
/// observed through this API rather than the live OS clipboard.
/// </summary>
[SupportedOSPlatform("browser")]
public class BrowserClipboard : IClipboard
{
	bool lastKnownHasText;

	public bool HasText => lastKnownHasText;

	public event EventHandler<EventArgs>? ClipboardContentChanged;

	public async Task SetTextAsync(string? text)
	{
		await BrowserEssentials.WhenInitializedAsync().ConfigureAwait(false);
		await BrowserEssentialsInterop.ClipboardWriteText(text ?? string.Empty).ConfigureAwait(false);
		lastKnownHasText = !string.IsNullOrEmpty(text);
		ClipboardContentChanged?.Invoke(this, EventArgs.Empty);
	}

	public async Task<string?> GetTextAsync()
	{
		await BrowserEssentials.WhenInitializedAsync().ConfigureAwait(false);
		var text = await BrowserEssentialsInterop.ClipboardReadText().ConfigureAwait(false);
		lastKnownHasText = !string.IsNullOrEmpty(text);
		return string.IsNullOrEmpty(text) ? null : text;
	}
}
