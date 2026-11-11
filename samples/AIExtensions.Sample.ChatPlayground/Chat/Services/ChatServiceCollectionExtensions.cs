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
    public static IServiceCollection AddChatFeature(this IServiceCollection services, AISettings settings)
    {
        services.AddSingleton<ConnectionStatusService>();
        services.AddSingleton(serviceProvider => new PlaygroundTools(
            serviceProvider.GetRequiredService<ConnectionStatusService>().GetStatus));

        // The engine's conversation thread saves each committed turn, so the chat comes back when the app starts. The
        // shared notes are saved next to it.
        var folder = Path.Combine(FileSystem.AppDataDirectory, "chat-playground");
        services.AddSingleton(_ => new FileConversationThread(Path.Combine(folder, "conversation.json")));
        services.AddSingleton(_ => new NotesFile(Path.Combine(folder, "notes.txt")));
        services.AddSingleton<SettingsPaneViewModel>();
        services.AddSingleton<ModelReadinessViewModel>();
        services.AddSingleton<ChatViewModel>();
        services.AddTransient<Page, ChatPage>();

#if WINDOWS
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100))
            services.AddSingleton<IChatClient>(CreateWindowsChatClient);
#endif

#if IOS || MACCATALYST
        if (OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26))
            services.AddSingleton<IChatClient>(CreateAppleChatClient);
#endif
#if ANDROID
        services.AddSingleton<IChatClient>(CreateGeminiChatClient);
#endif

        if (!string.IsNullOrWhiteSpace(settings.DeploymentName))
            services.AddSingleton<IChatClient>(serviceProvider => CreateAzureChatClient(serviceProvider, settings));

        return services;
    }

#if WINDOWS
    [SupportedOSPlatform("windows10.0.26100.0")]
    private static IChatClient CreateWindowsChatClient(IServiceProvider serviceProvider) =>
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

            // The model cannot call tools, but the chat's set_theme action can ask for approval, which needs this.
            .UseFunctionInvocation(serviceProvider.GetRequiredService<ILoggerFactory>())
            .Build();
#endif

#if IOS || MACCATALYST
    [SupportedOSPlatform("ios26.0")]
    [SupportedOSPlatform("maccatalyst26.0")]
    private static IChatClient CreateAppleChatClient(IServiceProvider serviceProvider) =>
        new AppleIntelligenceChatClient(serviceProvider.GetRequiredService<ILoggerFactory>())
            .AsBuilder()
            .UseDescriptor(new ChatClientDescriptor(
                "apple-intelligence-chat",
                "Apple Intelligence",
                "Model readiness is checked above the composer. Apple manages model preparation in system settings. " +
                    "Apple runs the tools itself, so the theme switch and notes proposals are unavailable.",
                SupportsImageInput: OperatingSystem.IsIOSVersionAtLeast(27) || OperatingSystem.IsMacCatalystVersionAtLeast(27),
                SupportsToolCalling: true,
                GetModelStatusAsync: (_, cancellationToken) => AppleIntelligenceChatClient.GetModelStatusAsync(cancellationToken),
                SupportsUIActions: false))
            .UsePlaygroundTelemetry()
            .UseLogging(serviceProvider.GetRequiredService<ILoggerFactory>())
            .UseFunctionInvocation(serviceProvider.GetRequiredService<ILoggerFactory>())
            .Build();
#endif

#if ANDROID
    [SupportedOSPlatform("android26.0")]
    private static IChatClient CreateGeminiChatClient(IServiceProvider serviceProvider) =>
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

    private static ChatClientBuilder UseDescriptor(this ChatClientBuilder builder, ChatClientDescriptor descriptor) =>
        builder.Use(inner => new DescribedChatClient(inner, descriptor));

    private static ChatClientBuilder UseImageGenerationPreservingInputs(this ChatClientBuilder builder, IImageGenerator generator) =>
        builder.Use(inner => new ImageGeneratingChatClient(inner, generator, ImageGeneratingChatClient.DataContentHandling.GeneratedImages));
}
