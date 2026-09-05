using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Maui.Devices;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

/// <summary>
/// Display info from window.screen and devicePixelRatio. KeepScreenOn uses the
/// Screen Wake Lock API where available (secure contexts, most modern browsers).
/// </summary>
[SupportedOSPlatform("browser")]
public class BrowserDeviceDisplay : IDeviceDisplay
{
	bool watching;
	bool keepScreenOnRequested;

	public bool KeepScreenOn
	{
		get
		{
			BrowserEssentials.EnsureInitialized();
			return BrowserEssentialsInterop.GetWakeLock();
		}
		set
		{
			BrowserEssentials.EnsureInitialized();
			keepScreenOnRequested = value;
			// Wake lock acquisition is async; fire and forget, tracked by the sentinel in JS.
			_ = BrowserEssentialsInterop.SetWakeLock(value);
		}
	}

	public DisplayInfo MainDisplayInfo
	{
		get
		{
			BrowserEssentials.EnsureInitialized();
			using var doc = JsonDocument.Parse(BrowserEssentialsInterop.GetDisplayInfo());
			var root = doc.RootElement;
			var orientationType = root.GetProperty("orientation").GetString() ?? string.Empty;
			var orientation = orientationType.StartsWith("portrait", StringComparison.Ordinal)
				? DisplayOrientation.Portrait
				: DisplayOrientation.Landscape;
			var rotation = orientationType switch
			{
				"portrait-primary" or "landscape-primary" => DisplayRotation.Rotation0,
				"landscape-secondary" => DisplayRotation.Rotation180,
				"portrait-secondary" => DisplayRotation.Rotation180,
				_ => DisplayRotation.Unknown,
			};
			return new DisplayInfo(
				width: root.GetProperty("width").GetDouble(),
				height: root.GetProperty("height").GetDouble(),
				density: root.GetProperty("pixelRatio").GetDouble(),
				orientation: orientation,
				rotation: rotation);
		}
	}

	public event EventHandler<DisplayInfoChangedEventArgs>? MainDisplayInfoChanged
	{
		add
		{
			EnsureWatching();
			mainDisplayInfoChanged += value;
		}
		remove => mainDisplayInfoChanged -= value;
	}

	event EventHandler<DisplayInfoChangedEventArgs>? mainDisplayInfoChanged;

	void EnsureWatching()
	{
		BrowserEssentials.EnsureInitialized();
		if (watching)
			return;
		watching = true;
		BrowserEssentialsInterop.WatchDisplay(() =>
		{
			mainDisplayInfoChanged?.Invoke(this, new DisplayInfoChangedEventArgs(MainDisplayInfo));
			// Wake locks are released by the browser when the page is hidden; re-request.
			if (keepScreenOnRequested && !BrowserEssentialsInterop.GetWakeLock())
				_ = BrowserEssentialsInterop.SetWakeLock(true);
		});
	}
}
