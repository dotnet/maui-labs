using Foundation;
using UIKit;

namespace CometOrderingApp;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => OrderingApp.CreateMauiApp();
}
