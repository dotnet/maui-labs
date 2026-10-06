using System.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.DevFlow.Agent;

#if IOS || MACCATALYST
using System.Runtime.Versioning;
#endif

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Configures dependency injection and developer-only diagnostics.</summary>
public static class MauiProgram
{
    /// <summary>Creates the MAUI application.</summary>
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Configuration.AddEmbeddedUserSecrets();
        var aiSettings = new AISettings();
        builder.Configuration.GetSection(AISettings.SectionName).Bind(aiSettings);
        aiSettings.Validate();

        builder.Services.AddSingleton<ImageInputService>();
        builder.Services.AddSingleton<ChatDiagnostics>();
        builder.Services.AddTransient<DiagnosticsViewModel>();
        builder.Services.AddSingleton<ILoggerProvider>(provider => provider.GetRequiredService<ChatDiagnostics>());
        builder.Logging.AddFilter<ChatDiagnostics>("Microsoft.Extensions.AI", LogLevel.Debug);
        builder.Logging.AddFilter<ChatDiagnostics>(ChatDiagnostics.AppleToolLogCategory, LogLevel.Debug);
        builder.Services.AddChatFeature();
#if IOS || MACCATALYST
        if (OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26))
            AddAppleChatClients(builder.Services, aiSettings);
#endif
        if (!string.IsNullOrWhiteSpace(aiSettings.DeploymentName))
            builder.Services.AddSingleton<IChatClient>(provider =>
                ChatServiceCollectionExtensions.CreateAzureChatClient(provider, aiSettings));
        builder.Services.AddSingleton<IChatClient>(ChatServiceCollectionExtensions.CreateReplayChatClient);

        builder.Services.AddEmbeddingFeature(aiSettings);
        builder.Services.AddImageFeature(aiSettings);
        builder.Services.AddTransient<MainWindow>();

#if DEBUG
        builder.AddMauiDevFlowAgent();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

#if IOS || MACCATALYST
    [SupportedOSPlatform("ios26.0")]
    [SupportedOSPlatform("maccatalyst26.0")]
    private static void AddAppleChatClients(IServiceCollection services, AISettings settings)
    {
        services.AddSingleton<IChatClient>(ChatServiceCollectionExtensions.CreateAppleChatClient);
        services.AddSingleton<IChatClient>(provider =>
            ChatServiceCollectionExtensions.CreateHybridChatClient(provider, settings));
    }
#endif

    private static void AddEmbeddedUserSecrets(this ConfigurationManager configuration)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("secrets.json", StringComparison.OrdinalIgnoreCase));

        if (resourceName is not null && assembly.GetManifestResourceStream(resourceName) is { } stream)
        {
            using (stream)
                configuration.AddJsonStream(stream);
        }
    }
}
