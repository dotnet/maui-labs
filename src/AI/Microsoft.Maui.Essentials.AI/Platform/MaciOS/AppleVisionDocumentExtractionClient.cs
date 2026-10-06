using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>Extracts structured image and PDF content using Apple Vision's RecognizeDocumentsRequest.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("ios26.0")]
[System.Runtime.Versioning.SupportedOSPlatform("maccatalyst26.0")]
[System.Runtime.Versioning.SupportedOSPlatform("macos26.0")]
public sealed class AppleVisionDocumentExtractionClient : IDocumentExtractionClient
{
	private readonly AppleVisionDocumentRecognizer _recognizer = new();
	private DocumentExtractionClientMetadata? _metadata;
	private AppleVisionDocumentCapabilities? _capabilities;

	/// <inheritdoc />
	public async Task<DocumentExtractionResult> ExtractAsync(
		Stream document,
		string mediaType,
		DocumentExtractionOptions? options = null,
		CancellationToken cancellationToken = default) =>
		await ExtractPagesAsync(document, mediaType, options, cancellationToken)
			.ToDocumentExtractionResultAsync(cancellationToken)
			.ConfigureAwait(false);

	/// <inheritdoc />
	public async IAsyncEnumerable<DocumentExtractionPageResult> ExtractPagesAsync(
		Stream document,
		string mediaType,
		DocumentExtractionOptions? options = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
		if (options?.ModelId is not null &&
			!string.Equals(options.ModelId, "recognize-documents", StringComparison.OrdinalIgnoreCase))
		{
			throw new NotSupportedException($"Model ID '{options.ModelId}' is not supported. Use 'recognize-documents'.");
		}

		var recognitionOptions = ToRecognitionOptions(options);
		await foreach (var recognized in _recognizer
			.RecognizePagesAsync(document, mediaType, recognitionOptions, cancellationToken)
			.ConfigureAwait(false))
		{
			var page = AppleVisionDocumentMapper.ToPage(
				recognized.Snapshot,
				recognized.PageNumber,
				recognized.SourcePixelWidth,
				recognized.SourcePixelHeight,
				recognized.Revision);
			AddPdfPageProperties(page, recognized.PdfPage);

			var properties = CreateRequestProperties(recognized.Revision);
			if (recognized.PdfPage is { } pdfPage)
			{
				properties["apple.pdf.pageNumber"] = recognized.PageNumber;
				properties["apple.pdf.totalPages"] = recognized.TotalPages;
				properties["apple.pdf.renderDpi"] = pdfPage.RequestedDpi;
				properties["apple.pdf.effectiveRenderDpi"] = pdfPage.EffectiveDpi;
			}

			yield return new DocumentExtractionPageResult(page)
			{
				PagesProcessed = recognized.PageNumber,
				TotalPages = recognized.TotalPages,
				AdditionalProperties = properties,
			};
		}
	}

	/// <inheritdoc />
	public object? GetService(Type serviceType, object? serviceKey = null)
	{
		ArgumentNullException.ThrowIfNull(serviceType);
		if (serviceKey is not null)
		{
			return null;
		}
		if (serviceType == typeof(DocumentExtractionClientMetadata))
		{
			return _metadata ??= new DocumentExtractionClientMetadata(
				providerName: "apple.vision",
				defaultModelId: "recognize-documents");
		}
		if (serviceType == typeof(AppleVisionDocumentCapabilities))
		{
			return _capabilities ??= new AppleVisionDocumentCapabilities(_recognizer.GetCapabilities());
		}
		return serviceType.IsInstanceOfType(this) ? this : null;
	}

	/// <inheritdoc />
	public void Dispose()
	{
	}

	private static AppleVisionRecognitionOptions? ToRecognitionOptions(DocumentExtractionOptions? options)
	{
		if (options?.AdditionalProperties is not { } properties)
		{
			return null;
		}

		return new AppleVisionRecognitionOptions
		{
			RecognitionLanguages = GetOption<string[]>(properties, AppleVisionDocumentOptions.RecognitionLanguagesKey),
			CustomWords = GetOption<string[]>(properties, AppleVisionDocumentOptions.CustomWordsKey),
			UseLanguageCorrection = GetOption<bool?>(properties, AppleVisionDocumentOptions.UseLanguageCorrectionKey),
			AutomaticallyDetectLanguage = GetOption<bool?>(properties, AppleVisionDocumentOptions.AutomaticallyDetectLanguageKey),
			MaximumCandidateCount = GetOption<int?>(properties, AppleVisionDocumentOptions.MaximumCandidateCountKey),
			MinimumTextHeightFraction = GetOption<float?>(properties, AppleVisionDocumentOptions.MinimumTextHeightFractionKey),
			BarcodeDetectionEnabled = GetOption<bool?>(properties, AppleVisionDocumentOptions.BarcodeDetectionEnabledKey),
			BarcodeSymbologies = GetOption<string[]>(properties, AppleVisionDocumentOptions.BarcodeSymbologiesKey),
			CoalesceCompositeSymbologies = GetOption<bool?>(properties, AppleVisionDocumentOptions.CoalesceCompositeSymbologiesKey),
			RegionOfInterest = GetOption<float[]>(properties, AppleVisionDocumentOptions.RegionOfInterestKey),
			Revision = GetOption<int?>(properties, AppleVisionDocumentOptions.RevisionKey),
		};
	}

	private static void AddPdfPageProperties(DocumentPage page, AppleVisionPdfPageInfo? pdfPage)
	{
		if (pdfPage is null)
		{
			return;
		}

		var properties = page.AdditionalProperties ??= new AdditionalPropertiesDictionary();
		properties["apple.pdf.pageLabel"] = pdfPage.Label;
		properties["apple.pdf.rotation"] = pdfPage.Rotation;
		properties["apple.pdf.displayBox"] = pdfPage.DisplayBox;
		properties["apple.pdf.renderDpi"] = pdfPage.RequestedDpi;
		properties["apple.pdf.effectiveRenderDpi"] = pdfPage.EffectiveDpi;
		properties["apple.pdf.widthPoints"] = pdfPage.WidthPoints;
		properties["apple.pdf.heightPoints"] = pdfPage.HeightPoints;
	}

	private static AdditionalPropertiesDictionary CreateRequestProperties(int revision) =>
		new()
		{
			["apple.vision.request"] = "recognize-documents",
			["apple.vision.revision"] = revision,
		};

	private static T? GetOption<T>(AdditionalPropertiesDictionary properties, string key) =>
		properties.TryGetValue(key, out var value) && value is T typed ? typed : default;
}
