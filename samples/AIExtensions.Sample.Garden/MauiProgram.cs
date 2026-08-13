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
using Microsoft.Maui.CopilotSdk;
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

        builder.AddAIServices();

        builder.Services.AddSingleton<ChatViewModel>();
        builder.Services.AddSingleton<CartViewModel>();
        builder.Services.AddTransient<CatalogViewModel>();
        builder.Services.AddTransient<OrdersViewModel>();
        builder.Services.AddTransient<ProductDetailViewModel>();
        builder.Services.AddTransient<ProductReviewViewModel>();
        builder.Services.AddTransient<OrderDetailViewModel>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<TeamChatViewModel>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<TeamChatPage>();
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

    private static MauiAppBuilder AddAIServices(this MauiAppBuilder builder)
    {
        var aiSection = builder.Configuration.GetSection("AI");
        var provider = aiSection["Provider"] ?? GetDefaultProvider();
        var apiKey = aiSection["ApiKey"];
        var endpoint = aiSection["Endpoint"];
        var deploymentName = aiSection["DeploymentName"];
        var imageDeploymentName = aiSection["ImageDeploymentName"];
        AzureOpenAIClient? azureClient = null;
        if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(endpoint))
        {
            azureClient = new AzureOpenAIClient(
                new Uri(endpoint),
                new ApiKeyCredential(apiKey));
        }

        IImageGenerator? imageGenerator = null;
        if (azureClient is not null && !string.IsNullOrEmpty(imageDeploymentName))
        {
            imageGenerator = azureClient
                .GetImageClient(imageDeploymentName)
                .AsIImageGenerator();
            builder.Services.AddSingleton(imageGenerator);
        }

        builder.Services.AddSingleton<IChatClient>(sp =>
        {
            var lf = sp.GetRequiredService<ILoggerFactory>();
            IChatClient baseClient = provider.Equals(
                "Copilot",
                StringComparison.OrdinalIgnoreCase)
                ? new CopilotSdkChatClient(new CopilotSdkConfiguration
                {
                    Model = aiSection["Model"],
                    GitHubToken = aiSection["GitHubToken"]
                        ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN"),
                    UseLoggedInUser = string.IsNullOrEmpty(
                        aiSection["GitHubToken"]
                            ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")),
                    CliPath = ResolveCopilotCliPath(
                        aiSection["CopilotCliPath"]),
                    // Leave WorkingDirectory unset. Pointing the installed CLI at the Mac Catalyst
                    // app-data container can stall a session after setup while it scans that location.
                })
                : CreateAzureChatClient(
                    azureClient,
                    deploymentName);

            var clientBuilder = baseClient
                .AsBuilder()
                .UseLogging(lf);
            if (imageGenerator is not null)
                clientBuilder.UseImageGeneration(imageGenerator);
            clientBuilder.UseFunctionInvocation(lf);

            return clientBuilder.Build(sp);
        });

        return builder;
    }

    private static IChatClient CreateAzureChatClient(
        AzureOpenAIClient? client,
        string? deploymentName)
    {
        if (client is null || string.IsNullOrEmpty(deploymentName))
        {
            throw new InvalidOperationException(
                """
                Azure OpenAI is not configured. Set AI:Provider to Copilot on a desktop,
                or configure AI:Endpoint, AI:ApiKey, and AI:DeploymentName.
                """);
        }

        return client.GetChatClient(deploymentName).AsIChatClient();
    }

    private static string GetDefaultProvider()
    {
#if MACCATALYST || WINDOWS
        return "Copilot";
#else
        return "AzureOpenAI";
#endif
    }

    private static string? ResolveCopilotCliPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return configuredPath;

        var names = OperatingSystem.IsWindows()
            ? new[] { "copilot.exe", "copilot.cmd" }
            : new[] { "copilot" };
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            foreach (var candidate in new[]
            {
                "/opt/homebrew/bin/copilot",
                "/usr/local/bin/copilot",
            })
            {
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }
}
