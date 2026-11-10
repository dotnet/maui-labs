using System.Runtime.Versioning;
using Microsoft.Extensions.DataIngestion;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>Reads images and PDF documents on-device using Apple Vision document recognition.</summary>
[SupportedOSPlatform("ios26.0")]
[SupportedOSPlatform("maccatalyst26.0")]
[SupportedOSPlatform("macos26.0")]
public sealed class AppleVisionRecognizeDocumentsReader : IngestionDocumentReader
{
	/// <inheritdoc />
	public override async Task<IngestionDocument> ReadAsync(
		Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(identifier);
		using var client = new AppleVisionRecognizeDocumentsClient();
		var result = await client.ExtractAsync(source, identifier, mediaType,
			cancellationToken: cancellationToken).ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();
		return AppleVisionRecognizeDocumentsIngestionMapper.MapDocument(result);
	}
}
