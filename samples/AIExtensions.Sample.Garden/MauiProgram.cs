using System.ClientModel;
using System.Reflection;
using AIExtensions.Sample.Garden.Pages;
using AIExtensions.Sample.Garden.Services;
using AIExtensions.Sample.Garden.ViewModels;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.AI.Indexer;
using Microsoft.Maui.AI.Navigation;
using Microsoft.Maui.AI.Wayfinding;
using Microsoft.Maui.DevFlow.Agent;

namespace AIExtensions.Sample.Garden;

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
        builder.Services.AddMauiWayfinding(
            AIExtensions_Sample_GardenIndexedPageCatalog.Default,
            options =>
        {
            options.EnableVision = true;
            options.AdditionalInstructions =
                """
                Before opening product detail or review, identify the product with search_products
                or get_product and pass its sku. Before opening order detail, identify the order
                with list_past_orders or find_order and pass its orderId.
                """;
        });

        builder.AddOpenAIServices();

        builder.Services.AddSingleton<ChatViewModel>();
        builder.Services.AddSingleton<CartViewModel>();
        builder.Services.AddTransient<CatalogViewModel>();
        builder.Services.AddTransient<OrdersViewModel>();
        builder.Services.AddTransient<ProductDetailViewModel>();
        builder.Services.AddTransient<ProductReviewViewModel>();
        builder.Services.AddTransient<OrderDetailViewModel>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddTransient<AppShell>();
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

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(deploymentName))
        {
            throw new InvalidOperationException(
                """
                AI services are not configured. Set up user secrets (shared across all AIExtensions samples):

                  dotnet user-secrets --id ai-attributes-secrets set "AI:Endpoint" "<your-endpoint>"
                  dotnet user-secrets --id ai-attributes-secrets set "AI:ApiKey" "<your-key>"
                  dotnet user-secrets --id ai-attributes-secrets set "AI:DeploymentName" "<your-deployment>"
                """);
        }

        var azureClient = new AzureOpenAIClient(
            new Uri(endpoint),
            new ApiKeyCredential(apiKey));
        var chatClient = azureClient.GetChatClient(deploymentName);
        var aiChatClient = chatClient.AsIChatClient();

        builder.Services.AddSingleton<IChatClient>(aiChatClient);
        builder.Services.AddKeyedSingleton<IChatClient>(
            MauiWayfindingOptions.VisionChatClientServiceKey,
            aiChatClient);

        return builder;
    }
}
