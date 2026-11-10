using System.ClientModel;
using System.Runtime.Versioning;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;

#if IOS || MACCATALYST || ANDROID || WINDOWS
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
        services.AddSingleton<ModelReadinessViewModel>();
        services.AddSingleton<ChatViewModel>();
        services.AddTransient<Page, ChatPage>();

#if WINDOWS
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100))
            services.AddRecordedChatClient(LocalClientKey, CreateWindowsChatClient);
#endif

#if IOS || MACCATALYST
        if (OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26))
            services.AddRecordedChatClient(LocalClientKey, CreateAppleChatClient);
#endif
#if ANDROID
        services.AddRecordedChatClient(LocalClientKey, CreateGeminiChatClient);
#endif

        if (!string.IsNullOrWhiteSpace(settings.DeploymentName))
            services.AddRecordedChatClient(AzureClientKey, (provider, _) => CreateAzureChatClient(provider, settings));

#if IOS || MACCATALYST
        if ((OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26)) &&
            !string.IsNullOrWhiteSpace(settings.DeploymentName))
            services.AddRecordedChatClient(HybridClientKey, CreateHybridChatClient);
#endif

        services.AddSingleton<IChatClient>(CreateReplayChatClient);

        return services;
    }

#if WINDOWS
    [SupportedOSPlatform("windows10.0.26100.0")]
    private static IChatClient CreateWindowsChatClient(IServiceProvider serviceProvider, object? key) =>
        new WindowsAIChatClient()
            .AsBuilder()
            .UseDescriptor(new ChatClientDescriptor(
                "windows-ai-chat",
                "Windows AI",
                "Check model readiness above the composer and prepare the model when needed. Tool calling and image input are unavailable.",
                GetModelStatusAsync: (_, cancellationToken) => WindowsAIChatClient.GetModelStatusAsync(cancellationToken),
                PrepareModelAsync: (_, _, cancellationToken) => WindowsAIChatClient.EnsureReadyAsync(cancellationToken)))
            .UsePlaygroundTelemetry()
            .UseLogging(serviceProvider.GetRequiredService<ILoggerFactory>())
            .Build();
#endif

#if IOS || MACCATALYST
    [SupportedOSPlatform("ios26.0")]
    [SupportedOSPlatform("maccatalyst26.0")]
    private static IChatClient CreateAppleChatClient(IServiceProvider serviceProvider, object? key) =>
        new AppleIntelligenceChatClient(serviceProvider.GetRequiredService<ILoggerFactory>())
            .AsBuilder()
            .UseDescriptor(new ChatClientDescriptor(
                "apple-intelligence-chat",
                "Apple Intelligence",
                "Model readiness is checked above the composer. Apple manages model preparation in system settings.",
                SupportsImageInput: OperatingSystem.IsIOSVersionAtLeast(27) || OperatingSystem.IsMacCatalystVersionAtLeast(27),
                SupportsToolCalling: true,
                GetModelStatusAsync: (_, cancellationToken) => AppleIntelligenceChatClient.GetModelStatusAsync(cancellationToken)))
            .UsePlaygroundTelemetry()
            .UseLogging(serviceProvider.GetRequiredService<ILoggerFactory>())
            .UseFunctionInvocation(serviceProvider.GetRequiredService<ILoggerFactory>())
            .Build();
#endif

#if ANDROID
    [SupportedOSPlatform("android26.0")]
    private static IChatClient CreateGeminiChatClient(IServiceProvider serviceProvider, object? key) =>
        new GeminiNanoChatClient()
            .AsBuilder()
            .UseDescriptor(new ChatClientDescriptor(
                "gemini-nano-chat",
                "Gemini Nano",
                "Gemini Nano includes the supplied conversation in each text/image request. " +
                    "Native tool calling requires a capable Nano V4 model, such as Preview fast. " +
                    "Required tool mode and structured JSON are unavailable. System instructions require Nano V3+. " +
                    "Use Download above the composer to prepare the model; requests never download automatically.",
                SupportsImageInput: true,
                SupportsReasoningSummary: false,
                SupportsImageGeneration: false,
                SupportsToolCalling: true,
                SupportsStructuredOutput: false,
                GetModelStatusAsync: GeminiNanoChatClient.GetModelStatusAsync,
                PrepareModelAsync: DownloadGeminiModelAsync,
                PreparationActionLabel: "Download",
                Models:
                [
                    new("gemini-nano", "Default (stable/full)"),
                    new("gemini-nano-fast", "Stable fast"),
                    new("gemini-nano-full", "Stable full"),
                    new("gemini-nano-preview-fast", "Preview fast"),
                    new("gemini-nano-preview-full", "Preview full"),
                ],
                SupportsRequiredToolMode: false))
            .UsePlaygroundTelemetry()
            .UseLogging(serviceProvider.GetRequiredService<ILoggerFactory>())
            .UseFunctionInvocation(serviceProvider.GetRequiredService<ILoggerFactory>())
            .Build();

    [SupportedOSPlatform("android26.0")]
    private static async Task DownloadGeminiModelAsync(
        string? modelId, IProgress<ModelPreparationProgress> progress, CancellationToken cancellationToken)
    {
        long? totalBytes = null;
        long? downloadedBytes = null;
        await foreach (var update in GeminiNanoChatClient.DownloadModelAsync(modelId, cancellationToken))
        {
            if (update.BytesToDownload is { } total)
                totalBytes = total;
            if (update.BytesDownloaded is { } downloaded)
                downloadedBytes = downloaded;
            progress.Report(new ModelPreparationProgress(downloadedBytes, totalBytes));
        }
    }
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
        return new HybridChatClient(local, cloud, serviceProvider.GetRequiredService<ILogger<HybridChatClient>>())
            .AsBuilder()
            .UseDescriptor(new ChatClientDescriptor(
                "hybrid-chat",
                "Hybrid (local + cloud)",
                "Local model chooses local or cloud for each turn. Cloud requests fall back to local if they fail before output.",
                SupportsToolCalling: true))
            .UsePlaygroundTelemetry()
            .UseLogging(serviceProvider.GetRequiredService<ILoggerFactory>())
            .Build();
    }

    // Hybrid reuses the keyed clients, so only the selected chat client records an interaction.
    private static void AddRecordedChatClient(
        this IServiceCollection services, string key, Func<IServiceProvider, object?, IChatClient> factory)
    {
        services.AddKeyedSingleton<IChatClient>(key, factory);
        services.AddSingleton<IChatClient>(provider => new RecordingChatClient(
            provider.GetRequiredKeyedService<IChatClient>(key),
            provider.GetRequiredService<IChatRecordingSession>(),
            leaveOpen: true));
    }

    private static ChatClientBuilder UseDescriptor(this ChatClientBuilder builder, ChatClientDescriptor descriptor) =>
        builder.Use(inner => new DescribedChatClient(inner, descriptor));

    private static ChatClientBuilder UseImageGenerationPreservingInputs(this ChatClientBuilder builder, IImageGenerator generator) =>
        builder.Use(inner => new ImageGeneratingChatClient(inner, generator, ImageGeneratingChatClient.DataContentHandling.GeneratedImages));
}
