using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Maui.Devices;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

/// <summary>Device info derived from navigator.userAgentData (falling back to the user agent string).</summary>
[SupportedOSPlatform("browser")]
public class BrowserDeviceInfo : IDeviceInfo
{
	/// <summary>The DevicePlatform reported by this backend.</summary>
	public static DevicePlatform BrowserPlatform { get; } = DevicePlatform.Create("Browser");

	Info? info;

	Info GetInfo()
	{
		if (info is not null)
			return info;

		BrowserEssentials.EnsureInitialized();
		using var doc = JsonDocument.Parse(BrowserEssentialsInterop.GetDeviceInfo());
		var root = doc.RootElement;

		var userAgent = root.GetProperty("userAgent").GetString() ?? string.Empty;
		var mobile = root.GetProperty("mobile").GetBoolean();
		var osPlatform = root.GetProperty("platform").GetString() ?? string.Empty;

		// Prefer userAgentData brands (Chromium); otherwise sniff the UA string.
		string browserName = string.Empty, browserVersion = string.Empty;
		foreach (var brand in root.GetProperty("brands").EnumerateArray())
		{
			var name = brand.GetProperty("brand").GetString() ?? string.Empty;
			if (name.Contains("Not", StringComparison.OrdinalIgnoreCase) || name == "Chromium")
				continue;
			browserName = name;
			browserVersion = brand.GetProperty("version").GetString() ?? string.Empty;
			break;
		}
		if (browserName.Length == 0)
			(browserName, browserVersion) = SniffUserAgent(userAgent);

		return info = new Info(browserName, browserVersion, osPlatform, mobile);
	}

	static (string Name, string Version) SniffUserAgent(string userAgent)
	{
		foreach (var candidate in new[] { "Firefox/", "Edg/", "Chrome/", "Version/" })
		{
			var index = userAgent.IndexOf(candidate, StringComparison.Ordinal);
			if (index < 0)
				continue;
			var version = userAgent[(index + candidate.Length)..].Split(' ', ')')[0];
			var name = candidate switch
			{
				"Edg/" => "Edge",
				"Version/" => "Safari",
				_ => candidate.TrimEnd('/'),
			};
			return (name, version);
		}
		return ("Browser", string.Empty);
	}

	sealed record Info(string BrowserName, string BrowserVersion, string OSPlatform, bool Mobile);

	public string Model => GetInfo().BrowserName;

	public string Manufacturer => GetInfo().OSPlatform;

	public string Name => GetInfo().BrowserName;

	public string VersionString => GetInfo().BrowserVersion;

	public Version Version => Version.TryParse(VersionString, out var version)
		? version
		: int.TryParse(VersionString.Split('.')[0], out var major) ? new Version(major, 0) : new Version(0, 0);

	public DevicePlatform Platform => BrowserPlatform;

	public DeviceIdiom Idiom => GetInfo().Mobile ? DeviceIdiom.Phone : DeviceIdiom.Desktop;

	public DeviceType DeviceType => DeviceType.Physical;
}
