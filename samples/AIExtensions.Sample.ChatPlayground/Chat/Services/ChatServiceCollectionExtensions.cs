using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;

#if IOS || MACCATALYST
using System.Runtime.Versioning;
using Microsoft.Maui.Essentials.AI;
#endif

namespace AIExtensions.Sample.ChatPlayground;

internal static class ChatServiceCollectionExtensions
{
    private const string LocalClientKey = "local-chat";
    private const string AzureClientKey = "azure-openai-chat";
    private const string HybridClientKey = "hybrid-chat";

    public static IServiceCollection AddChatFeature(this IServiceCollection services, AISettings settings)
    {
        services.AddSingleton<ConnectionStatusService>();
        services.AddSingleton(serviceProvider => new PlaygroundTools(
            serviceProvider.GetRequiredService<ConnectionStatusService>().GetStatus));
        services.AddSingleton(serviceProvider => new ChatSessionService(
            serviceProvider.GetRequiredService<ILogger<ChatSessionService>>(),
            FileSystem.AppDataDirectory,
            FileSystem.CacheDirectory));
        services.AddSingleton<IChatRecordingSession>(provider => provider.GetRequiredService<ChatSessionService>());
        services.AddSingleton<SettingsPaneViewModel>();
        services.AddSingleton<ChatAreaViewModel>();
        services.AddSingleton<ChatViewModel>();
        services.AddTransient<Page, ChatPage>();

#if IOS || MACCATALYST
        if (OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26))
        {
            services.AddKeyedSingleton<IChatClient>(LocalClientKey, CreateAppleChatClient);
            services.AddSingleton<IChatClient>(provider => CreateRecordedClient(provider, LocalClientKey));
        }
#endif

        if (!string.IsNullOrWhiteSpace(settings.DeploymentName))
        {
            services.AddKeyedSingleton<IChatClient>(AzureClientKey, (provider, _) => CreateAzureChatClient(provider, settings));
            services.AddSingleton<IChatClient>(provider => CreateRecordedClient(provider, AzureClientKey));
        }

#if IOS || MACCATALYST
        if ((OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26)) &&
            !string.IsNullOrWhiteSpace(settings.DeploymentName))
        {
            services.AddKeyedSingleton<IChatClient>(HybridClientKey, CreateHybridChatClient);
            services.AddSingleton<IChatClient>(provider => CreateRecordedClient(provider, HybridClientKey));
        }
#endif

        services.AddSingleton<IChatClient>(CreateReplayChatClient);

        return services;
    }

#if IOS || MACCATALYST
    [SupportedOSPlatform("ios26.0")]
    [SupportedOSPlatform("maccatalyst26.0")]
    private static IChatClient CreateAppleChatClient(IServiceProvider serviceProvider, object? key) =>
        new AppleIntelligenceChatClient(serviceProvider.GetRequiredService<ILoggerFactory>())
            .AsBuilder()
            .UseDescriptor(new ChatClientDescriptor(
                "apple-intelligence-chat",
                "Apple Intelligence",
                "Apple Intelligence is supported on this OS. The first request confirms that the local model is enabled and available.",
                SupportsImageInput: OperatingSystem.IsIOSVersionAtLeast(27) || OperatingSystem.IsMacCatalystVersionAtLeast(27),
                SupportsToolCalling: true))
            .UsePlaygroundTelemetry()
            .UseLogging(serviceProvider.GetRequiredService<ILoggerFactory>())
            .UseFunctionInvocation(serviceProvider.GetRequiredService<ILoggerFactory>())
            .Build();
#endif

    private static IChatClient CreateAzureChatClient(IServiceProvider serviceProvider, AISettings settings)
    {
        var deploymentName = settings.DeploymentName!;
        var openAIClient = new OpenAIClient(
            new ApiKeyCredential(settings.ApiKey!),
            new OpenAIClientOptions { Endpoint = settings.Endpoint! });
        var imageDeployment = settings.ImageDeploymentName;
        var imageGenerator = string.IsNullOrWhiteSpace(imageDeployment)
            ? null
            : serviceProvider.GetRequiredService<IImageGenerator>();

        var builder = openAIClient.GetResponsesClient()
            .AsIChatClient(deploymentName)
            .AsBuilder()
            .UseDescriptor(new ChatClientDescriptor(
                "azure-openai-chat",
                "Azure OpenAI",
                $"Azure OpenAI deployment '{deploymentName}' is ready." +
                    (imageGenerator is null ? "" : $" Image generation uses deployment '{imageDeployment}'."),
                SupportsImageInput: true,
                SupportsReasoningSummary: true,
                SupportsImageGeneration: imageGenerator is not null,
                SupportsToolCalling: true))
            .UsePlaygroundTelemetry()
            .UseLogging(serviceProvider.GetRequiredService<ILoggerFactory>());
        if (imageGenerator is not null)
            builder.UseImageGenerationPreservingInputs(imageGenerator);
        return builder.UseFunctionInvocation(serviceProvider.GetRequiredService<ILoggerFactory>()).Build();
    }

    private static IChatClient CreateReplayChatClient(IServiceProvider serviceProvider) =>
        new ReplayChatClient(serviceProvider.GetRequiredService<IChatRecordingSession>())
            .AsBuilder()
            .UseDescriptor(new ChatClientDescriptor(
                "replay",
                "Replay",
                "Play the saved chat without contacting a model. Switch to a live client after playback to continue.",
                IsReplay: true))
            .Build();

    private static IChatClient CreateHybridChatClient(IServiceProvider serviceProvider, object? key)
    {
        var local = serviceProvider.GetRequiredKeyedService<IChatClient>(LocalClientKey);
        var cloud = serviceProvider.GetRequiredKeyedService<IChatClient>(AzureClientKey);
        return new HybridChatClient(local, cloud, serviceProvider.GetRequiredService<ILoggerFactory>())
            .AsBuilder()
            .UseDescriptor(new ChatClientDescriptor(
                "hybrid-chat",
                "Hybrid (local + cloud)",
                "Local model chooses local or cloud for each turn. Cloud requests fall back to local if they fail before output.",
                SupportsToolCalling: true))
            .UsePlaygroundDiagnostics(serviceProvider.GetRequiredService<ILoggerFactory>())
            .Build();
    }

    private static IChatClient CreateRecordedClient(IServiceProvider serviceProvider, string key) =>
        new RecordingChatClient(
            serviceProvider.GetRequiredKeyedService<IChatClient>(key),
            serviceProvider.GetRequiredService<IChatRecordingSession>(),
            leaveOpen: true);

    private static ChatClientBuilder UseDescriptor(this ChatClientBuilder builder, ChatClientDescriptor descriptor) =>
        builder.Use(inner => new DescribedChatClient(inner, descriptor));

    private static ChatClientBuilder UseImageGenerationPreservingInputs(this ChatClientBuilder builder, IImageGenerator generator) =>
        builder.Use(inner => new ImageGeneratingChatClient(inner, generator, ImageGeneratingChatClient.DataContentHandling.GeneratedImages));
}
