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
        // The plugin: wrap Shiny's IBleManager so activity is recorded, then add the plugin to the agent.
        builder.Services.AddShinyBleDevFlowMonitor();
        builder.AddMauiDevFlowAgent(options => options.AddExtension<ShinyBleDevFlowExtension>());
#endif

        return builder.Build();
    }
}
