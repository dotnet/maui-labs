using System.Text.Json;
using Microsoft.Maui.Storage;

namespace Platform.Maui.Linux.Gtk4.Essentials.Storage;

public class LinuxPreferences : IPreferences
{
	private readonly object _lock = new();

	private string GetFilePath(string? sharedName)
	{
		var configDir = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
		if (string.IsNullOrEmpty(configDir))
			configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
		var appDir = Path.Combine(configDir, AppDomain.CurrentDomain.FriendlyName);
		Directory.CreateDirectory(appDir);
		var fileName = string.IsNullOrEmpty(sharedName) ? "preferences.json" : $"preferences.{sharedName}.json";
		return Path.Combine(appDir, fileName);
	}

	private Dictionary<string, JsonElement> Load(string? sharedName)
	{
		var path = GetFilePath(sharedName);
		if (!File.Exists(path))
			return new();
		try
		{
			var json = File.ReadAllText(path);
			return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
		}
		catch { return new(); }
	}

	private void Save(string? sharedName, Dictionary<string, JsonElement> data)
	{
		var path = GetFilePath(sharedName);
		var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
		File.WriteAllText(path, json);
	}

	public bool ContainsKey(string key, string? sharedName)
	{
		lock (_lock)
			return Load(sharedName).ContainsKey(key);
	}

	public void Remove(string key, string? sharedName)
	{
		lock (_lock)
		{
			var data = Load(sharedName);
			if (data.Remove(key))
				Save(sharedName, data);
		}
	}

	public void Clear(string? sharedName)
	{
		lock (_lock)
			Save(sharedName, new());
	}

	public void Set<T>(string key, T value, string? sharedName)
	{
		lock (_lock)
		{
			var data = Load(sharedName);
			data[key] = JsonSerializer.SerializeToElement(value);
			Save(sharedName, data);
		}
	}

	public T Get<T>(string key, T defaultValue, string? sharedName)
	{
		lock (_lock)
		{
			var data = Load(sharedName);
			if (!data.TryGetValue(key, out var element))
				return defaultValue;
			try { return element.Deserialize<T>() ?? defaultValue; }
			catch { return defaultValue; }
		}
	}
}
