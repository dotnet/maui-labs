using System.Reflection;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;

namespace Platform.Maui.Linux.Gtk4.Essentials.AppModel;

public class LinuxVersionTracking : IVersionTracking
{
	private readonly object _lock = new();
	private VersionTrackingData _data = null!;
	private bool _tracked;

	private string DataFilePath
	{
		get
		{
			var configDir = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
			if (string.IsNullOrEmpty(configDir))
				configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
			var appDir = Path.Combine(configDir, AppDomain.CurrentDomain.FriendlyName);
			Directory.CreateDirectory(appDir);
			return Path.Combine(appDir, "version_tracking.json");
		}
	}

	public void Track()
	{
		lock (_lock)
		{
			if (_tracked) return;
			_tracked = true;

			_data = Load();

			var currentVer = CurrentVersion;
			var currentBld = CurrentBuild;

			if (_data.VersionHistory.Count == 0)
			{
				_data.FirstInstalledVersion = currentVer;
				_data.FirstInstalledBuild = currentBld;
			}

			_data.IsFirstLaunchEver = _data.VersionHistory.Count == 0;
			_data.IsFirstLaunchForCurrentVersion = !_data.VersionHistory.Contains(currentVer);
			_data.IsFirstLaunchForCurrentBuild = !_data.BuildHistory.Contains(currentBld);

			if (_data.VersionHistory.Count > 0 && _data.VersionHistory[^1] != currentVer)
				_data.PreviousVersion = _data.VersionHistory[^1];
			if (_data.BuildHistory.Count > 0 && _data.BuildHistory[^1] != currentBld)
				_data.PreviousBuild = _data.BuildHistory[^1];

			if (!_data.VersionHistory.Contains(currentVer))
				_data.VersionHistory.Add(currentVer);
			if (!_data.BuildHistory.Contains(currentBld))
				_data.BuildHistory.Add(currentBld);

			Save(_data);
		}
	}

	private VersionTrackingData Load()
	{
		try
		{
			if (File.Exists(DataFilePath))
			{
				var json = File.ReadAllText(DataFilePath);
				return JsonSerializer.Deserialize<VersionTrackingData>(json) ?? new();
			}
		}
		catch { }
		return new();
	}

	private void Save(VersionTrackingData data)
	{
		var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
		File.WriteAllText(DataFilePath, json);
	}

	private Assembly EntryAssembly => Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

	public bool IsFirstLaunchEver => EnsureTracked()._data.IsFirstLaunchEver;
	public bool IsFirstLaunchForCurrentVersion => EnsureTracked()._data.IsFirstLaunchForCurrentVersion;
	public bool IsFirstLaunchForCurrentBuild => EnsureTracked()._data.IsFirstLaunchForCurrentBuild;
	public string CurrentVersion => EntryAssembly.GetName().Version?.ToString() ?? "1.0.0";
	public string CurrentBuild => EntryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
		?.InformationalVersion ?? CurrentVersion;
	public string? PreviousVersion => EnsureTracked()._data.PreviousVersion;
	public string? PreviousBuild => EnsureTracked()._data.PreviousBuild;
	public string? FirstInstalledVersion => EnsureTracked()._data.FirstInstalledVersion;
	public string? FirstInstalledBuild => EnsureTracked()._data.FirstInstalledBuild;
	public IReadOnlyList<string> VersionHistory => EnsureTracked()._data.VersionHistory;
	public IReadOnlyList<string> BuildHistory => EnsureTracked()._data.BuildHistory;

	public bool IsFirstLaunchForVersion(string version) => !EnsureTracked()._data.VersionHistory.Contains(version);
	public bool IsFirstLaunchForBuild(string build) => !EnsureTracked()._data.BuildHistory.Contains(build);

	private LinuxVersionTracking EnsureTracked()
	{
		if (!_tracked) Track();
		return this;
	}

	private class VersionTrackingData
	{
		public bool IsFirstLaunchEver { get; set; }
		public bool IsFirstLaunchForCurrentVersion { get; set; }
		public bool IsFirstLaunchForCurrentBuild { get; set; }
		public string? PreviousVersion { get; set; }
		public string? PreviousBuild { get; set; }
		public string? FirstInstalledVersion { get; set; }
		public string? FirstInstalledBuild { get; set; }
		public List<string> VersionHistory { get; set; } = new();
		public List<string> BuildHistory { get; set; } = new();
	}
}
