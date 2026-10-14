using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.DocumentExtraction;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>Extracts images and PDF pages using Vision's RecognizeDocumentsRequest.</summary>
[SupportedOSPlatform("ios26.0")]
[SupportedOSPlatform("maccatalyst26.0")]
[SupportedOSPlatform("macos26.0")]
public sealed partial class AppleVisionRecognizeDocumentsClient : IDocumentExtractionClient
{
	private readonly Func<Stream, string, RecognizeDocumentsRequestOptions?, CancellationToken,
		IAsyncEnumerable<AppleVisionRecognizeDocumentsPageSnapshot>> _recognize;
	private bool _disposed;

	internal AppleVisionRecognizeDocumentsClient(
		Func<Stream, string, RecognizeDocumentsRequestOptions?, CancellationToken,
			IAsyncEnumerable<AppleVisionRecognizeDocumentsPageSnapshot>> recognize)
	{
		_recognize = recognize ?? throw new ArgumentNullException(nameof(recognize));
	}

	/// <inheritdoc />
	public IAsyncEnumerable<DocumentExtractionPage> ExtractPagesAsync(
		Stream document, string identifier, string mediaType, DocumentExtractionOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		ArgumentNullException.ThrowIfNull(document);
		ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
		ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
		if (!document.CanRead)
			throw new ArgumentException("The document stream must be readable.", nameof(document));
		if (mediaType.ToLowerInvariant() is not
			("application/pdf" or "image/jpeg" or "image/jpg" or "image/png" or "image/heic" or "image/tiff"))
			throw new NotSupportedException($"Apple Vision does not support media type '{mediaType}'.");
		var request = options?.Clone();
		var nativeOptions = RecognizeDocumentsRequestOptions.From(request);
		return ExtractCoreAsync(document, identifier, mediaType, nativeOptions,
			request?.CaptureRawOutput ?? true, cancellationToken);
	}

	private async IAsyncEnumerable<DocumentExtractionPage> ExtractCoreAsync(
		Stream document, string identifier, string mediaType, RecognizeDocumentsRequestOptions? options,
		bool captureRawOutput, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		await foreach (var source in _recognize(document, mediaType, options, cancellationToken).ConfigureAwait(false))
		{
			cancellationToken.ThrowIfCancellationRequested();
			yield return AppleVisionRecognizeDocumentsMapper.ToPage(
				source.Snapshot, source.PageNumber, identifier, captureRawOutput);
		}
		cancellationToken.ThrowIfCancellationRequested();
	}

	/// <inheritdoc />
	public object? GetService(Type serviceType, object? serviceKey = null)
	{
		ArgumentNullException.ThrowIfNull(serviceType);
		ObjectDisposedException.ThrowIf(_disposed, this);
		return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
	}

	/// <inheritdoc />
	public void Dispose() => _disposed = true;
}

internal sealed record AppleVisionRecognizeDocumentsPageSnapshot(int PageNumber, int TotalPages, JsonElement Snapshot);

internal sealed class RecognizeDocumentsRequestOptions
{
	internal string[]? RecognitionLanguages { get; init; }
	internal string[]? CustomWords { get; init; }
	internal bool? UseLanguageCorrection { get; init; }
	internal bool? AutomaticallyDetectLanguage { get; init; }
	internal int? MaximumCandidateCount { get; init; }
	internal float? MinimumTextHeightFraction { get; init; }
	internal bool? BarcodeDetectionEnabled { get; init; }
	internal string[]? BarcodeSymbologies { get; init; }
	internal bool? CoalesceCompositeSymbologies { get; init; }
	internal float[]? RegionOfInterest { get; init; }
	internal int? Revision { get; init; }

	internal static RecognizeDocumentsRequestOptions? From(DocumentExtractionOptions? options)
	{
		if (options?.ModelId is not null)
			throw new NotSupportedException("RecognizeDocumentsRequest does not expose model selection.");
		if (options is null || options.AdditionalProperties.Count == 0)
			return null;
		var supported = new HashSet<string>(StringComparer.Ordinal)
		{
			"recognitionLanguages", "customWords", "useLanguageCorrection", "automaticallyDetectLanguage",
			"maximumCandidateCount", "minimumTextHeightFraction", "barcodeDetectionEnabled",
			"barcodeSymbologies", "coalesceCompositeSymbologies", "regionOfInterest", "revision",
		};
		foreach (var key in options.AdditionalProperties.Keys)
			if (!key.StartsWith("apple.vision.", StringComparison.Ordinal) || !supported.Contains(key[13..]))
				throw new NotSupportedException($"Unsupported Apple Vision request option '{key}'.");

		T? Scalar<T>(string name) where T : struct =>
			!options.AdditionalProperties.TryGetValue("apple.vision." + name, out var value) ? null :
			value is T actual ? actual : throw new ArgumentException($"Apple Vision option '{name}' requires {typeof(T).Name}.");
		string[]? Strings(string name)
		{
			if (!options.AdditionalProperties.TryGetValue("apple.vision." + name, out var value))
				return null;
			if (value is not string[] strings || strings.Any(string.IsNullOrWhiteSpace))
				throw new ArgumentException($"Apple Vision option '{name}' requires an array of non-empty strings.");
			return [.. strings];
		}
		var count = Scalar<int>("maximumCandidateCount");
		var fraction = Scalar<float>("minimumTextHeightFraction");
		var revision = Scalar<int>("revision");
		if (count is < 1 or > 10 || revision is < 1 ||
			fraction is { } f && (!float.IsFinite(f) || f is < 0 or > 1))
			throw new ArgumentOutOfRangeException(nameof(options), "Invalid Apple Vision request option.");
		float[]? region = null;
		if (options.AdditionalProperties.TryGetValue("apple.vision.regionOfInterest", out var roi))
		{
			if (roi is not float[] values || values.Length != 4 ||
				values.Any(value => !float.IsFinite(value)) ||
				values[0] is < 0 or > 1 || values[1] is < 0 or > 1 ||
				values[2] is <= 0 or > 1 || values[3] is <= 0 or > 1 ||
				values[0] + values[2] > 1 || values[1] + values[3] > 1)
				throw new ArgumentException("Apple Vision regionOfInterest requires [x, y, width, height] within [0, 1].");
			region = [.. values];
		}
		return new()
		{
			RecognitionLanguages = Strings("recognitionLanguages"),
			CustomWords = Strings("customWords"),
			UseLanguageCorrection = Scalar<bool>("useLanguageCorrection"),
			AutomaticallyDetectLanguage = Scalar<bool>("automaticallyDetectLanguage"),
			MaximumCandidateCount = count,
			MinimumTextHeightFraction = fraction,
			BarcodeDetectionEnabled = Scalar<bool>("barcodeDetectionEnabled"),
			BarcodeSymbologies = Strings("barcodeSymbologies"),
			CoalesceCompositeSymbologies = Scalar<bool>("coalesceCompositeSymbologies"),
			RegionOfInterest = region,
			Revision = revision,
		};
	}
}
