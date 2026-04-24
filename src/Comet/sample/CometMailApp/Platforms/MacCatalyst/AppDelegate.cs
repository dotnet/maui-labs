using Foundation;

namespace CometMailApp;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MailApp.CreateMauiApp();
}
