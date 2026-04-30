using System.IO;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials
{
	public class WPFPreferences : IPreferences
	{
		static readonly string _prefsDir = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			AppDomain.CurrentDomain.FriendlyName, "preferences");

		static string GetFilePath(string? sharedName) =>
			Path.Combine(_prefsDir, (sharedName ?? "default") + ".json");

		static Dictionary<string, string> Load(string? sharedName)
		{
			var path = GetFilePath(sharedName);
			if (!File.Exists(path)) return new();
			try
			{
				var json = File.ReadAllText(path);
				return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
			}
			catch
			{
				return new();
			}
		}

		static void Save(string? sharedName, Dictionary<string, string> dict)
		{
			Directory.CreateDirectory(_prefsDir);
			var path = GetFilePath(sharedName);
			var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(path, json);
		}

		public bool ContainsKey(string key, string? sharedName = null) => Load(sharedName).ContainsKey(key);

		public void Remove(string key, string? sharedName = null)
		{
			var dict = Load(sharedName);
			if (dict.Remove(key))
				Save(sharedName, dict);
		}

		public void Clear(string? sharedName = null)
		{
			var path = GetFilePath(sharedName);
			if (File.Exists(path)) File.Delete(path);
		}

		public void Set<T>(string key, T value, string? sharedName = null)
		{
			var dict = Load(sharedName);
			dict[key] = value?.ToString() ?? "";
			Save(sharedName, dict);
		}

		public T Get<T>(string key, T defaultValue, string? sharedName = null)
		{
			var dict = Load(sharedName);
			if (!dict.TryGetValue(key, out var strValue))
				return defaultValue;

			try
			{
				return (T)Convert.ChangeType(strValue, typeof(T));
			}
			catch
			{
				return defaultValue;
			}
		}
	}
}
