using System.ClientModel;
using System.Reflection;
using Azure.AI.OpenAI;
using GenerativeUI.Sample.Garden.ViewModels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.AI.GenerativeUI.OpenApi;
using Microsoft.Maui.DevFlow.Agent;

namespace GenerativeUI.Sample.Garden;

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

        // Where the Garden minimal-API server is listening. Override via config "Api:BaseAddress"
        // (Android emulators reach the host through http://10.0.2.2).
        var baseAddress = builder.Configuration["Api:BaseAddress"] ?? "http://localhost:5225";

        // The generic OpenAPI server-API stack: fetches + reduces the spec, exposes read/write tools.
        builder.Services.AddGenerativeUiOpenApi(options =>
        {
            options.BaseAddress = new Uri(baseAddress);
        });

        builder.AddOpenAIServices();

        // DevFlow agent — enables driving/inspecting the running app via `maui devflow` and MCP tools.
        builder.AddMauiDevFlowAgent();

        builder.Services.AddSingleton<ChatViewModel>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static void AddUserSecrets(this ConfigurationManager manager)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var secretsResource = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("secrets.json"));
        if (secretsResource is null)
            return;

        using var stream = assembly.GetManifestResourceStream(secretsResource);
        if (stream is not null)
            manager.AddJsonStream(stream);
    }

    private static MauiAppBuilder AddOpenAIServices(this MauiAppBuilder builder)
    {
        var ai = builder.Configuration.GetSection("AI");
        var endpoint = ai["Endpoint"];
        var apiKey = ai["ApiKey"];
        var deploymentName = ai["DeploymentName"];

        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(deploymentName))
        {
            throw new InvalidOperationException(
                """
                AI services are not configured. Set up user secrets (shared with the AIExtensions samples):

                  dotnet user-secrets --id ai-attributes-secrets set "AI:Endpoint" "<your-endpoint>"
                  dotnet user-secrets --id ai-attributes-secrets set "AI:ApiKey" "<your-key>"
                  dotnet user-secrets --id ai-attributes-secrets set "AI:DeploymentName" "<your-deployment>"
                """);
        }

        var azureClient = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey));
        var chatClient = azureClient.GetChatClient(deploymentName);
        builder.Services.AddSingleton<IChatClient>(chatClient.AsIChatClient());

        return builder;
    }
}
