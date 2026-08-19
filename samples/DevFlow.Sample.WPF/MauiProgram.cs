using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.DevFlow.Agent.WPF;
using Microsoft.Maui.Platforms.Windows.WPF.Essentials;

namespace DevFlow.Sample;

public static class MauiProgram
{
    static int ResolveAgentPort()
        => int.TryParse(Environment.GetEnvironmentVariable("DEVFLOW_TEST_PORT"), out var port)
            ? port
            : 9223;

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder()
            .UseMauiAppWPF<App>()
            .UseWPFEssentials()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddWpfBlazorWebView();
        builder.Services.AddSingleton<TodoService>();
        builder.Services.AddHttpClient();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<BlazorTodoPage>();
        builder.Services.AddTransient<NetworkTestPage>();

#if DEBUG
        builder.Logging.AddDebug();
        builder.AddMauiDevFlowAgent(options =>
        {
            options.Port = ResolveAgentPort();
            options.EnableProfiler = true;
        });
#endif

        return builder.Build();
    }
}
