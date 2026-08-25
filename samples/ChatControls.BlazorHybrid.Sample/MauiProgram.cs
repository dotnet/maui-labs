using ChatControls.Sample;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Chat.Controls;
using Microsoft.Maui.Chat.Controls.Blazor;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent;
using Microsoft.Maui.DevFlow.Blazor;
#endif

namespace ChatControls.BlazorHybrid.Sample;

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

        builder.Services.AddMauiBlazorWebView();

        // Simulated microphone / speech services keep the DevFlow-driven runtime validation
        // deterministic on any host, without touching the platform microphone.
        builder.Services.AddSingleton<IChatAudioRecorder, SimulatedChatAudioRecorder>();
        builder.Services.AddSingleton<IChatSpeechRecognizer, SimulatedChatSpeechRecognizer>();

        // Single shared view model so the XAML sidebar and the Blazor chat surface talk to
        // the same conversation and simulator commands. This mirrors ChatControls.Sample.
        builder.Services.AddSingleton<TeamChatViewModel>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
        builder.AddMauiDevFlowAgent();
        builder.AddMauiBlazorDevFlowTools();
#endif

        return builder.Build();
    }
}
