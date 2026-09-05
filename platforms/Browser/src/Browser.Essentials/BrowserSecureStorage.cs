using System.Runtime.Versioning;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

/// <summary>
/// Secure storage backed by localStorage with AES-GCM encryption via WebCrypto.
/// The encryption key is non-extractable and lives in IndexedDB, so stored values
/// are unreadable outside this origin's browser context. This is best-effort:
/// any script running on the same origin can decrypt values, and clearing site
/// data destroys the key (existing values then read as null).
/// </summary>
[SupportedOSPlatform("browser")]
public class BrowserSecureStorage : ISecureStorage
{
	const string KeyPrefix = "maui:securestorage:";

	public async Task<string?> GetAsync(string key)
	{
		await BrowserEssentials.WhenInitializedAsync().ConfigureAwait(false);
		return await BrowserEssentialsInterop.SecureGet(KeyPrefix + key).ConfigureAwait(false);
	}

	public async Task SetAsync(string key, string value)
	{
		await BrowserEssentials.WhenInitializedAsync().ConfigureAwait(false);
		await BrowserEssentialsInterop.SecureSet(KeyPrefix + key, value).ConfigureAwait(false);
	}

	public bool Remove(string key)
	{
		BrowserEssentials.EnsureInitialized();
		var storageKey = KeyPrefix + key;
		var exists = BrowserEssentialsInterop.PrefGet(storageKey) is not null;
		BrowserEssentialsInterop.PrefRemove(storageKey);
		return exists;
	}

	public void RemoveAll()
	{
		BrowserEssentials.EnsureInitialized();
		foreach (var storageKey in BrowserEssentialsInterop.PrefKeys(KeyPrefix))
			BrowserEssentialsInterop.PrefRemove(storageKey);
	}
}
