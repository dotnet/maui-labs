using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI.Internal.DocumentExtraction;

namespace Microsoft.Maui.Essentials.AI;

internal sealed partial class AppleVisionRecognizeDocumentsClient : IDocumentExtractionClient
{
	private readonly Func<Stream, string, RecognizeDocumentsRequestOptions?, CancellationToken,
		IAsyncEnumerable<AppleVisionRecognizeDocumentsPageSnapshot>> _recognize;
	private readonly Lazy<AppleVisionRecognizeDocumentsCapabilities> _capabilities;
	private readonly DocumentExtractionClientMetadata _metadata = new("apple.vision",
		new Uri("https://developer.apple.com/documentation/vision/recognizedocumentsrequest"), "recognize-documents");

	internal AppleVisionRecognizeDocumentsClient(
		Func<Stream, string, RecognizeDocumentsRequestOptions?, CancellationToken,
			IAsyncEnumerable<AppleVisionRecognizeDocumentsPageSnapshot>> recognize,
		Func<AppleVisionRecognizeDocumentsCapabilities> getCapabilities)
	{
		_recognize = recognize ?? throw new ArgumentNullException(nameof(recognize));
		_capabilities = new(getCapabilities ?? throw new ArgumentNullException(nameof(getCapabilities)));
	}

	internal AppleVisionRecognizeDocumentsCapabilities GetCapabilities() => _capabilities.Value;

	public object? GetService(Type serviceType, object? serviceKey = null)
	{
		ArgumentNullException.ThrowIfNull(serviceType);
		if (serviceKey is not null)
			return null;
		if (serviceType == typeof(DocumentExtractionClientMetadata))
			return _metadata;
		if (serviceType == typeof(AppleVisionRecognizeDocumentsCapabilities))
			return GetCapabilities();
		return serviceType.IsInstanceOfType(this) ? this : null;
	}

	public void Dispose() { }

	public async Task<DocumentExtractionResult> ExtractAsync(
		Stream document, string mediaType, DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default)
	{
		var pages = new List<DocumentPage>();
		var properties = new AdditionalPropertiesDictionary();
		int? pagesProcessed = null;
		await foreach (var update in ExtractPagesAsync(document, mediaType, options, cancellationToken).ConfigureAwait(false))
		{
			pages.Add(update.Page);
			pagesProcessed = update.PagesProcessed;
			properties["apple.vision.totalPages"] = update.TotalPages;
			if (update.AdditionalProperties is { } updateProperties)
				foreach (var property in updateProperties)
					properties[property.Key] = property.Value;
		}
		return new(pages.ToArray())
		{
			Usage = new() { PagesProcessed = pagesProcessed },
			AdditionalProperties = properties,
			RawRepresentation = pages.Select(page => page.RawRepresentation).ToArray(),
		};
	}

	public async IAsyncEnumerable<DocumentExtractionPageResult> ExtractPagesAsync(
		Stream document, string mediaType, DocumentExtractionOptions? options = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
		if (!document.CanRead)
			throw new ArgumentException("The document stream must be readable.", nameof(document));
		if (mediaType.ToLowerInvariant() is not
			("application/pdf" or "image/jpeg" or "image/jpg" or "image/png" or "image/heic" or "image/tiff"))
			throw new NotSupportedException($"Apple Vision does not support media type '{mediaType}'.");
		cancellationToken.ThrowIfCancellationRequested();
		var requestOptions = AppleVisionRecognizeDocumentsOptions.ToRequestOptions(options);
		var optionsSnapshot = options?.Clone();
		if (optionsSnapshot?.AdditionalProperties is { } optionProperties && requestOptions is not null)
		{
			if (requestOptions.RecognitionLanguages is { } languages)
				optionProperties[AppleVisionRecognizeDocumentsOptions.RecognitionLanguages] = languages;
			if (requestOptions.CustomWords is { } words)
				optionProperties[AppleVisionRecognizeDocumentsOptions.CustomWords] = words;
			if (requestOptions.BarcodeSymbologies is { } symbologies)
				optionProperties[AppleVisionRecognizeDocumentsOptions.BarcodeSymbologies] = symbologies;
			if (requestOptions.RegionOfInterest is { } region)
				optionProperties[AppleVisionRecognizeDocumentsOptions.RegionOfInterest] = region;
		}
		var processed = 0;
		await foreach (var sourcePage in _recognize(document, mediaType, requestOptions, cancellationToken).ConfigureAwait(false))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var page = AppleVisionRecognizeDocumentsMapper.ToPage(sourcePage.Snapshot, sourcePage.PageNumber,
				sourcePage.SourcePixelWidth, sourcePage.SourcePixelHeight, sourcePage.Revision);
			page.AdditionalProperties!["apple.vision.totalPages"] = sourcePage.TotalPages;
			if (sourcePage.PdfPage is { } pdf)
			{
				var properties = page.AdditionalProperties;
				properties["apple.pdf.pageInfo"] = pdf;
				properties["apple.pdf.pageLabel"] = pdf.Label;
				properties["apple.pdf.rotation"] = pdf.Rotation;
				properties["apple.pdf.displayBox"] = pdf.DisplayBox;
				properties["apple.pdf.renderDpi"] = pdf.RequestedDpi;
				properties["apple.pdf.effectiveRenderDpi"] = pdf.EffectiveDpi;
				properties["apple.pdf.widthPoints"] = pdf.WidthPoints;
				properties["apple.pdf.heightPoints"] = pdf.HeightPoints;
			}
			var update = new DocumentExtractionPageResult(page)
			{
				PagesProcessed = ++processed,
				TotalPages = sourcePage.TotalPages,
				Usage = new DocumentExtractionUsage { PagesProcessed = processed },
				RawRepresentation = page.RawRepresentation,
				AdditionalProperties = new(),
			};
			update.AdditionalProperties["apple.vision.request"] = "recognize-documents";
			update.AdditionalProperties["apple.vision.modelId"] = _metadata.DefaultModelId;
			update.AdditionalProperties["apple.vision.revision"] = sourcePage.Revision;
			if (optionsSnapshot is not null)
				update.AdditionalProperties["apple.vision.options"] = optionsSnapshot;
			yield return update;
		}
		cancellationToken.ThrowIfCancellationRequested();
	}

}

internal sealed class AppleVisionRecognizeDocumentsCapabilities
{
	internal required IReadOnlyList<string> SupportedRecognitionLanguages { get; init; }
	internal required IReadOnlyList<string> SupportedBarcodeSymbologies { get; init; }
	internal required IReadOnlyList<int> SupportedRevisions { get; init; }
}

internal sealed class AppleVisionRecognizeDocumentsPageSnapshot
{
	internal required int PageNumber { get; init; }
	internal required int TotalPages { get; init; }
	internal required System.Text.Json.JsonElement Snapshot { get; init; }
	internal int? SourcePixelWidth { get; init; }
	internal int? SourcePixelHeight { get; init; }
	internal int Revision { get; init; } = 1;
	internal AppleVisionRecognizeDocumentsPdfPageInfo? PdfPage { get; init; }
}
