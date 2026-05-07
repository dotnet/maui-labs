using Foundation;
using Microsoft.Maui.Platforms.MacOS.Platform;

namespace MauiMacOSApp;

[Register("MauiMacOSAppDelegate")]
public class MauiMacOSAppDelegate : MacOSMauiApplication
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
