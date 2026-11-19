using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Microsoft.Maui.Hosting;
#if (essentials)
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Hosting;
#endif

namespace MauiLinuxApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp
            .CreateBuilder()
            .UseMauiAppLinuxGtk4<App>();

#if (essentials)
        builder.AddLinuxGtk4Essentials();

#endif
        return builder.Build();
    }
}
