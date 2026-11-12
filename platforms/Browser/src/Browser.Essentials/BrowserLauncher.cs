using System.Runtime.Versioning;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

/// <summary>
/// Launcher backed by window.open for web URLs and location.assign for
/// protocol-handler schemes (mailto:, tel:, sms:). Files open as blob URLs in a new tab.
/// </summary>
[SupportedOSPlatform("browser")]
public class BrowserLauncher : ILauncher
{
	static readonly string[] NavigationSchemes = ["mailto", "tel", "sms"];

	public Task<bool> CanOpenAsync(Uri uri) =>
		Task.FromResult(uri.Scheme is "http" or "https" || NavigationSchemes.Contains(uri.Scheme));

	public async Task<bool> OpenAsync(Uri uri)
	{
		await BrowserEssentials.WhenInitializedAsync().ConfigureAwait(false);
		return NavigationSchemes.Contains(uri.Scheme)
			? BrowserEssentialsInterop.NavigateTo(uri.AbsoluteUri)
			: BrowserEssentialsInterop.OpenUrl(uri.AbsoluteUri);
	}

	public async Task<bool> OpenAsync(OpenFileRequest request)
	{
		await BrowserEssentials.WhenInitializedAsync().ConfigureAwait(false);
		if (request.File is null)
			return false;
		var bytes = await File.ReadAllBytesAsync(request.File.FullPath).ConfigureAwait(false);
		return BrowserEssentialsInterop.OpenFileBlob(
			Convert.ToBase64String(bytes),
			request.File.ContentType,
			Path.GetFileName(request.File.FullPath));
	}

	public async Task<bool> TryOpenAsync(Uri uri) =>
		await CanOpenAsync(uri).ConfigureAwait(false) && await OpenAsync(uri).ConfigureAwait(false);
}
