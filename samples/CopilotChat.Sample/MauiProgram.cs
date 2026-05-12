using Microsoft.Extensions.Logging;
using Microsoft.Maui.CopilotChat;
using Microsoft.Maui.DevFlow.Agent;

namespace CopilotChat.Sample;

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

#if DEBUG
        builder.AddMauiDevFlowAgent();
        builder.Logging.AddDebug();
#endif

        // Register the Copilot SDK as the default IChatClient
        builder.Services.AddCopilotChatWithCopilotSdk(options =>
        {
            options.Model = "gpt-4.1";
            options.SystemMessage = "You are a helpful assistant. Be concise and friendly. When using tools, explain what you're doing.";
            options.CliPath = "/opt/homebrew/bin/copilot";
        });

        // Register sample tools
        builder.Services.AddSingleton<SampleTools>();

        // Register pages
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<ChatPage>();
        builder.Services.AddTransient<SettingsPage>();

        return builder.Build();
    }
}
