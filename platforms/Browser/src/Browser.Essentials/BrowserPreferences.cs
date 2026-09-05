using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

/// <summary>Preferences backed by window.localStorage.</summary>
[SupportedOSPlatform("browser")]
public class BrowserPreferences : IPreferences
{
	const string KeyPrefix = "maui:prefs:";

	static string GetContainer(string? sharedName) => KeyPrefix + (sharedName ?? "_default") + ":";

	static string GetStorageKey(string key, string? sharedName) => GetContainer(sharedName) + key;

	public bool ContainsKey(string key, string? sharedName = null)
	{
		BrowserEssentials.EnsureInitialized();
		return BrowserEssentialsInterop.PrefGet(GetStorageKey(key, sharedName)) is not null;
	}

	public void Remove(string key, string? sharedName = null)
	{
		BrowserEssentials.EnsureInitialized();
		BrowserEssentialsInterop.PrefRemove(GetStorageKey(key, sharedName));
	}

	public void Clear(string? sharedName = null)
	{
		BrowserEssentials.EnsureInitialized();
		foreach (var storageKey in BrowserEssentialsInterop.PrefKeys(GetContainer(sharedName)))
			BrowserEssentialsInterop.PrefRemove(storageKey);
	}

	public void Set<T>(string key, T value, string? sharedName = null)
	{
		BrowserEssentials.EnsureInitialized();
		if (value is null)
		{
			BrowserEssentialsInterop.PrefRemove(GetStorageKey(key, sharedName));
			return;
		}

		var stored = value switch
		{
			DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
			DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
			IConvertible c => c.ToString(CultureInfo.InvariantCulture),
			_ => value.ToString() ?? string.Empty,
		};
		BrowserEssentialsInterop.PrefSet(GetStorageKey(key, sharedName), stored);
	}

	public T Get<T>(string key, T defaultValue, string? sharedName = null)
	{
		BrowserEssentials.EnsureInitialized();
		var stored = BrowserEssentialsInterop.PrefGet(GetStorageKey(key, sharedName));
		if (stored is null)
			return defaultValue;

		try
		{
			var type = typeof(T);
			var underlying = Nullable.GetUnderlyingType(type) ?? type;
			if (underlying == typeof(DateTime))
				return (T)(object)DateTime.Parse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
			if (underlying == typeof(DateTimeOffset))
				return (T)(object)DateTimeOffset.Parse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
			return (T)Convert.ChangeType(stored, underlying, CultureInfo.InvariantCulture);
		}
		catch
		{
			return defaultValue;
		}
	}
}
