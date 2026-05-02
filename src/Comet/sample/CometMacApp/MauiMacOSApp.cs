using Foundation;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform.MacOS.Hosting;

namespace CometMacApp;

[Register("MauiMacOSApp")]
public class MauiMacOSApp : MacOSMauiApplication
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
