using System.Runtime.Versioning;
using Microsoft.Extensions.DataIngestion;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>Reads images and PDF documents on-device using Apple Vision document recognition.</summary>
[SupportedOSPlatform("ios26.0")]
[SupportedOSPlatform("maccatalyst26.0")]
[SupportedOSPlatform("macos26.0")]
public sealed class AppleVisionDocumentReader : IngestionDocumentReader
{
	/// <inheritdoc />
	public override async Task<IngestionDocument> ReadAsync(
		Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(identifier);
		var document = new IngestionDocument(identifier);
		var recognizer = new AppleVisionDocumentRecognizer();
		await foreach (var page in recognizer.RecognizePagesAsync(source, mediaType, options: null, cancellationToken)
			.ConfigureAwait(false))
		{
			cancellationToken.ThrowIfCancellationRequested();
			document.Sections.Add(AppleVisionIngestionMapper.MapPage(page.Snapshot, page.PageNumber));
		}
		cancellationToken.ThrowIfCancellationRequested();
		return document;
	}
}
