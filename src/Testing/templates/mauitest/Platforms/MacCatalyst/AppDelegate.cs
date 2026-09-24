using Foundation;
using Microsoft.Maui.Testing;

namespace MauiTest1;

[Register("AppDelegate")]
public sealed class AppDelegate : MauiTestAppDelegate
{
    protected override MauiTestApp CreateMauiTestApp() => MauiProgram.CreateMauiTestApp();
}
