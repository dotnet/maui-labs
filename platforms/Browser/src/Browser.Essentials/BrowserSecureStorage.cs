using System.Runtime.Versioning;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

/// <summary>
/// The .NET MAUI <see cref="Microsoft.Maui.Storage.SecureStorage"/> API is not supported in the browser.
/// All storage operations throw <see cref="PlatformNotSupportedException"/>.
/// </summary>
[SupportedOSPlatform("browser")]
public class BrowserSecureStorage : ISecureStorage
{
	const string UnsupportedMessage = "The .NET MAUI Microsoft.Maui.Storage.SecureStorage API is not supported in the browser.";

	public Task<string?> GetAsync(string key)
		=> throw new PlatformNotSupportedException(UnsupportedMessage);

	public Task SetAsync(string key, string value)
		=> throw new PlatformNotSupportedException(UnsupportedMessage);

	public bool Remove(string key)
		=> throw new PlatformNotSupportedException(UnsupportedMessage);

	public void RemoveAll()
		=> throw new PlatformNotSupportedException(UnsupportedMessage);
}
