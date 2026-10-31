using System.Runtime.Versioning;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

/// <summary>Opens URIs in a new browser tab via window.open.</summary>
[SupportedOSPlatform("browser")]
public class BrowserBrowser : IBrowser
{
	public async Task<bool> OpenAsync(Uri uri, BrowserLaunchOptions options)
	{
		await BrowserEssentials.WhenInitializedAsync().ConfigureAwait(false);
		// Launch mode and title/color options have no browser equivalent — always a new tab.
		return BrowserEssentialsInterop.OpenUrl(uri.AbsoluteUri);
	}
}
