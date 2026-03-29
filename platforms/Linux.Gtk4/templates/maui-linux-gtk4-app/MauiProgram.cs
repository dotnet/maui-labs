using Platform.Maui.Linux.Gtk4.Hosting;
using Microsoft.Maui.Hosting;

namespace MauiLinuxApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp
            .CreateBuilder()
            .UseMauiAppLinuxGtk4<App>();

        return builder.Build();
    }
}
