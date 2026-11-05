using System.Runtime.Versioning;
using Microsoft.Maui.Accessibility;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

/// <summary>Screen reader announcements via a visually-hidden aria-live region.</summary>
[SupportedOSPlatform("browser")]
public class BrowserSemanticScreenReader : ISemanticScreenReader
{
	public void Announce(string text)
	{
		BrowserEssentials.EnsureInitialized();
		BrowserEssentialsInterop.Announce(text);
	}
}
