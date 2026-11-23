using Shiny;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent;
using ShinyBle.DevFlowPlugin;
#endif

namespace ShinyBle.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseShiny();

        builder.Services.AddBluetoothLE();
        builder.Services.AddTransient<MainPage>();

#if DEBUG
        // The plugin: wraps Shiny's IBleManager so activity is recorded, and registers the plugin,
        // which the agent picks up from the app's services.
        builder.Services.AddShinyBleDevFlowMonitor();
        builder.AddMauiDevFlowAgent();
#endif

        return builder.Build();
    }
}
