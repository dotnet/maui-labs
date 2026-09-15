using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.AI.Chat.Controls;
using Microsoft.Maui.AI.Chat.Controls.Blazor;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent;
using Microsoft.Maui.DevFlow.Blazor;
#endif

namespace AIChat.Client.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().UseChatControls().AddAIChatControlsBlazor();
        using var settings = OpenResource("appsettings.json");
        builder.Configuration.AddJsonStream(settings);
        builder.Configuration.AddUserSecrets(typeof(MauiProgram).Assembly, optional: true);
        builder.Configuration.AddEnvironmentVariables();
        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.AddMauiDevFlowAgent();
        builder.AddMauiBlazorDevFlowTools();
        builder.Logging.AddDebug();
#endif
        builder.Services.AddSingleton<DirectChatComposition>();
        builder.Services.AddSingleton<MainPage>();
        return builder.Build();
    }

    internal static Stream OpenResource(string fileName)
    {
        var assembly = typeof(MauiProgram).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(
                $".Configuration.{fileName}",
                StringComparison.Ordinal));
        return resourceName is null
            ? throw new InvalidOperationException($"Embedded resource '{fileName}' was not found.")
            : assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{fileName}' could not be opened.");
    }
}
