using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.DevFlow.Agent;

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
        builder.Services.AddChatFeature(aiSettings);
        builder.Services.AddEmbeddingFeature(aiSettings);
        builder.Services.AddImageFeature(aiSettings);
        builder.Services.AddTransient<MainWindow>();

#if DEBUG
        builder.AddMauiDevFlowAgent();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

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
