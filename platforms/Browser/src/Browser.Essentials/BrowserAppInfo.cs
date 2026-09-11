using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

/// <summary>
/// App info sourced from the hosting document (title, origin) and the entry assembly (version).
/// </summary>
[SupportedOSPlatform("browser")]
public class BrowserAppInfo : IAppInfo
{
	static readonly Version AssemblyVersion =
		Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0);

	static JsonDocument GetInfo()
	{
		BrowserEssentials.EnsureInitialized();
		return JsonDocument.Parse(BrowserEssentialsInterop.GetAppInfo());
	}

	public string PackageName
	{
		get
		{
			using var doc = GetInfo();
			return doc.RootElement.GetProperty("hostname").GetString() ?? string.Empty;
		}
	}

	public string Name
	{
		get
		{
			using var doc = GetInfo();
			var title = doc.RootElement.GetProperty("title").GetString();
			return string.IsNullOrEmpty(title)
				? Assembly.GetEntryAssembly()?.GetName().Name ?? "App"
				: title;
		}
	}

	public string VersionString => AssemblyVersion.ToString();

	public Version Version => AssemblyVersion;

	public string BuildString => AssemblyVersion.Revision >= 0 ? AssemblyVersion.Revision.ToString() : "0";

	public AppTheme RequestedTheme
	{
		get
		{
			BrowserEssentials.EnsureInitialized();
			return BrowserEssentialsInterop.PrefersDark() ? AppTheme.Dark : AppTheme.Light;
		}
	}

	public AppPackagingModel PackagingModel => AppPackagingModel.Unpackaged;

	public LayoutDirection RequestedLayoutDirection
	{
		get
		{
			using var doc = GetInfo();
			return doc.RootElement.GetProperty("rtl").GetBoolean()
				? LayoutDirection.RightToLeft
				: LayoutDirection.LeftToRight;
		}
	}

	public void ShowSettingsUI() =>
		throw new FeatureNotSupportedException("Browsers do not expose an app settings UI.");
}
