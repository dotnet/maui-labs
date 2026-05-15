using System.ClientModel;
using System.Reflection;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.AI;
using Microsoft.Maui.DevFlow.Agent;

namespace AiControlsSample;

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
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Configuration.AddUserSecrets();

#if DEBUG
        builder.AddMauiDevFlowAgent();
        builder.Logging.AddDebug();
#endif

        // Register Azure OpenAI as the default IChatClient
        builder.AddOpenAIServices();

        // Register the agent session factory
        builder.Services.AddAgentSession();

        // Register sample tools
        builder.Services.AddSingleton<SampleTools>();

        // Register pages
        builder.Services.AddTransient<PlaygroundPage>();
        builder.Services.AddTransient<AgenticChatPage>();
        builder.Services.AddTransient<ToolRenderingPage>();

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
            throw new InvalidOperationException(
                """
                AI services are not configured. Set up user secrets (shared across all AI samples):

                  dotnet user-secrets --id ai-attributes-secrets set "AI:Endpoint" "<your-endpoint>"
                  dotnet user-secrets --id ai-attributes-secrets set "AI:ApiKey" "<your-key>"
                  dotnet user-secrets --id ai-attributes-secrets set "AI:DeploymentName" "<your-deployment>"
                """);
        }

        var azureClient = new AzureOpenAIClient(
            new Uri(endpoint),
            new ApiKeyCredential(apiKey));
        var chatClient = azureClient.GetChatClient(deploymentName);

        builder.Services.AddSingleton<IChatClient>(chatClient.AsIChatClient());

        return builder;
    }
}
