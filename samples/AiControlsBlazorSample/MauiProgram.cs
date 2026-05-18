using System.ClientModel;
using System.Reflection;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiControlsBlazorSample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        builder.Configuration.AddUserSecrets();

        // Register Azure OpenAI as IChatClient with function invocation middleware
        builder.AddOpenAIServices();

        return builder.Build();
    }

    private static void AddUserSecrets(this ConfigurationManager manager)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();
        var secretsResource = resourceNames.FirstOrDefault(n => n.EndsWith("secrets.json"));
        if (secretsResource is not null)
        {
            using var stream = assembly.GetManifestResourceStream(secretsResource);
            if (stream is not null)
                manager.AddJsonStream(stream);
        }
    }

    private static MauiAppBuilder AddOpenAIServices(this MauiAppBuilder builder)
    {
        var aiSection = builder.Configuration.GetSection("AI");
        var apiKey = aiSection["ApiKey"];
        var endpoint = aiSection["Endpoint"];
        var deploymentName = aiSection["DeploymentName"];

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(deploymentName))
        {
            // Register a no-op client so the app compiles and launches without secrets
            builder.Services.AddSingleton<IChatClient>(new NoOpChatClient());
            return builder;
        }

        var azureClient = new AzureOpenAIClient(
            new Uri(endpoint),
            new ApiKeyCredential(apiKey));
        var chatClient = azureClient.GetChatClient(deploymentName);

        builder.Services.AddSingleton<IChatClient>(sp =>
        {
            var lf = sp.GetRequiredService<ILoggerFactory>();
            return chatClient.AsIChatClient()
                .AsBuilder()
                .UseLogging(lf)
                .UseFunctionInvocation()
                .Build(sp);
        });

        return builder;
    }
}

/// <summary>
/// Fallback chat client used when AI services are not configured.
/// </summary>
internal sealed class NoOpChatClient : IChatClient
{
    public ChatClientMetadata Metadata { get; } = new("NoOp");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant,
            "AI services are not configured. Please set up user secrets."));
        return Task.FromResult(response);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return GetStreamingResponseCore(cancellationToken);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseCore(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield return new ChatResponseUpdate(ChatRole.Assistant,
            "AI services are not configured. Please set up user secrets.");
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
