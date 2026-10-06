using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton<DocumentSettingsViewModel>();
        services.AddSingleton<DocumentPlaygroundViewModel>();
        services.AddTransient<Page, DocumentPage>();

#if IOS || MACCATALYST
        if (OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26))
            services.AddSingleton<IngestionDocumentReader>(CreateAppleDocumentReader);
#endif

        if (settings.DocumentIntelligenceEndpoint is not null &&
            !string.IsNullOrWhiteSpace(settings.DocumentIntelligenceKey))
            services.AddSingleton<IngestionDocumentReader>(_ => new DescribedDocumentReader(
                new AzureDocumentIntelligenceReader(settings.DocumentIntelligenceEndpoint, settings.DocumentIntelligenceKey),
                new DocumentReaderDescriptor(
                    "azure-document-intelligence",
                    "Azure Document Intelligence",
                    "Uploads the entire document to Azure for prebuilt-layout analysis; may incur charges.",
                    IsCloud: true)));
        return services;
    }

#if IOS || MACCATALYST
    [SupportedOSPlatform("ios26.0")]
    [SupportedOSPlatform("maccatalyst26.0")]
    private static IngestionDocumentReader CreateAppleDocumentReader(IServiceProvider _) =>
        new DescribedDocumentReader(
            new AppleVisionDocumentReader(),
            new DocumentReaderDescriptor(
                "apple-vision-document",
                "Apple Vision",
                "Reads images and PDFs on-device with Apple Vision; requires iOS or Mac Catalyst 26+."));
#endif
}
