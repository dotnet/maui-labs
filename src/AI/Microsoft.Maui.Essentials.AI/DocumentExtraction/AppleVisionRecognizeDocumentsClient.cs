using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>Extracts images and PDF pages using Vision's RecognizeDocumentsRequest.</summary>
[SupportedOSPlatform("ios26.0")]
[SupportedOSPlatform("maccatalyst26.0")]
[SupportedOSPlatform("macos26.0")]
public sealed partial class AppleVisionRecognizeDocumentsClient : DocumentExtractionClient
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
	public override Task<ExtractionDocument> ExtractAsync(
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
		return ExtractCoreAsync(document, identifier, mediaType, nativeOptions, cancellationToken);
	}

	private async Task<ExtractionDocument> ExtractCoreAsync(
		Stream document, string identifier, string mediaType, RecognizeDocumentsRequestOptions? options,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var result = new ExtractionDocument(identifier);
		var snapshots = new List<JsonElement>();
		await foreach (var source in _recognize(document, mediaType, options, cancellationToken).ConfigureAwait(false))
		{
			cancellationToken.ThrowIfCancellationRequested();
			AppleVisionRecognizeDocumentsMapper.AddPage(result, source.Snapshot, source.PageNumber);
			snapshots.Add(source.Snapshot.Clone());
		}
		cancellationToken.ThrowIfCancellationRequested();
		result.RawRepresentation = snapshots.ToArray();
		return result;
	}

	/// <inheritdoc />
	public override object? GetService(Type serviceType, object? serviceKey = null)
	{
		ArgumentNullException.ThrowIfNull(serviceType);
		ObjectDisposedException.ThrowIf(_disposed, this);
		return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
	}

	/// <inheritdoc />
	protected override void Dispose(bool disposing) => _disposed = true;
}
