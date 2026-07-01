using System.ClientModel;
using System.Reflection;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.AI.Chat.Controls;
using Microsoft.Maui.DevFlow.Agent;

namespace AiControlsSample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseChatControls()
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

        // Register Azure OpenAI as IChatClient with function invocation middleware
        builder.AddOpenAIServices();

        // Register pages
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
        var imageDeploymentName = aiSection["ImageDeploymentName"];

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(deploymentName))
        {
            throw new InvalidOperationException(
                """
                AI services are not configured. Set up user secrets (shared across all AI samples):

                  dotnet user-secrets --id ai-attributes-secrets set "AI:Endpoint" "<your-endpoint>"
                  dotnet user-secrets --id ai-attributes-secrets set "AI:ApiKey" "<your-key>"
                  dotnet user-secrets --id ai-attributes-secrets set "AI:DeploymentName" "<your-deployment>"
                  dotnet user-secrets --id ai-attributes-secrets set "AI:ImageDeploymentName" "<your-image-deployment>"
                """);
        }

        var azureClient = new AzureOpenAIClient(
            new Uri(endpoint),
            new ApiKeyCredential(apiKey));
        var chatClient = azureClient.GetChatClient(deploymentName);

        // Optional image-generation deployment (e.g. gpt-image-1). When configured,
        // UseImageGeneration lets the chat model produce images inline in the same
        // conversation; the image arrives as DataContent and renders as a MediaContentBlock.
        IImageGenerator? imageGenerator = null;
        if (!string.IsNullOrEmpty(imageDeploymentName))
        {
            imageGenerator = azureClient.GetImageClient(imageDeploymentName).AsIImageGenerator();
        }

        builder.Services.AddSingleton<IChatClient>(sp =>
        {
            var lf = sp.GetRequiredService<ILoggerFactory>();
            var clientBuilder = chatClient.AsIChatClient()
                .AsBuilder()
                .UseLogging(lf);

            // Image generation must be registered BEFORE function invocation so the
            // hosted image tool is handled beneath the function-invocation loop.
            if (imageGenerator is not null)
            {
                clientBuilder.UseImageGeneration(imageGenerator);
            }

            clientBuilder.UseFunctionInvocation();

            return clientBuilder.Build(sp);
        });

        return builder;
    }
}
