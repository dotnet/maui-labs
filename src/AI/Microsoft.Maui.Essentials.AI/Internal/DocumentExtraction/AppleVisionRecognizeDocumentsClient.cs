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
	private bool _disposed;

	internal AppleVisionRecognizeDocumentsClient(
		Func<Stream, string, CancellationToken, IAsyncEnumerable<AppleVisionRecognizeDocumentsPageSnapshot>> recognize)
	{
		_recognize = recognize ?? throw new ArgumentNullException(nameof(recognize));
	}

	/// <inheritdoc />
	public async IAsyncEnumerable<DocumentExtractionPage> ExtractPagesAsync(
		Stream document,
		string identifier,
		string mediaType,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
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
		cancellationToken.ThrowIfCancellationRequested();
		await foreach (var source in _recognize(document, mediaType, cancellationToken).ConfigureAwait(false))
		{
			cancellationToken.ThrowIfCancellationRequested();
			yield return AppleVisionRecognizeDocumentsMapper.ToPage(source.Snapshot, source.PageNumber);
		}
		cancellationToken.ThrowIfCancellationRequested();
	}

	/// <inheritdoc />
	public object? GetService(Type serviceType, object? serviceKey = null)
	{
		ArgumentNullException.ThrowIfNull(serviceType);
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (serviceKey is not null)
			return null;
		return serviceType.IsInstanceOfType(this) ? this : null;
	}

	/// <inheritdoc />
	public void Dispose() => _disposed = true;
}

internal sealed record AppleVisionRecognizeDocumentsPageSnapshot(int PageNumber, int TotalPages, JsonElement Snapshot);
