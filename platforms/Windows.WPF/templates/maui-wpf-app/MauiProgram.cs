using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF.Essentials;

namespace MauiWpfApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiAppWPF<App>()
            .UseWPFEssentials();

        return builder.Build();
    }
}
