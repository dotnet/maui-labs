using Microsoft.Extensions.DocumentExtraction;

#if IOS || MACCATALYST
using System.Runtime.Versioning;
using Microsoft.Maui.Essentials.AI;
#endif

namespace AIExtensions.Sample.ChatPlayground;

internal sealed class AppleDocumentExtractionProvider : IDocumentExtractionProvider
{
    public AppleDocumentExtractionProvider()
    {
        Descriptor = CreateDescriptor();
    }

    public DocumentProviderDescriptor Descriptor { get; }

    public IDocumentExtractionClient CreateClient(string mediaType)
    {
        if (!Descriptor.IsAvailable)
            throw new NotSupportedException(Descriptor.Description);

#if IOS || MACCATALYST
        return CreateAppleClient(mediaType);
#else
        throw new PlatformNotSupportedException(Descriptor.Description);
#endif
    }

    public DocumentExtractionOptions CreateOptions(DocumentExtractionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

#if IOS || MACCATALYST
        if (Descriptor.IsAvailable)
        {
            return new DocumentExtractionOptions()
                .WithAppleBarcodeDetection(settings.DetectBarcodes)
                .WithAppleAutomaticLanguageDetection(settings.AutomaticallyDetectLanguage);
        }
#endif

        return new DocumentExtractionOptions();
    }

    public string GetCapabilitiesSummary()
    {
        if (!Descriptor.IsAvailable)
            return Descriptor.Description;

#if IOS || MACCATALYST
        return GetAppleCapabilitiesSummary();
#else
        return Descriptor.Description;
#endif
    }

    private static DocumentProviderDescriptor CreateDescriptor()
    {
#if IOS
        var available = OperatingSystem.IsIOSVersionAtLeast(26);
        return new(
            "apple-vision",
            "Apple Vision",
            available
                ? "RecognizeDocumentsRequest runs entirely on-device. PDF pages are rendered with PDFKit and passed to the same Vision client."
                : "Apple Vision document extraction requires iOS 26 or later. No fallback engine is used.",
            available,
            SendsDocumentOffDevice: false);
#elif MACCATALYST
        var available = OperatingSystem.IsMacCatalystVersionAtLeast(26);
        return new(
            "apple-vision",
            "Apple Vision",
            available
                ? "RecognizeDocumentsRequest runs entirely on-device. PDF pages are rendered with PDFKit and passed to the same Vision client."
                : "Apple Vision document extraction requires Mac Catalyst 26 or later. No fallback engine is used.",
            available,
            SendsDocumentOffDevice: false);
#elif ANDROID
        return new(
            "apple-vision",
            "Unavailable on Android",
            "No Android document extraction provider is registered. This playground does not substitute cloud OCR or another local engine.",
            false,
            SendsDocumentOffDevice: false);
#elif WINDOWS
        return new(
            "apple-vision",
            "Unavailable on Windows",
            "No Windows document extraction provider is registered. This playground does not substitute cloud OCR or another local engine.",
            false,
            SendsDocumentOffDevice: false);
#else
        return new(
            "apple-vision",
            "Unavailable",
            "No document extraction provider is registered for this platform.",
            false,
            SendsDocumentOffDevice: false);
#endif
    }

#if IOS || MACCATALYST
    [SupportedOSPlatform("ios26.0")]
    [SupportedOSPlatform("maccatalyst26.0")]
    private static IDocumentExtractionClient CreateAppleClient(string mediaType)
    {
        var vision = new AppleVisionRecognizeDocumentsClient();
        return string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase)
            ? new ApplePdfKitRenderingExtractionClient(vision)
            : vision;
    }

    [SupportedOSPlatform("ios26.0")]
    [SupportedOSPlatform("maccatalyst26.0")]
    private static string GetAppleCapabilitiesSummary()
    {
        using var client = new AppleVisionRecognizeDocumentsClient();
        var capabilities = client.GetService<AppleVisionDocumentCapabilities>()
            ?? throw new InvalidOperationException("Apple Vision capabilities are unavailable.");
        var writer = new System.Text.StringBuilder();
        writer.AppendLine("Recognition languages:");
        foreach (var language in capabilities.RecognitionLanguages)
            writer.AppendLine($"  - {language}");
        writer.AppendLine();
        writer.AppendLine("Barcode symbologies:");
        foreach (var symbology in capabilities.BarcodeSymbologies)
            writer.AppendLine($"  - {symbology}");
        writer.AppendLine();
        writer.AppendLine("RecognizeDocumentsRequest revisions:");
        foreach (var revision in capabilities.Revisions)
            writer.AppendLine($"  - {revision}");
        return writer.ToString();
    }
#endif
}
