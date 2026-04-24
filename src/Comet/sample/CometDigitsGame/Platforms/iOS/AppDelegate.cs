using Foundation;
using UIKit;

namespace CometDigitsGame;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => DigitsGameApp.CreateMauiApp();
}
