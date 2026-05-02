using Foundation;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform.MacOS.Hosting;

namespace CometControlsGallery
{
	[Register("MauiMacOSApp")]
	public class MauiMacOSApp : MacOSMauiApplication
	{
		protected override MauiApp CreateMauiApp() => App.CreateMauiApp();
	}
}
