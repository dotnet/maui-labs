using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace CometRecipeApp;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => RecipeApp.CreateMauiApp();
}
