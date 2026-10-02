using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using UIKit;

namespace LifecycleQualification;

public static class Program
{
    public static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}

[Register("AppDelegate")]
public sealed class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp()
    {
        Recorder.Start();
        var builder = MauiApp.CreateBuilder().UseMauiApp<QualificationApp>();
        MyApp.MigrationRegistration.Configure(builder);
        Contracts.ObserveBaseForwarding(builder);
        return builder.Build();
    }
}

public sealed class QualificationApp : Application
{
    protected override Microsoft.Maui.Controls.Window CreateWindow(IActivationState? activationState) => NewWindow();

    internal static Microsoft.Maui.Controls.Window NewWindow() => new(new ContentPage
    {
        Content = new Label { Text = "Lifecycle qualification: results are in Documents/lifecycle-events.jsonl." }
    });
}
