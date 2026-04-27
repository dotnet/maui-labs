using Microsoft.Maui.Devices;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class HapticFeedbackImplementation : IHapticFeedback
{
	public bool IsSupported => true;

	public void Perform(HapticFeedbackType type)
	{
		var pattern = type == HapticFeedbackType.LongPress
			? AppKit.NSHapticFeedbackPattern.LevelChange
			: AppKit.NSHapticFeedbackPattern.Generic;

		AppKit.NSHapticFeedbackManager.DefaultPerformer.PerformFeedback(
			pattern,
			AppKit.NSHapticFeedbackPerformanceTime.Default);
	}
}
