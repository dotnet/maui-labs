using System.Reflection;
using System.ClientModel;
using AIExtensions.Sample.ChatPlayground.Features.Chat.Recording;
using AIExtensions.Sample.ChatPlayground.Features.Chat.Services;
using AIExtensions.Sample.ChatPlayground.Features.Chat.ViewModels;
using AIExtensions.Sample.ChatPlayground.Features.Chat.Views;
using AIExtensions.Sample.ChatPlayground.Features.Embeddings.Services;
using AIExtensions.Sample.ChatPlayground.Features.Embeddings.ViewModels;
using AIExtensions.Sample.ChatPlayground.Features.Embeddings.Views;
using AIExtensions.Sample.ChatPlayground.Features.Images.Services;
using AIExtensions.Sample.ChatPlayground.Features.Images.ViewModels;
using AIExtensions.Sample.ChatPlayground.Features.Images.Views;
using AIExtensions.Sample.ChatPlayground.Shared.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.DevFlow.Agent;
using OpenAI;

#if WINDOWS
using System.Runtime.Versioning;
#endif
#if IOS || MACCATALYST || WINDOWS
using Microsoft.Maui.Essentials.AI;
#endif
#if WINDOWS
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
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
        AddImageGenerators(builder.Services, builder.Configuration);
        AddChatClients(builder.Services, builder.Configuration);

#if DEBUG
        builder.AddMauiDevFlowAgent();
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<ImageInputService>();
        builder.Services.AddSingleton<PlaygroundTools>();
        builder.Services.AddSingleton(serviceProvider => new ChatSessionService(
            serviceProvider.GetRequiredService<ILogger<ChatSessionService>>(),
            FileSystem.AppDataDirectory,
            FileSystem.CacheDirectory));
        builder.Services.AddSingleton<IChatRecordingSession>(provider => provider.GetRequiredService<ChatSessionService>());
        AddEmbeddingGenerators(builder.Services, builder.Configuration);
        builder.Services.AddSingleton(_ => new DocumentStore(FileSystem.AppDataDirectory));
        builder.Services.AddSingleton(serviceProvider => new DocumentSearchService(
            serviceProvider.GetRequiredService<DocumentStore>(),
            serviceProvider.GetRequiredService<ILogger<DocumentSearchService>>(),
            FileSystem.AppDataDirectory));
        builder.Services.AddSingleton<ImageGenerationService>();
        builder.Services.AddSingleton<EmbeddingSettingsViewModel>();
        builder.Services.AddSingleton<ImageSettingsViewModel>();
        builder.Services.AddSingleton<SettingsPaneViewModel>();
        builder.Services.AddSingleton<ChatAreaViewModel>();
        builder.Services.AddSingleton<EmbeddingPlaygroundViewModel>();
        builder.Services.AddSingleton<ImagePlaygroundViewModel>();
        builder.Services.AddSingleton<ChatViewModel>();
        builder.Services.AddTransient<ChatPage>();
        builder.Services.AddTransient<EmbeddingPage>();
        builder.Services.AddTransient<ImagePage>();
        builder.Services.AddTransient<PlaygroundTabs>();

        return builder.Build();
    }

#pragma warning disable MEAI001 // Azure chat uses an optional experimental image generator.
    private static void AddChatClients(IServiceCollection services, IConfiguration configuration)
    {
#if WINDOWS
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100))
            services.AddSingleton<IChatClient>(CreateWindowsChatClient);
#endif
#if IOS || MACCATALYST
        if (OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26))
            services.AddSingleton<IChatClient>(serviceProvider => CreateLocalChatClient(
                serviceProvider.GetRequiredService<ILoggerFactory>(),
                serviceProvider.GetRequiredService<IChatRecordingSession>()));
#endif
        if (!string.IsNullOrWhiteSpace(configuration["AI:DeploymentName"]))
        {
            services.AddSingleton<IChatClient>(serviceProvider => CreateCloudChatClient(
                configuration["AI:Endpoint"],
                configuration["AI:ApiKey"],
                configuration["AI:DeploymentName"],
                serviceProvider.GetServices<IImageGenerator>()
                    .FirstOrDefault(generator =>
                        generator.GetService<ImageGeneratorDescriptor>()?.Id ==
                        $"azure/{configuration["AI:ImageDeploymentName"]}"),
                serviceProvider.GetRequiredService<ILoggerFactory>(),
                serviceProvider.GetRequiredService<IChatRecordingSession>()));
        }

        services.AddSingleton<IChatClient>(serviceProvider =>
            new ReplayChatClient(serviceProvider.GetRequiredService<IChatRecordingSession>())
                .AsBuilder()
                .UseDescriptor(new ChatClientDescriptor(
                    "Replay",
                    "Play the saved chat without contacting a model. Switch to a live client after playback to continue.",
                    SupportsImageInput: false,
                    IsReplay: true))
                .Build());
    }
#pragma warning restore MEAI001

#pragma warning disable MEAI001, OPENAI001 // Responses and image adapters are experimental in the installed SDK.
    private static IChatClient CreateCloudChatClient(
        string? endpointValue,
        string? apiKey,
        string? deploymentName,
        IImageGenerator? imageGenerator,
        ILoggerFactory loggerFactory,
        IChatRecordingSession recording)
    {
        if (string.IsNullOrWhiteSpace(endpointValue) ||
            string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(deploymentName))
        {
            throw new InvalidOperationException(
                "Configure AI:Endpoint, AI:ApiKey, and AI:DeploymentName in the shared local user secrets.");
        }

        var endpoint = RequireOpenAIEndpoint(endpointValue);
        var openAIClient = CreateOpenAIClient(endpoint, apiKey);
        var imageDeployment = imageGenerator?.GetService<ImageGeneratorDescriptor>()?.DisplayName;
        // Recording wraps the tool and image middleware so the saved response is the one shown in chat.
        var builder = openAIClient.GetResponsesClient().AsIChatClient(deploymentName)
            .AsBuilder()
            .UseRecording(recording)
            .UseDescriptor(new ChatClientDescriptor(
                "Azure OpenAI",
                $"Azure OpenAI deployment '{deploymentName}' is ready." +
                    (imageDeployment is null ? string.Empty : $" Image generation uses {imageDeployment}."),
                SupportsImageInput: true,
                SupportsReasoningSummary: true,
                SupportsImageGeneration: imageGenerator is not null))
            .UseLogging(loggerFactory);
        if (imageGenerator is not null)
            builder.UseImageGenerationPreservingInputs(imageGenerator);
        var client = builder.UseFunctionInvocation().Build();

        return client;
    }

    private static OpenAIClient CreateOpenAIClient(Uri endpoint, string apiKey) =>
        new(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = endpoint });
#pragma warning restore MEAI001, OPENAI001

    private static void AddImageGenerators(IServiceCollection services, IConfiguration configuration)
    {
        var deployment = configuration["AI:ImageDeploymentName"];
        if (string.IsNullOrWhiteSpace(deployment))
            return;

        var apiKey = configuration["AI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("AI:ImageDeploymentName requires AI:ApiKey and AI:Endpoint.");
        var endpoint = RequireOpenAIEndpoint(configuration["AI:Endpoint"]);
#pragma warning disable MEAI001 // The installed OpenAI image adapter is experimental.
        services.AddSingleton<IImageGenerator>(_ => new DescribedImageGenerator(
            () => CreateOpenAIClient(endpoint, apiKey).GetImageClient(deployment).AsIImageGenerator(),
            new ImageGeneratorDescriptor(
                $"azure/{deployment}", "Azure OpenAI",
                "Generates or edits images with the configured Azure deployment. Prompts and original images leave this device; requests may incur charges.",
                SupportsEdits: true)));
#pragma warning restore MEAI001
    }

    private static void AddEmbeddingGenerators(IServiceCollection services, IConfiguration configuration)
    {
#if IOS || MACCATALYST
        if (OperatingSystem.IsIOSVersionAtLeast(13) ||
            OperatingSystem.IsMacCatalystVersionAtLeast(13, 1))
        {
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_ =>
                new DescribedEmbeddingGenerator(
                    () => new NLEmbeddingGenerator(),
                    new EmbeddingGeneratorDescriptor(
                    "apple-natural-language", "Apple NaturalLanguage",
                    "On-device English sentence embeddings from Apple's NaturalLanguage framework.",
                    $"apple/natural-language/english/{typeof(NLEmbeddingGenerator).Assembly.GetName().Version}/{Environment.OSVersion.Version}")));
        }
#endif
        var embeddingDeployment = configuration["AI:EmbeddingDeploymentName"];
        if (!string.IsNullOrWhiteSpace(embeddingDeployment))
        {
            var apiKey = configuration["AI:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException(
                    "AI:EmbeddingDeploymentName requires AI:ApiKey and AI:Endpoint.");

            var endpoint = RequireOpenAIEndpoint(configuration["AI:Endpoint"]);
            var revision = configuration["AI:EmbeddingIndexRevision"] ?? "initial";
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_ =>
                new DescribedEmbeddingGenerator(
                    () => CreateOpenAIClient(endpoint, apiKey)
                        .GetEmbeddingClient(embeddingDeployment).AsIEmbeddingGenerator(),
                    new EmbeddingGeneratorDescriptor(
                    $"azure/{embeddingDeployment}", "Azure OpenAI",
                    $"Embeddings use deployment '{embeddingDeployment}'. Imported document text and search queries are sent to Azure.",
                    $"azure/{endpoint.AbsoluteUri}/{embeddingDeployment}/{revision}")));
        }
    }

    private static Uri RequireOpenAIEndpoint(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.AbsolutePath.TrimEnd('/').EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "AI:Endpoint must be an absolute OpenAI-compatible endpoint ending in /openai/v1/.");
        return endpoint;
    }
#if WINDOWS
    [SupportedOSPlatform("windows10.0.26100.0")]
    private static IChatClient CreateWindowsChatClient(IServiceProvider services) =>
        new PhiSilicaChatClient()
            .AsBuilder()
            .UseRecording(services.GetRequiredService<IChatRecordingSession>())
            .UseDescriptor(new ChatClientDescriptor(
                "Phi Silica",
                "Windows Copilot Runtime is supported on this OS. The first request checks whether the on-device model is ready.",
                SupportsImageInput: true,
                SupportsImageGeneration: true))
            .UseLogging(services.GetRequiredService<ILoggerFactory>())
            .UseImageGenerationPreservingInputs(new PhiSilicaImageGenerator())
            .UseFunctionInvocation()
            .Use(inner => new PhiSilicaToolCallingClient(inner))
            .Build();
#endif

#if IOS || MACCATALYST
    private static IChatClient CreateLocalChatClient(ILoggerFactory loggerFactory, IChatRecordingSession recording)
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
