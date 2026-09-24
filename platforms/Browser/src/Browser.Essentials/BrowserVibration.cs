using System.Runtime.Versioning;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

/// <summary>Vibration backed by navigator.vibrate (mobile browsers; no-ops when unsupported by hardware).</summary>
[SupportedOSPlatform("browser")]
public class BrowserVibration : IVibration
{
	public bool IsSupported
	{
		get
		{
			BrowserEssentials.EnsureInitialized();
			return BrowserEssentialsInterop.VibrationIsSupported();
		}
	}

	public void Vibrate() => Vibrate(TimeSpan.FromMilliseconds(500));

	public void Vibrate(TimeSpan duration)
	{
		BrowserEssentials.EnsureInitialized();
		if (!BrowserEssentialsInterop.VibrationIsSupported())
			throw new FeatureNotSupportedException("The Vibration API is not available in this browser.");
		BrowserEssentialsInterop.Vibrate(duration.TotalMilliseconds);
	}

	public void Cancel()
	{
		BrowserEssentials.EnsureInitialized();
		if (BrowserEssentialsInterop.VibrationIsSupported())
			BrowserEssentialsInterop.Vibrate(0);
	}
}
