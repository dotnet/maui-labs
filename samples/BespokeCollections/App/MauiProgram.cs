using Microsoft.Extensions.Logging;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent;
#endif

namespace BespokeCollections.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder()
            .UseMauiApp<App>()
            .ConfigureMauiHandlers(handlers => handlers.AddHandler<PhotoMasonryView, PhotoMasonryHandler>());
        builder.Services.AddSingleton<MainPage>();
#if DEBUG
        builder.Logging.AddDebug();
        builder.AddMauiDevFlowAgent();
#endif
        return builder.Build();
    }
}
