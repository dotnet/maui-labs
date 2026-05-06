using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace CometMauiApp
{
	[Register("AppDelegate")]
	public class AppDelegate : MauiUIApplicationDelegate
	{
		protected override MauiApp CreateMauiApp() => MyApp.CreateMauiApp();
	}
}
