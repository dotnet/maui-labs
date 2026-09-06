using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.CopilotSdk;
using Microsoft.Maui.DevFlow.Agent;

namespace CopilotSdkSample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Register the Copilot SDK IChatClient
        builder.Services.AddCopilotSdkChatClient(config =>
        {
            config.Model = "gpt-4.1";
            config.UseLoggedInUser = true;
            config.SystemMessage = "You are a helpful assistant. Be concise.";
            // Point to a known working Copilot CLI binary.
            // The SDK bundles one but it doesn't get copied into the Mac Catalyst app bundle.
            var cachedCli = Directory.GetDirectories(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Caches"),
                    "copilot-sdk-*")
                .OrderByDescending(d => d)
                .Select(d => Path.Combine(d, "copilot"))
                .FirstOrDefault(File.Exists);
            if (cachedCli is not null)
                config.CliPath = cachedCli;
        });

        // Register sample tools
        builder.Services.AddSingleton<IList<AITool>>(sp =>
        [
            AIFunctionFactory.Create(
                ([Description("The city name")] string city) =>
                    $"Weather in {city}: 22°C, Sunny with light clouds.",
                name: "get_weather",
                description: "Get the current weather for a city"),
            AIFunctionFactory.Create(
                () => DateTime.Now.ToString("F"),
                name: "get_current_time",
                description: "Get the current date and time"),
        ]);

        builder.Services.AddTransient<ChatViewModel>();
        builder.Services.AddTransient<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
        builder.AddMauiDevFlowAgent(options =>
        {
            options.Port = 9224;
        });
#endif

        return builder.Build();
    }
}
