using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Chat;
using Microsoft.Maui.Chat.Controls;
using Microsoft.Maui.Chat.Controls.Blazor;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent;
using Microsoft.Maui.DevFlow.Blazor;
#endif

namespace ChatControls.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseChatControls()
            .AddChatControlsBlazor()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.AddMauiDevFlowAgent();
        builder.AddMauiBlazorDevFlowTools();
        builder.Logging.AddDebug();
#endif
        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton<IChatAudioRecorder, SimulatedChatAudioRecorder>();
        builder.Services.AddSingleton<IChatSpeechRecognizer, SimulatedChatSpeechRecognizer>();
        builder.Services.AddSingleton<TeamChatViewModel>();
        builder.Services.AddSingleton<MainPage>();

        return builder.Build();
    }
}
