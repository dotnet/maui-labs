using System.ClientModel;
using System.Reflection;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.AI.Chat;
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

        // Register sample tools as IEnumerable<AITool> for ChatSession
        builder.Services.AddSingleton<SampleTools>();
        builder.Services.AddSingleton<IEnumerable<AITool>>(sp => sp.GetRequiredService<SampleTools>().GetTools());

        // Register the headless chat engine (transient = new session per page)
        builder.Services.AddChatSession(ServiceLifetime.Transient);

        // Register pages
        builder.Services.AddTransient<PlaygroundPage>();
        builder.Services.AddTransient<AgenticChatPage>();
        builder.Services.AddTransient<ToolRenderingPage>();
        builder.Services.AddTransient<HumanInTheLoopPage>();
        builder.Services.AddTransient<SharedStatePage>();
        builder.Services.AddTransient<AgenticGenerativeUIPage>();
        builder.Services.AddTransient<PredictiveStatePage>();
        builder.Services.AddTransient<HaikuPage>();

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
