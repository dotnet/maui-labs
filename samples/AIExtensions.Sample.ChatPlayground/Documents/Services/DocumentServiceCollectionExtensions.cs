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
        services.AddSingleton<DocumentInputService>();
        services.AddSingleton<DocumentReadingService>();
        services.AddSingleton<DocumentExtractionService>();
        services.AddSingleton<DocumentSettingsViewModel>();
        services.AddSingleton<DocumentPlaygroundViewModel>();
        services.AddTransient<Page, DocumentPage>();

#if IOS || MACCATALYST
        if (OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26))
        {
            services.AddSingleton<IngestionDocumentReader>(CreateAppleRecognizeDocumentsReader);
            services.AddSingleton<IDocumentExtractionClient>(CreateAppleRecognizeDocumentsClient);
        }
#endif

        if (settings.DocumentIntelligenceEndpoint is not null &&
            !string.IsNullOrWhiteSpace(settings.DocumentIntelligenceKey))
        {
            services.AddSingleton<IngestionDocumentReader>(_ => CreateAzureDocumentReader(settings));
            services.AddSingleton<IDocumentExtractionClient>(provider =>
                Describe(new AzureDocumentIntelligenceExtractionClient(
                    settings.DocumentIntelligenceEndpoint!, settings.DocumentIntelligenceKey!),
                    new DocumentExtractionClientDescriptor(
                        "azure-document-intelligence", "Azure Document Intelligence",
                        "Uploads the document for actual prebuilt-layout output; charges may apply.", IsCloud: true), provider));
        }

        if (!string.IsNullOrWhiteSpace(settings.DocumentDeploymentName))
        {
            services.AddKeyedSingleton<HttpClient>("foundry-document", (_, _) =>
            {
                var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                {
                    BaseAddress = settings.GetFoundryDocumentEndpoint(),
                    Timeout = Timeout.InfiniteTimeSpan,
                };
                client.DefaultRequestHeaders.Add("api-key", settings.ApiKey);
                return client;
            });
            services.AddSingleton<IngestionDocumentReader>(provider => CreateFoundryDocumentReader(
                provider.GetRequiredKeyedService<HttpClient>("foundry-document"), settings));
            services.AddSingleton<IDocumentExtractionClient>(provider =>
                Describe(new FoundryMistralDocumentExtractionClient(
                    provider.GetRequiredKeyedService<HttpClient>("foundry-document"), settings.DocumentDeploymentName!),
                    new DocumentExtractionClientDescriptor(
                        "foundry-mistral-document", "Foundry Mistral Document AI",
                        "Uploads the document to the configured Mistral OCR deployment; charges may apply.", IsCloud: true), provider));
        }

        return services;
    }

#if IOS || MACCATALYST
    [SupportedOSPlatform("ios26.0")]
    [SupportedOSPlatform("maccatalyst26.0")]
    private static IDocumentExtractionClient CreateAppleRecognizeDocumentsClient(IServiceProvider provider) =>
        Describe(new AppleVisionRecognizeDocumentsClient(),
            new DocumentExtractionClientDescriptor(
                "apple-vision-document", "Apple Vision RecognizeDocuments",
                "RecognizeDocumentsRequest on-device. Returns only the Apple/proposed-contract intersection; no synthesized metadata."), provider);

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

    private static IDocumentExtractionClient Describe(
        IDocumentExtractionClient client, DocumentExtractionClientDescriptor descriptor, IServiceProvider provider) =>
        client.AsBuilder()
            .Use(inner => new DescribedDocumentExtractionClient(inner, descriptor))
            .UseLogging(provider.GetRequiredService<ILoggerFactory>())
            .UseOpenTelemetry(sourceName: "AIExtensions.Sample.ChatPlayground.Documents")
            .Build();

    private static IngestionDocumentReader CreateAzureDocumentReader(AISettings settings) =>
        new DescribedDocumentReader(
            new AzureDocumentIntelligenceReader(settings.DocumentIntelligenceEndpoint!, settings.DocumentIntelligenceKey!),
            new DocumentReaderDescriptor(
                "azure-document-intelligence",
                "Azure Document Intelligence",
                "Uploads the entire document to Azure for prebuilt-layout analysis; may incur charges.",
                IsCloud: true));

    private static IngestionDocumentReader CreateFoundryDocumentReader(HttpClient client, AISettings settings) =>
        new DescribedDocumentReader(
            new FoundryMistralDocumentReader(client, settings.DocumentDeploymentName!),
            new DocumentReaderDescriptor(
                "foundry-mistral-document",
                "Foundry Mistral Document AI",
                "Uploads the entire document to the configured Foundry document model; may incur charges.",
                IsCloud: true));
}
