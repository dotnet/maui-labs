using CommunityToolkit.DocumentExtraction.Mistral;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Logging;
#if IOS || MACCATALYST
using System.Runtime.Versioning;
using Microsoft.Maui.Essentials.AI;
#endif

namespace AIExtensions.Sample.ChatPlayground;

internal static class DocumentServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentFeature(this IServiceCollection services, AISettings settings)
    {
        services.AddSingleton<DocumentExtractionService>();
        services.AddSingleton<DocumentSettingsViewModel>();
        services.AddSingleton<DocumentPlaygroundViewModel>();
        services.AddTransient<Page, DocumentPage>();

#if IOS || MACCATALYST
        if (OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26))
            services.AddSingleton<DocumentExtractionClient>(CreateAppleRecognizeDocumentsClient);
#endif

        if (!string.IsNullOrWhiteSpace(settings.DocumentDeploymentName))
        {
            services.AddSingleton<DocumentExtractionClient>(provider =>
                Describe(new MistralDocumentExtractionClient(
                    new MistralDocumentExtractionClientOptions
                    {
                        OcrEndpoint = new Uri(settings.GetFoundryDocumentEndpoint(), "providers/mistral/azure/ocr"),
                        DefaultModel = settings.DocumentDeploymentName!,
                        IncludeImages = true,
                    }, settings.ApiKey!),
                    new DocumentExtractionClientDescriptor(
                        "foundry-mistral-document", "Foundry Mistral Document AI",
                        "Uploads the document using the Toolkit Mistral adapter. Tables retain native Markdown/HTML without an addressed cell grid. Native inspection retains detached parsed JSON."), provider));
        }

        return services;
    }

#if IOS || MACCATALYST
    [SupportedOSPlatform("ios26.0")]
    [SupportedOSPlatform("maccatalyst26.0")]
    private static DocumentExtractionClient CreateAppleRecognizeDocumentsClient(IServiceProvider provider) =>
        Describe(new AppleVisionRecognizeDocumentsClient(),
            new DocumentExtractionClientDescriptor(
                "apple-vision-document", "Apple Vision RecognizeDocuments",
                "RecognizeDocumentsRequest on-device. Preserves native geometry and table spans. Native inspection is a bounded bridge snapshot, not an Apple observation object."), provider);
#endif

    private static DocumentExtractionClient Describe(
        DocumentExtractionClient client, DocumentExtractionClientDescriptor descriptor, IServiceProvider provider) =>
        new DocumentExtractionClientBuilder(client)
            .Use(inner => new DescribedDocumentExtractionClient(inner, descriptor))
            .UseLogging(provider.GetRequiredService<ILoggerFactory>())
            .Build();
}
