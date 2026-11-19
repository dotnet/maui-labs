using System.Reflection;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials
{
	public class WPFVersionTracking : IVersionTracking
	{
		readonly IPreferences _preferences;
		readonly object _trackingLock = new();
		bool _tracked;

		const string VersionKey = "vt_current_version";
		const string BuildKey = "vt_current_build";
		const string PrevVersionKey = "vt_previous_version";
		const string FirstVersionKey = "vt_first_version";
		const string VersionHistoryKey = "vt_version_history";
		const string IsFirstLaunchKey = "vt_is_first_launch";
		const string SharedName = "version_tracking";

		public WPFVersionTracking(Storage.IPreferences preferences)
		{
			_preferences = preferences;
		}

		void TrackCore()
		{
			var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";
			var currentBuild = currentVersion;

			var savedVersion = _preferences.Get<string>(VersionKey, "", SharedName);

			if (string.IsNullOrEmpty(savedVersion))
			{
				_preferences.Set(FirstVersionKey, currentVersion, SharedName);
				_preferences.Set(IsFirstLaunchKey, "true", SharedName);
			}
			else
			{
				_preferences.Set(IsFirstLaunchKey, "false", SharedName);
			}

			if (savedVersion != currentVersion)
			{
				_preferences.Set(PrevVersionKey, savedVersion, SharedName);
			}

			_preferences.Set(VersionKey, currentVersion, SharedName);
			_preferences.Set(BuildKey, currentBuild, SharedName);

			var history = _preferences.Get<string>(VersionHistoryKey, "", SharedName);
			if (!history.Contains(currentVersion))
			{
				history = string.IsNullOrEmpty(history) ? currentVersion : $"{history}|{currentVersion}";
				_preferences.Set(VersionHistoryKey, history, SharedName);
			}
		}

		IPreferences TrackedPreferences
		{
			get
			{
				Track();
				return _preferences;
			}
		}

		public bool IsFirstLaunchEver => TrackedPreferences.Get(IsFirstLaunchKey, "true", SharedName) == "true";
		public bool IsFirstLaunchForCurrentVersion =>
			TrackedPreferences.Get(PrevVersionKey, "", SharedName) != CurrentVersion || IsFirstLaunchEver;
		public bool IsFirstLaunchForCurrentBuild => IsFirstLaunchForCurrentVersion;

		public string CurrentVersion => TrackedPreferences.Get(VersionKey, "1.0.0", SharedName);
		public string CurrentBuild => TrackedPreferences.Get(BuildKey, "1.0.0", SharedName);
		public string PreviousVersion => TrackedPreferences.Get(PrevVersionKey, "", SharedName);
		public string PreviousBuild => PreviousVersion;
		public string FirstInstalledVersion => TrackedPreferences.Get(FirstVersionKey, "", SharedName);
		public string FirstInstalledBuild => FirstInstalledVersion;

		public IReadOnlyList<string> VersionHistory =>
			TrackedPreferences.Get(VersionHistoryKey, "", SharedName).Split('|', StringSplitOptions.RemoveEmptyEntries);
		public IReadOnlyList<string> BuildHistory => VersionHistory;

		public bool IsFirstLaunchForVersion(string version) => CurrentVersion == version && IsFirstLaunchForCurrentVersion;
		public bool IsFirstLaunchForBuild(string build) => IsFirstLaunchForVersion(build);

		public void Track()
		{
			lock (_trackingLock)
			{
				if (_tracked)
					return;

				TrackCore();
				_tracked = true;
			}
		}
	}
}
