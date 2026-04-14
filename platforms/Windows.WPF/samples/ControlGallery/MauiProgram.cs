using ControlGallery.Common.Effects;
using Fonts;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Platforms.Windows.WPF.Essentials;
using Syncfusion.Maui.Toolkit.Hosting;

namespace ControlGallery;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiAppWPF<App>()
            .UseMauiCommunityToolkit()
            .UseMauiCommunityToolkitMarkup()
            .ConfigureSyncfusionToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("fa_solid.ttf", "FontAwesome");
                fonts.AddFont("opensans_regular.ttf", "OpenSansRegular");
                fonts.AddFont("opensans_semibold.ttf", "OpenSansSemiBold");
                fonts.AddFont("fabmdl2.ttf", "FabMDL2");
                fonts.AddFont("FluentSystemIcons-Regular.ttf", FluentUI.FontFamily);
            })
            ;

        var app = builder.Build();

        return app;
    }

}