using Microsoft.Maui.Devices;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class VibrationImplementation : IVibration
{
	// macOS does not have a vibration motor on most hardware.
	// NSHapticFeedbackManager exists on some MacBooks with Force Touch trackpads
	// but it's not a general vibration API. We report as unsupported.
	public bool IsSupported => false;

	public void Vibrate() { }

	public void Vibrate(TimeSpan duration) { }

	public void Cancel() { }
}
