using System.Runtime.Versioning;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

/// <summary>Haptic feedback approximated with short navigator.vibrate pulses.</summary>
[SupportedOSPlatform("browser")]
public class BrowserHapticFeedback : IHapticFeedback
{
	public bool IsSupported
	{
		get
		{
			BrowserEssentials.EnsureInitialized();
			return BrowserEssentialsInterop.VibrationIsSupported();
		}
	}

	public void Perform(HapticFeedbackType type = HapticFeedbackType.Click)
	{
		BrowserEssentials.EnsureInitialized();
		if (!BrowserEssentialsInterop.VibrationIsSupported())
			throw new FeatureNotSupportedException("The Vibration API is not available in this browser.");
		BrowserEssentialsInterop.Vibrate(type == HapticFeedbackType.LongPress ? 25 : 10);
	}
}
