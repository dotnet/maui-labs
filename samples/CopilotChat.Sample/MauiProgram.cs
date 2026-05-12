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
#endif

        // Register Copilot SDK as the default IChatClient
        builder.Services.AddCopilotChatWithCopilotSdk(options =>
        {
            options.Model = "gpt-4.1";
            options.SystemMessage = "You are a helpful assistant in a MAUI app. Be concise and friendly.";
        });

        return builder.Build();
    }
}
