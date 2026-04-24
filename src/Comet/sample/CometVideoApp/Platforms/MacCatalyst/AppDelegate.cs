using Foundation;

namespace CometVideoApp;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => VideoApp.CreateMauiApp();
}
