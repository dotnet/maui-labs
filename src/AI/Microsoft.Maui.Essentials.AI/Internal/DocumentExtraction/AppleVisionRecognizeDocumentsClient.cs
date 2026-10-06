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
	private readonly Func<Stream, string, CancellationToken, IAsyncEnumerable<AppleVisionRecognizeDocumentsPageSnapshot>> _recognize;
	private readonly DocumentExtractionClientMetadata _metadata = new(
		providerName: "apple.vision",
		providerUri: new Uri("https://developer.apple.com/documentation/vision/recognizedocumentsrequest"));

	internal AppleVisionRecognizeDocumentsClient(
		Func<Stream, string, CancellationToken, IAsyncEnumerable<AppleVisionRecognizeDocumentsPageSnapshot>> recognize)
	{
		_recognize = recognize ?? throw new ArgumentNullException(nameof(recognize));
	}

	/// <inheritdoc />
	public Task<DocumentExtractionResult> ExtractAsync(
		Stream document,
		string mediaType,
		DocumentExtractionOptions? options = null,
		CancellationToken cancellationToken = default) =>
		ExtractPagesAsync(document, mediaType, options, cancellationToken)
			.ToDocumentExtractionResultAsync(cancellationToken);

	/// <inheritdoc />
	public async IAsyncEnumerable<DocumentExtractionPageResult> ExtractPagesAsync(
		Stream document,
		string mediaType,
		DocumentExtractionOptions? options = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
		if (!document.CanRead)
			throw new ArgumentException("The document stream must be readable.", nameof(document));
		if (mediaType.ToLowerInvariant() is not
			("application/pdf" or "image/jpeg" or "image/jpg" or "image/png" or "image/heic" or "image/tiff"))
			throw new NotSupportedException($"Apple Vision does not support media type '{mediaType}'.");
		if (options?.ModelId is not null)
			throw new NotSupportedException("RecognizeDocumentsRequest does not expose model selection.");
		if (options?.AdditionalProperties is { Count: > 0 })
			throw new NotSupportedException("Provider-specific request options are not mapped by this prototype.");

		cancellationToken.ThrowIfCancellationRequested();
		var processed = 0;
		await foreach (var source in _recognize(document, mediaType, cancellationToken).ConfigureAwait(false))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var page = AppleVisionRecognizeDocumentsMapper.ToPage(source.Snapshot, source.PageNumber);
			yield return new DocumentExtractionPageResult(page)
			{
				PagesProcessed = ++processed,
				TotalPages = source.TotalPages,
				RawRepresentation = page.RawRepresentation,
			};
		}
		cancellationToken.ThrowIfCancellationRequested();
	}

	/// <inheritdoc />
	public object? GetService(Type serviceType, object? serviceKey = null)
	{
		ArgumentNullException.ThrowIfNull(serviceType);
		if (serviceKey is not null)
			return null;
		if (serviceType == typeof(DocumentExtractionClientMetadata))
			return _metadata;
		return serviceType.IsInstanceOfType(this) ? this : null;
	}

	/// <inheritdoc />
	public void Dispose()
	{
	}
}

internal sealed record AppleVisionRecognizeDocumentsPageSnapshot(int PageNumber, int TotalPages, JsonElement Snapshot);
