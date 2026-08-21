using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Maui.Essentials.AI;

namespace DocumentExtractionSample.Services;

/// <summary>Reports incremental progress while a document is being extracted.</summary>
public readonly record struct DocumentExtractionProgress(int? PagesProcessed, int? TotalPages, DocumentPage Page);

/// <summary>Picks the correct Apple document-extraction client for a media type and drives streaming extraction.</summary>
public static class DocumentExtractionRunner
{
	/// <summary>Gets a value indicating whether the current OS version supports Apple Vision document recognition
	/// (iOS/Mac Catalyst/macOS 26 or later).</summary>
	public static bool IsPlatformSupported =>
#if IOS
		OperatingSystem.IsIOSVersionAtLeast(26);
#elif MACCATALYST
		OperatingSystem.IsMacCatalystVersionAtLeast(26);
#elif MACOS
		OperatingSystem.IsMacOSVersionAtLeast(26);
#else
		false;
#endif

	/// <summary>Maps a picked file name to a supported Apple Vision / PDFKit media type, or <see langword="null"/>
	/// when the extension is not supported by this sample.</summary>
	public static string? GetMediaType(string fileName)
	{
		var extension = Path.GetExtension(fileName);
		return extension.ToLowerInvariant() switch
		{
			".png" => "image/png",
			".jpg" or ".jpeg" => "image/jpeg",
			".heic" => "image/heic",
			".tif" or ".tiff" => "image/tiff",
			".pdf" => "application/pdf",
			_ => null,
		};
	}

	/// <summary>Creates the appropriate <see cref="IDocumentExtractionClient"/> for the given media type. PDFs are
	/// rendered page-by-page with PDFKit and fed into Apple Vision; images go directly to Apple Vision. There is no
	/// fallback client — extraction fails outright when the platform or media type isn't supported.</summary>
	public static IDocumentExtractionClient CreateClient(string mediaType) =>
		string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase)
			? new ApplePdfKitRenderingExtractionClient(new AppleVisionRecognizeDocumentsClient())
			: new AppleVisionRecognizeDocumentsClient();

	/// <summary>Streams extraction results for a document, invoking <paramref name="onProgress"/> for each page as
	/// it completes and returning the assembled <see cref="DocumentExtractionResult"/> once the stream ends.</summary>
	public static async Task<DocumentExtractionResult> ExtractAsync(
		Stream document,
		string mediaType,
		Action<DocumentExtractionProgress> onProgress,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
		ArgumentNullException.ThrowIfNull(onProgress);

		using var client = CreateClient(mediaType);
		var pages = new List<DocumentExtractionPageResult>();
		await foreach (var page in client.ExtractPagesAsync(document, mediaType, cancellationToken: cancellationToken)
			.WithCancellation(cancellationToken)
			.ConfigureAwait(false))
		{
			pages.Add(page);
			onProgress(new DocumentExtractionProgress(page.PagesProcessed, page.TotalPages, page.Page));
		}
		return pages.ToDocumentExtractionResult();
	}
}
