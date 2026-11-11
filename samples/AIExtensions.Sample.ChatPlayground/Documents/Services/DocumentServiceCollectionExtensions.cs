using CommunityToolkit.DocumentExtraction.Mistral;
using Microsoft.Extensions.DataIngestion;
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
        services.AddSingleton<DocumentReadingService>();
        services.AddSingleton<DocumentExtractionService>();
        services.AddSingleton<DocumentSettingsViewModel>();
        services.AddSingleton<DocumentPlaygroundViewModel>();
        services.AddTransient<Page, DocumentPage>();

#if IOS || MACCATALYST
        if (OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26))
        {
            services.AddSingleton<IngestionDocumentReader>(CreateAppleRecognizeDocumentsReader);
            services.AddSingleton<DocumentExtractionClient>(CreateAppleRecognizeDocumentsClient);
        }
#endif

        if (!string.IsNullOrWhiteSpace(settings.DocumentDeploymentName))
        {
            services.AddKeyedSingleton<DocumentExtractionClient>("foundry-document", (_, _) =>
                new MistralDocumentExtractionClient(
                    new MistralDocumentExtractionClientOptions
                    {
                        OcrEndpoint = new Uri(settings.GetFoundryDocumentEndpoint(), "providers/mistral/azure/ocr"),
                        DefaultModel = settings.DocumentDeploymentName!,
                        IncludeImages = true,
                    }, settings.ApiKey!));
            services.AddSingleton<IngestionDocumentReader>(provider => new DescribedDocumentReader(
                new ExtractedDocumentReader(provider.GetRequiredKeyedService<DocumentExtractionClient>("foundry-document")),
                new DocumentReaderDescriptor("foundry-mistral-document", "Foundry Mistral Document AI",
                    "Uploads the entire document to the configured Foundry document model; may incur charges.", IsCloud: true)));
            services.AddSingleton<DocumentExtractionClient>(provider =>
                Describe(provider.GetRequiredKeyedService<DocumentExtractionClient>("foundry-document"),
                    new DocumentExtractionClientDescriptor(
                        "foundry-mistral-document", "Foundry Mistral Document AI",
                        "Uploads the document using the Toolkit Mistral adapter; charges may apply. Tables retain native Markdown/HTML without an addressed cell grid. Native inspection retains detached parsed JSON.", IsCloud: true), provider));
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

    [SupportedOSPlatform("ios26.0")]
    [SupportedOSPlatform("maccatalyst26.0")]
    private static IngestionDocumentReader CreateAppleRecognizeDocumentsReader(IServiceProvider _) =>
        new DescribedDocumentReader(
            new AppleVisionRecognizeDocumentsReader(),
            new DocumentReaderDescriptor(
                "apple-vision-document",
                "Apple Vision RecognizeDocuments",
                "Reads images and PDFs on-device with Vision RecognizeDocumentsRequest; requires iOS or Mac Catalyst 26+."));
#endif

    private static DocumentExtractionClient Describe(
        DocumentExtractionClient client, DocumentExtractionClientDescriptor descriptor, IServiceProvider provider) =>
        new DocumentExtractionClientBuilder(client)
            .Use(inner => new DescribedDocumentExtractionClient(inner, descriptor))
            .UseLogging(provider.GetRequiredService<ILoggerFactory>())
            .Build();
}
