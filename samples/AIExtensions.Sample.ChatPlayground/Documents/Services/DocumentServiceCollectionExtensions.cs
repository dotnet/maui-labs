using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DocumentExtraction;
using OpenAI;

#if IOS || MACCATALYST
using System.Runtime.Versioning;
using Microsoft.Maui.Essentials.AI;
#endif

namespace AIExtensions.Sample.ChatPlayground;

internal static class DocumentServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentFeature(
        this IServiceCollection services,
        AISettings settings)
    {
        services.AddSingleton<DocumentInputService>();
        services.AddSingleton<DocumentSettingsViewModel>();
        services.AddSingleton<DocumentPlaygroundViewModel>();
        services.AddTransient<Page, DocumentPage>();

#if IOS || MACCATALYST
        if (OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26))
            services.AddSingleton<IDocumentExtractionClient>(_ => CreateAppleDocumentClient());
#endif

        if (!string.IsNullOrWhiteSpace(settings.DocumentDeploymentName))
            services.AddSingleton<IDocumentExtractionClient>(_ => CreateMistralClient(settings));

        if (!string.IsNullOrWhiteSpace(settings.DeploymentName))
            services.AddSingleton<IDocumentExtractionClient>(_ => CreateVisionModelClient(settings));

        return services;
    }

#if IOS || MACCATALYST
    [SupportedOSPlatform("ios26.0")]
    [SupportedOSPlatform("maccatalyst26.0")]
    private static IDocumentExtractionClient CreateAppleDocumentClient() =>
        new AppleVisionDocumentExtractionClient()
            .AsBuilder()
            .UseDescriptor(new DocumentExtractionClientDescriptor(
                "apple-vision",
                "Apple Vision",
                "On-device RecognizeDocumentsRequest for images and PDFs. PDFKit renders PDF pages internally; no document data leaves the device."))
            .Build();
#endif

    private static IDocumentExtractionClient CreateMistralClient(AISettings settings) =>
        new FoundryMistralDocumentExtractionClient(
                CreateFoundryDocumentHttpClient(settings),
                settings.DocumentDeploymentName!,
                disposeHttpClient: true)
            .AsBuilder()
            .UseDescriptor(new DocumentExtractionClientDescriptor(
                "foundry-mistral-document",
                $"Mistral document ({settings.DocumentDeploymentName})",
                $"Mistral image-to-text deployment '{settings.DocumentDeploymentName}'. " +
                "Sends documents to Azure and returns Markdown, tables, figures, confidence, and pixel geometry."))
            .Build();

    private static HttpClient CreateFoundryDocumentHttpClient(AISettings settings)
    {
        var client = new HttpClient
        {
            BaseAddress = settings.GetFoundryResourceEndpoint(),
            Timeout = TimeSpan.FromMinutes(5),
        };
        client.DefaultRequestHeaders.Add("api-key", settings.ApiKey!);
        return client;
    }

    private static IDocumentExtractionClient CreateVisionModelClient(AISettings settings)
    {
        var openAIClient = new OpenAIClient(
            new ApiKeyCredential(settings.ApiKey!),
            new OpenAIClientOptions { Endpoint = settings.Endpoint! });

        return new FoundryModelDocumentExtractionClient(
                openAIClient.GetResponsesClient().AsIChatClient(settings.DeploymentName!),
                settings.DeploymentName!,
                disposeChatClient: true)
            .AsBuilder()
            .UseDescriptor(new DocumentExtractionClientDescriptor(
                "foundry-vision-chat",
                $"Vision chat model ({settings.DeploymentName})",
                $"General semantic extraction with the Azure vision-capable chat deployment '{settings.DeploymentName}'. " +
                "Sends documents to Azure; geometry and confidence are unavailable."))
            .Build();
    }

    private static DocumentExtractionClientBuilder UseDescriptor(
        this DocumentExtractionClientBuilder builder,
        DocumentExtractionClientDescriptor descriptor) =>
        builder.Use(inner => new DescribedDocumentExtractionClient(inner, descriptor));
}
