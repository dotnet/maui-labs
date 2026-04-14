using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Platforms.Windows.WPF.Essentials;
#if DEBUG
#endif

namespace Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp
            .CreateBuilder()
            .UseMauiAppWPF<App>()
            .UseWPFEssentials();

        builder.ConfigureFonts(fonts =>
        {
            fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
        });

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        //builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        return builder.Build();
    }
}
