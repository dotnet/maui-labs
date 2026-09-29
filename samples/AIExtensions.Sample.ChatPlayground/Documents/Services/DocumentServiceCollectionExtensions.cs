using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace AIExtensions.Sample.ChatPlayground;

internal static class DocumentServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentFeature(
        this IServiceCollection services,
        AISettings settings)
    {
        services.AddSingleton<IDocumentExtractionProvider, AppleDocumentExtractionProvider>();
        if (!string.IsNullOrWhiteSpace(settings.DocumentDeploymentName))
        {
            services.AddSingleton<IDocumentExtractionProvider>(_ =>
                new FoundryMistralOcrProvider(
                    new HttpClient(),
                    settings.FoundryEndpoint!,
                    settings.FoundryApiKey!,
                    settings.DocumentDeploymentName));
        }
        if (!string.IsNullOrWhiteSpace(settings.DeploymentName))
        {
            services.AddSingleton<IDocumentExtractionProvider>(_ =>
            {
                var openAIClient = new OpenAIClient(
                    new ApiKeyCredential(settings.ApiKey!),
                    new OpenAIClientOptions { Endpoint = settings.Endpoint! });
                return new FoundryModelDocumentExtractionProvider(
                    openAIClient.GetResponsesClient()
                        .AsIChatClient(settings.DeploymentName),
                    settings.DeploymentName);
            });
        }
        services.AddSingleton<DocumentExtractionRunner>();
        services.AddSingleton<DocumentInputService>();
        services.AddSingleton<DocumentSettingsViewModel>();
        services.AddSingleton<DocumentPlaygroundViewModel>();
        services.AddTransient<Page, DocumentPage>();
        return services;
    }
}
