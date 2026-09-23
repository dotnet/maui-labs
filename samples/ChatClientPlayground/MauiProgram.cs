using System.Reflection;
using System.ClientModel;
using Azure.AI.OpenAI;
using ChatClientPlayground.Features.Recording;
using ChatClientPlayground.Services;
using ChatClientPlayground.ViewModels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.DevFlow.Agent;
using OpenAI;
using OpenAI.Chat;

#if IOS || MACCATALYST
using Microsoft.Maui.Essentials.AI;
#endif

namespace ChatClientPlayground;

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
        AddChatClientSlots(builder.Services, builder.Configuration);

#if DEBUG
        builder.AddMauiDevFlowAgent();
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<ChatClientService>();
        builder.Services.AddSingleton<ImageInputService>();
        builder.Services.AddSingleton<PlaygroundTools>();
        builder.Services.AddSingleton<ChatRecordingFeature>();
        builder.Services.AddSingleton<RecordingViewModel>();
        builder.Services.AddSingleton<SettingsPaneViewModel>();
        builder.Services.AddSingleton<ChatAreaViewModel>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddSingleton<Func<MainPage>>(services =>
            () => services.GetRequiredService<MainPage>());

        return builder.Build();
    }

    private static void AddChatClientSlots(IServiceCollection services, IConfiguration configuration)
    {
        AddLocalChatClientSlot(services);
        AddCloudChatClientSlot(services, configuration);
    }

    private static void AddLocalChatClientSlot(IServiceCollection services)
    {
        services.AddKeyedSingleton<IChatClient>(ChatClientKeys.Local, static (serviceProvider, _) =>
            CreateLocalChatClient(serviceProvider.GetRequiredService<ILoggerFactory>()));
    }

    private static void AddCloudChatClientSlot(IServiceCollection services, IConfiguration configuration)
    {
        services.AddKeyedSingleton<IChatClient>(ChatClientKeys.Cloud, (serviceProvider, _) =>
            CreateCloudChatClient(
                configuration["AI:Endpoint"],
                configuration["AI:ApiKey"],
                configuration["AI:DeploymentName"],
                serviceProvider.GetRequiredService<ILoggerFactory>()));
    }

    private static IChatClient CreateCloudChatClient(
        string? endpointValue,
        string? apiKey,
        string? deploymentName,
        ILoggerFactory loggerFactory)
    {
        if (string.IsNullOrWhiteSpace(endpointValue) ||
            string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(deploymentName))
        {
            throw new InvalidOperationException(
                "Configure AI:Endpoint, AI:ApiKey, and AI:DeploymentName in the shared local user secrets.");
        }

        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint))
            throw new InvalidOperationException("AI:Endpoint must be a valid absolute URI.");

        IChatClient client = IsOpenAICompatibleEndpoint(endpoint)
            ? new ChatClient(
                model: deploymentName,
                credential: new ApiKeyCredential(apiKey),
                options: new OpenAIClientOptions { Endpoint = endpoint }).AsIChatClient()
            : new AzureOpenAIClient(endpoint, new ApiKeyCredential(apiKey))
                .GetChatClient(deploymentName)
                .AsIChatClient();

        client = client
            .AsBuilder()
            .UseLogging(loggerFactory)
            .UseFunctionInvocation()
            .Build();

        return new DescribedChatClient(
            client,
            new ChatClientDescriptor(
                "Azure OpenAI",
                $"Azure OpenAI deployment '{deploymentName}' is ready.",
                SupportsImageInput: true));
    }

    private static IChatClient CreateLocalChatClient(ILoggerFactory loggerFactory)
    {
#if IOS || MACCATALYST
#pragma warning disable CA1416
        if (OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26))
        {
            var client = new AppleIntelligenceChatClient(loggerFactory)
                .AsBuilder()
                .UseLogging(loggerFactory)
                .UseFunctionInvocation()
                .Build();
            return new DescribedChatClient(
                client,
                new ChatClientDescriptor(
                    "Apple Intelligence",
                    "Apple Intelligence is supported on this OS. The first request confirms that the local model is enabled and available.",
                    SupportsImageInput: false));
        }
#pragma warning restore CA1416
#endif
        throw new InvalidOperationException(
            "No local provider is configured on this platform or OS. Replace the local keyed factory with a real local IChatClient.");
    }

    private static bool IsOpenAICompatibleEndpoint(Uri endpoint) =>
        endpoint.AbsolutePath.TrimEnd('/').EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase);

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
