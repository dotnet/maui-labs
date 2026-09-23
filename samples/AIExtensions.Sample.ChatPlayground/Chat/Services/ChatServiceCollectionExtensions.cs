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
    public static IServiceCollection AddChatFeature(this IServiceCollection services, AISettings settings)
    {
        services.AddSingleton<PlaygroundTools>();
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
            services.AddSingleton<IChatClient>(CreateAppleChatClient);
#endif

        if (!string.IsNullOrWhiteSpace(settings.DeploymentName))
            services.AddSingleton<IChatClient>(serviceProvider => CreateAzureChatClient(serviceProvider, settings));

        services.AddSingleton<IChatClient>(CreateReplayChatClient);

        return services;
    }

#if IOS || MACCATALYST
    [SupportedOSPlatform("ios26.0")]
    [SupportedOSPlatform("maccatalyst26.0")]
    private static IChatClient CreateAppleChatClient(IServiceProvider serviceProvider) =>
        new AppleIntelligenceChatClient(serviceProvider.GetRequiredService<ILoggerFactory>())
            .AsBuilder()
            .UseRecording(serviceProvider.GetRequiredService<IChatRecordingSession>())
            .UseDescriptor(new ChatClientDescriptor(
                "apple-intelligence-chat",
                "Apple Intelligence",
                "Apple Intelligence is supported on this OS. The first request confirms that the local model is enabled and available.",
                SupportsImageInput: OperatingSystem.IsIOSVersionAtLeast(27) ||
                    OperatingSystem.IsMacCatalystVersionAtLeast(27),
                SupportsToolCalling: true))
            .UseLogging(serviceProvider.GetRequiredService<ILoggerFactory>())
            .UseFunctionInvocation()
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
            : openAIClient.GetImageClient(imageDeployment).AsIImageGenerator();

        // Recording wraps the tool and image middleware so the saved response is the one shown in chat.
        var builder = openAIClient.GetResponsesClient()
            .AsIChatClient(deploymentName)
            .AsBuilder()
            .UseRecording(serviceProvider.GetRequiredService<IChatRecordingSession>())
            .UseDescriptor(new ChatClientDescriptor(
                "azure-openai-chat",
                "Azure OpenAI",
                $"Azure OpenAI deployment '{deploymentName}' is ready." +
                    (imageGenerator is null ? "" : $" Image generation uses deployment '{imageDeployment}'."),
                SupportsImageInput: true,
                SupportsReasoningSummary: true,
                SupportsImageGeneration: imageGenerator is not null,
                SupportsToolCalling: true))
            .UseLogging(serviceProvider.GetRequiredService<ILoggerFactory>());
        if (imageGenerator is not null)
            builder.UseImageGenerationPreservingInputs(imageGenerator);
        return builder.UseFunctionInvocation().Build();
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

    private static ChatClientBuilder UseRecording(this ChatClientBuilder builder, IChatRecordingSession recording) =>
        builder.Use(inner => new RecordingChatClient(inner, recording));

    private static ChatClientBuilder UseDescriptor(this ChatClientBuilder builder, ChatClientDescriptor descriptor) =>
        builder.Use(inner => new DescribedChatClient(inner, descriptor));

    private static ChatClientBuilder UseImageGenerationPreservingInputs(this ChatClientBuilder builder, IImageGenerator generator) =>
        builder.Use(inner => new ImageGeneratingChatClient(inner, generator, ImageGeneratingChatClient.DataContentHandling.GeneratedImages));
}
