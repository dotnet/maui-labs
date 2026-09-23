using System.Reflection;
using System.ClientModel;
using AIExtensions.Sample.ChatPlayground.Features.Recording;
using AIExtensions.Sample.ChatPlayground.Services;
using AIExtensions.Sample.ChatPlayground.ViewModels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.DevFlow.Agent;
using OpenAI;

#if IOS || MACCATALYST
using Microsoft.Maui.Essentials.AI;
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
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
        AddChatClients(builder.Services, builder.Configuration);

#if DEBUG
        builder.AddMauiDevFlowAgent();
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<ImageInputService>();
        builder.Services.AddSingleton<PlaygroundTools>();
        builder.Services.AddSingleton(serviceProvider => new ChatRecordingService(
            serviceProvider.GetRequiredService<ILogger<ChatRecordingService>>(),
            FileSystem.CacheDirectory));
        builder.Services.AddSingleton<SettingsPaneViewModel>();
        builder.Services.AddSingleton<ChatAreaViewModel>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddTransient<MainPage>();

        return builder.Build();
    }

    private static void AddChatClients(IServiceCollection services, IConfiguration configuration)
    {
#if IOS || MACCATALYST
        if (OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26))
            services.AddSingleton<IChatClient>(serviceProvider => CreateLocalChatClient(
                serviceProvider.GetRequiredService<ILoggerFactory>(),
                serviceProvider.GetRequiredService<ChatRecordingService>()));
#endif
        if (!string.IsNullOrWhiteSpace(configuration["AI:Endpoint"]) ||
            !string.IsNullOrWhiteSpace(configuration["AI:ApiKey"]) ||
            !string.IsNullOrWhiteSpace(configuration["AI:DeploymentName"]) ||
            !string.IsNullOrWhiteSpace(configuration["AI:ImageDeploymentName"]))
        {
            services.AddSingleton<IChatClient>(serviceProvider => CreateCloudChatClient(
                configuration["AI:Endpoint"],
                configuration["AI:ApiKey"],
                configuration["AI:DeploymentName"],
                configuration["AI:ImageDeploymentName"],
                serviceProvider.GetRequiredService<ILoggerFactory>(),
                serviceProvider.GetRequiredService<ChatRecordingService>()));
        }

        if (!services.Any(service => service.ServiceType == typeof(IChatClient)))
            throw new InvalidOperationException(
                "No real chat client is configured. Use iOS or Mac Catalyst 26+ or configure " +
                "AI:Endpoint, AI:ApiKey, AI:DeploymentName, and AI:ImageDeploymentName.");

        services.AddSingleton<IChatClient>(serviceProvider =>
            new ReplayChatClient(serviceProvider.GetRequiredService<ChatRecordingService>())
                .AsBuilder()
                .UseDescriptor(new ChatClientDescriptor(
                    "Replay",
                    "Play the saved chat without contacting a model. Switch to a live client after playback to continue.",
                    SupportsImageInput: false,
                    IsReplay: true))
                .Build());
    }

#pragma warning disable MEAI001, OPENAI001 // Responses and image adapters are experimental in the installed SDK.
    private static IChatClient CreateCloudChatClient(
        string? endpointValue,
        string? apiKey,
        string? deploymentName,
        string? imageDeploymentName,
        ILoggerFactory loggerFactory,
        ChatRecordingService recording)
    {
        if (string.IsNullOrWhiteSpace(endpointValue) ||
            string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(deploymentName) ||
            string.IsNullOrWhiteSpace(imageDeploymentName))
        {
            throw new InvalidOperationException(
                "Configure AI:Endpoint, AI:ApiKey, AI:DeploymentName, and AI:ImageDeploymentName in the shared local user secrets.");
        }

        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint) ||
            !endpoint.AbsolutePath.TrimEnd('/').EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("AI:Endpoint must be an absolute OpenAI-compatible endpoint ending in /openai/v1/.");

        var openAIClient = CreateOpenAIClient(endpoint, apiKey);
        var generator = openAIClient.GetImageClient(imageDeploymentName).AsIImageGenerator();
        // Recording wraps the tool and image middleware so the saved response is the one shown in chat.
        var client = openAIClient.GetResponsesClient().AsIChatClient(deploymentName)
            .AsBuilder()
            .UseRecording(recording)
            .UseDescriptor(new ChatClientDescriptor(
                "Azure OpenAI",
                $"Azure OpenAI deployment '{deploymentName}' is ready. Image generation uses '{imageDeploymentName}'.",
                SupportsImageInput: true,
                SupportsReasoningSummary: true,
                SupportsImageGeneration: true))
            .UseLogging(loggerFactory)
            .UseImageGenerationPreservingInputs(generator)
            .UseFunctionInvocation()
            .Build();

        return client;
    }

    private static OpenAIClient CreateOpenAIClient(Uri endpoint, string apiKey) =>
        new(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = endpoint });
#pragma warning restore MEAI001, OPENAI001

#if IOS || MACCATALYST
    private static IChatClient CreateLocalChatClient(ILoggerFactory loggerFactory, ChatRecordingService recording)
    {
#pragma warning disable CA1416
        var client = new AppleIntelligenceChatClient(loggerFactory)
            .AsBuilder()
            .UseRecording(recording)
            .UseDescriptor(new ChatClientDescriptor(
                "Apple Intelligence",
                "Apple Intelligence is supported on this OS. The first request confirms that the local model is enabled and available.",
                SupportsImageInput: false))
            .UseLogging(loggerFactory)
            .UseFunctionInvocation()
            .Build();
#pragma warning restore CA1416

        return client;
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
