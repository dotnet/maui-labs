using AppKit;
using Foundation;
using Microsoft.Maui.Accessibility;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class SemanticScreenReaderImplementation : ISemanticScreenReader
{
	public void Announce(string text)
	{
		if (string.IsNullOrEmpty(text))
			return;

		// Post an announcement notification for VoiceOver using raw notification name
		var userInfo = new NSDictionary(
			new NSString("NSAccessibilityAnnouncementKey"),
			new NSString(text));

		NSAccessibility.PostNotification(
			NSApplication.SharedApplication,
			new NSString("AXAnnouncementRequested"),
			userInfo);
	}
}
