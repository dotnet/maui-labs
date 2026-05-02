using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace CometSurfingApp
{
	[Register("AppDelegate")]
	public class AppDelegate : MauiUIApplicationDelegate
	{
		protected override MauiApp CreateMauiApp() => MyApp.CreateMauiApp();
	}
}
