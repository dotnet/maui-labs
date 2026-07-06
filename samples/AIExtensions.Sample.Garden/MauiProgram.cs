using System.ClientModel;
using System.Reflection;
using AIExtensions.Sample.Garden.Pages;
using AIExtensions.Sample.Garden.Services;
using AIExtensions.Sample.Garden.ViewModels;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.AI.Chat.Controls;
using Microsoft.Maui.DevFlow.Agent;

namespace AIExtensions.Sample.Garden;

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
                fonts.AddFont("FluentSystemIcons-Filled.ttf", "FluentFilled");
            })
            .ConfigureMauiHandlers(handlers =>
            {
                // Remove native Entry border so our custom Border wrapper is the only visible frame.
#if IOS || MACCATALYST
                Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoBorder", (handler, _) =>
                {
                    handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
                });
#elif ANDROID
                Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoBorder", (handler, _) =>
                {
                    handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
                });
#endif
            });

        builder.Configuration.AddUserSecrets();

#if DEBUG
        builder.AddMauiDevFlowAgent();
#endif

        builder.Services.AddSingleton<IOrderArchive, PreferencesOrderArchive>();
        builder.Services.AddSingleton<CurrentCart>();
        builder.Services.AddSingleton<ReviewStore>();

        builder.AddOpenAIServices();

        builder.Services.AddSingleton<ChatViewModel>();
        builder.Services.AddSingleton<CartViewModel>();
        builder.Services.AddTransient<CatalogViewModel>();
        builder.Services.AddTransient<OrdersViewModel>();
        builder.Services.AddTransient<ProductDetailViewModel>();
        builder.Services.AddTransient<ProductReviewViewModel>();
        builder.Services.AddTransient<OrderDetailViewModel>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<OrdersPage>();
        builder.Services.AddTransient<CatalogPage>();
        builder.Services.AddTransient<CartPage>();
        builder.Services.AddTransient<ProductDetailPage>();
        builder.Services.AddTransient<ProductReviewPage>();
        builder.Services.AddTransient<OrderDetailPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

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
                AI services are not configured. Set up user secrets (shared across all AIExtensions samples):

                  dotnet user-secrets --id ai-attributes-secrets set "AI:Endpoint" "<your-endpoint>"
                  dotnet user-secrets --id ai-attributes-secrets set "AI:ApiKey" "<your-key>"
                  dotnet user-secrets --id ai-attributes-secrets set "AI:DeploymentName" "<your-deployment>"

                Optionally, to demo inline image generation:
                  dotnet user-secrets --id ai-attributes-secrets set "AI:ImageDeploymentName" "<your-image-deployment>"
                """);
        }

        var azureClient = new AzureOpenAIClient(
            new Uri(endpoint),
            new ApiKeyCredential(apiKey));
        var chatClient = azureClient.GetChatClient(deploymentName);

        // Optional image-generation deployment (e.g. gpt-image-1). When configured, UseImageGeneration
        // lets the chat model produce images inline; the image arrives as DataContent and renders as a
        // MediaContentBlock. The ChatViewModel adds the matching HostedImageGenerationTool when set.
        IImageGenerator? imageGenerator = null;
        if (!string.IsNullOrEmpty(imageDeploymentName))
        {
            imageGenerator = azureClient.GetImageClient(imageDeploymentName).AsIImageGenerator();
        }

        // Build the full client here (with the root service provider) so source-generated tools bind
        // their [FromServices] parameters. Image generation must be registered BEFORE function
        // invocation so the hosted image tool is handled beneath the function-invocation loop.
        builder.Services.AddSingleton<IChatClient>(sp =>
        {
            var lf = sp.GetRequiredService<ILoggerFactory>();
            var clientBuilder = chatClient.AsIChatClient()
                .AsBuilder()
                .UseLogging(lf);

            if (imageGenerator is not null)
            {
                clientBuilder.UseImageGeneration(imageGenerator);
            }

            clientBuilder.UseFunctionInvocation(lf);

            return clientBuilder.Build(sp);
        });

        return builder;
    }
}
