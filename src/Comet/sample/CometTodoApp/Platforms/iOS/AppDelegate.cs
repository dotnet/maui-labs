using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace CometTodoApp
{
	[Register("AppDelegate")]
	public class AppDelegate : MauiUIApplicationDelegate
	{
		protected override MauiApp CreateMauiApp() => TodoApp.CreateMauiApp();
	}
}
