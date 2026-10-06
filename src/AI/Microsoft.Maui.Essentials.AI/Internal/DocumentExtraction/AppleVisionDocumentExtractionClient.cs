using System.Runtime.CompilerServices;
using Microsoft.Maui.Essentials.AI.Internal.DocumentExtraction;

namespace Microsoft.Maui.Essentials.AI;

internal sealed partial class AppleVisionDocumentExtractionClient
{
	private readonly Func<Stream, string, DocumentExtractionOptions?, CancellationToken,
		IAsyncEnumerable<AppleVisionSourcePage>> _recognize;
	private readonly Func<AppleVisionDocumentCapabilities> _getCapabilities;
	private AppleVisionDocumentCapabilities? _capabilities;

	internal AppleVisionDocumentExtractionClient(
		Func<Stream, string, DocumentExtractionOptions?, CancellationToken, IAsyncEnumerable<AppleVisionSourcePage>> recognize,
		Func<AppleVisionDocumentCapabilities> getCapabilities)
	{
		_recognize = recognize ?? throw new ArgumentNullException(nameof(recognize));
		_getCapabilities = getCapabilities ?? throw new ArgumentNullException(nameof(getCapabilities));
	}

	internal AppleVisionDocumentCapabilities GetCapabilities() => _capabilities ??= _getCapabilities();

	internal async Task<DocumentExtractionResult> ExtractAsync(
		Stream source, string mediaType, DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default)
	{
		var result = new DocumentExtractionResult();
		await foreach (var update in ExtractPagesAsync(source, mediaType, options, cancellationToken).ConfigureAwait(false))
		{
			result.Pages.Add(update.Page);
			result.Usage.PagesProcessed = update.Usage.PagesProcessed;
			result.Usage.TotalPages = update.Usage.TotalPages;
			foreach (var property in update.AdditionalProperties)
				result.AdditionalProperties[property.Key] = property.Value;
		}
		result.RawRepresentation = result.Pages.Select(page => page.RawRepresentation).ToArray();
		return result;
	}

	internal async IAsyncEnumerable<DocumentExtractionPageResult> ExtractPagesAsync(
		Stream source, string mediaType, DocumentExtractionOptions? options = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
		if (!source.CanRead)
			throw new ArgumentException("The document stream must be readable.", nameof(source));
		if (mediaType.ToLowerInvariant() is not
			("application/pdf" or "image/jpeg" or "image/jpg" or "image/png" or "image/heic" or "image/tiff"))
			throw new NotSupportedException($"Apple Vision does not support media type '{mediaType}'.");
		cancellationToken.ThrowIfCancellationRequested();
		var requestOptions = ValidateOptions(options);
		var processed = 0;
		await foreach (var sourcePage in _recognize(source, mediaType, requestOptions, cancellationToken).ConfigureAwait(false))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var page = AppleVisionDocumentMapper.ToPage(sourcePage.Snapshot, sourcePage.PageNumber,
				sourcePage.SourcePixelWidth, sourcePage.SourcePixelHeight, sourcePage.Revision);
			page.PdfPage = sourcePage.PdfPage;
			page.AdditionalProperties["apple.vision.totalPages"] = sourcePage.TotalPages;
			if (sourcePage.PdfPage is { } pdf)
			{
				var properties = page.AdditionalProperties;
				properties["apple.pdf.pageLabel"] = pdf.Label;
				properties["apple.pdf.rotation"] = pdf.Rotation;
				properties["apple.pdf.displayBox"] = pdf.DisplayBox;
				properties["apple.pdf.renderDpi"] = pdf.RequestedDpi;
				properties["apple.pdf.effectiveRenderDpi"] = pdf.EffectiveDpi;
				properties["apple.pdf.widthPoints"] = pdf.WidthPoints;
				properties["apple.pdf.heightPoints"] = pdf.HeightPoints;
			}
			var update = new DocumentExtractionPageResult
			{
				Page = page,
				Usage = new DocumentExtractionUsage { PagesProcessed = ++processed, TotalPages = sourcePage.TotalPages },
				RawRepresentation = page.RawRepresentation,
			};
			update.AdditionalProperties["apple.vision.request"] = "recognize-documents";
			update.AdditionalProperties["apple.vision.revision"] = sourcePage.Revision;
			if (requestOptions is not null)
				update.AdditionalProperties["apple.vision.options"] = requestOptions;
			yield return update;
		}
		cancellationToken.ThrowIfCancellationRequested();
	}

	private static DocumentExtractionOptions? ValidateOptions(DocumentExtractionOptions? options)
	{
		if (options is null)
			return null;
		if (options.ModelId is not null &&
			!string.Equals(options.ModelId, "recognize-documents", StringComparison.OrdinalIgnoreCase))
			throw new NotSupportedException($"Apple Vision does not support model '{options.ModelId}'.");
		if (options.MaximumCandidateCount is < 1 or > 10)
			throw new ArgumentOutOfRangeException(nameof(options.MaximumCandidateCount));
		if (options.Revision is < 1)
			throw new ArgumentOutOfRangeException(nameof(options.Revision));
		if (options.MinimumTextHeightFraction is { } fraction && (!float.IsFinite(fraction) || fraction is < 0 or > 1))
			throw new ArgumentOutOfRangeException(nameof(options.MinimumTextHeightFraction));
		if (options.RegionOfInterest is { } roi && (roi.Length != 4 || roi.Any(value => !float.IsFinite(value)) ||
			roi[0] is < 0 or > 1 || roi[1] is < 0 or > 1 || roi[2] is <= 0 or > 1 || roi[3] is <= 0 or > 1 ||
			roi[0] + roi[2] > 1 || roi[1] + roi[3] > 1))
			throw new ArgumentOutOfRangeException(nameof(options.RegionOfInterest));
		foreach (var values in new[] { options.RecognitionLanguages, options.CustomWords, options.BarcodeSymbologies })
			if (values?.Any(string.IsNullOrWhiteSpace) == true)
				throw new ArgumentException("Recognition option values must not be empty.", nameof(options));
		var copy = new DocumentExtractionOptions
		{
			ModelId = options.ModelId,
			RecognitionLanguages = options.RecognitionLanguages?.ToArray(),
			CustomWords = options.CustomWords?.ToArray(),
			UseLanguageCorrection = options.UseLanguageCorrection,
			AutomaticallyDetectLanguage = options.AutomaticallyDetectLanguage,
			MaximumCandidateCount = options.MaximumCandidateCount,
			MinimumTextHeightFraction = options.MinimumTextHeightFraction,
			BarcodeDetectionEnabled = options.BarcodeDetectionEnabled,
			BarcodeSymbologies = options.BarcodeSymbologies?.ToArray(),
			CoalesceCompositeSymbologies = options.CoalesceCompositeSymbologies,
			RegionOfInterest = options.RegionOfInterest?.ToArray(),
			Revision = options.Revision,
		};
		foreach (var property in options.AdditionalProperties)
			copy.AdditionalProperties[property.Key] = property.Value;
		return copy;
	}
}

internal sealed class AppleVisionDocumentCapabilities
{
	internal required IReadOnlyList<string> RecognitionLanguages { get; init; }
	internal required IReadOnlyList<string> BarcodeSymbologies { get; init; }
	internal required IReadOnlyList<int> Revisions { get; init; }
}

internal sealed class AppleVisionSourcePage
{
	internal required int PageNumber { get; init; }
	internal required int TotalPages { get; init; }
	internal required System.Text.Json.JsonElement Snapshot { get; init; }
	internal int? SourcePixelWidth { get; init; }
	internal int? SourcePixelHeight { get; init; }
	internal int Revision { get; init; } = 1;
	internal DocumentPdfPageInfo? PdfPage { get; init; }
}
