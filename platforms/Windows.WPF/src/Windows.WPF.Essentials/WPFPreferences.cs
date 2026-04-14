using System.IO;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials
{
	public class WPFPreferences : IPreferences
	{
		static readonly string _prefsDir = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			AppDomain.CurrentDomain.FriendlyName, "preferences");

		static string GetFilePath(string? sharedName) =>
			Path.Combine(_prefsDir, (sharedName ?? "default") + ".prefs");

		static Dictionary<string, string> Load(string? sharedName)
		{
			var path = GetFilePath(sharedName);
			if (!File.Exists(path)) return new();
			var dict = new Dictionary<string, string>();
			foreach (var line in File.ReadAllLines(path))
			{
				var idx = line.IndexOf('=');
				if (idx > 0)
					dict[line[..idx]] = line[(idx + 1)..];
			}
			return dict;
		}

		static void Save(string? sharedName, Dictionary<string, string> dict)
		{
			Directory.CreateDirectory(_prefsDir);
			var path = GetFilePath(sharedName);
			File.WriteAllLines(path, dict.Select(kv => $"{kv.Key}={kv.Value}"));
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
