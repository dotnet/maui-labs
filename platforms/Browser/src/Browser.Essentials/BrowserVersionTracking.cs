using System.Runtime.Versioning;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

/// <summary>Version tracking persisted in preferences (localStorage).</summary>
[SupportedOSPlatform("browser")]
public class BrowserVersionTracking : IVersionTracking
{
	const string SharedName = "versiontracking";
	const string VersionsKey = "VersionTracking.Versions";
	const string BuildsKey = "VersionTracking.Builds";

	readonly IPreferences preferences;
	readonly IAppInfo appInfo;

	List<string> versionHistory = [];
	List<string> buildHistory = [];
	bool tracked;

	public BrowserVersionTracking(IPreferences preferences, IAppInfo appInfo)
	{
		this.preferences = preferences;
		this.appInfo = appInfo;
	}

	public bool IsFirstLaunchEver { get; private set; }

	public bool IsFirstLaunchForCurrentVersion { get; private set; }

	public bool IsFirstLaunchForCurrentBuild { get; private set; }

	public string CurrentVersion => appInfo.VersionString;

	public string CurrentBuild => appInfo.BuildString;

	public string? PreviousVersion => GetPrevious(versionHistory);

	public string? PreviousBuild => GetPrevious(buildHistory);

	public string? FirstInstalledVersion => versionHistory.FirstOrDefault();

	public string? FirstInstalledBuild => buildHistory.FirstOrDefault();

	public IReadOnlyList<string> VersionHistory => versionHistory;

	public IReadOnlyList<string> BuildHistory => buildHistory;

	public bool IsFirstLaunchForVersion(string version) =>
		CurrentVersion == version ? IsFirstLaunchForCurrentVersion : !versionHistory.Contains(version);

	public bool IsFirstLaunchForBuild(string build) =>
		CurrentBuild == build ? IsFirstLaunchForCurrentBuild : !buildHistory.Contains(build);

	public void Track()
	{
		if (tracked)
			return;
		tracked = true;

		versionHistory = ReadHistory(VersionsKey);
		buildHistory = ReadHistory(BuildsKey);

		IsFirstLaunchEver = versionHistory.Count == 0;
		IsFirstLaunchForCurrentVersion = !versionHistory.Contains(CurrentVersion) || CurrentVersion != versionHistory.LastOrDefault();
		IsFirstLaunchForCurrentBuild = !buildHistory.Contains(CurrentBuild) || CurrentBuild != buildHistory.LastOrDefault();

		if (IsFirstLaunchForCurrentVersion)
		{
			versionHistory.Remove(CurrentVersion);
			versionHistory.Add(CurrentVersion);
		}
		if (IsFirstLaunchForCurrentBuild)
		{
			buildHistory.Remove(CurrentBuild);
			buildHistory.Add(CurrentBuild);
		}

		WriteHistory(VersionsKey, versionHistory);
		WriteHistory(BuildsKey, buildHistory);
	}

	string? GetPrevious(List<string> history) =>
		history.Count >= 2 ? history[^2] : null;

	List<string> ReadHistory(string key)
	{
		var raw = preferences.Get<string?>(key, null, SharedName);
		return string.IsNullOrEmpty(raw) ? [] : [.. raw.Split('|', StringSplitOptions.RemoveEmptyEntries)];
	}

	void WriteHistory(string key, List<string> history) =>
		preferences.Set(key, string.Join('|', history), SharedName);
}
