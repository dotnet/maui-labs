using Platform.Maui.Linux.Gtk4.Platform;
using Microsoft.Maui.Hosting;

namespace MauiLinuxApp;

public class Program : GtkMauiApplication
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public static void Main(string[] args)
    {
        var app = new Program();
        app.Run(args);
    }
}
