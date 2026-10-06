using System.Runtime.CompilerServices;

namespace Microsoft.Maui.Essentials.AI;

public sealed partial class AppleVisionRecognizeDocumentsClient
{
	/// <summary>Initializes an on-device RecognizeDocumentsRequest client.</summary>
	public AppleVisionRecognizeDocumentsClient() : this(RecognizePagesAsync)
	{
	}

	private static async IAsyncEnumerable<AppleVisionRecognizeDocumentsPageSnapshot> RecognizePagesAsync(
		Stream source,
		string mediaType,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var processor = new AppleVisionRecognizeDocumentsProcessor();
		await foreach (var page in processor.RecognizePagesAsync(source, mediaType, cancellationToken).ConfigureAwait(false))
			yield return new AppleVisionRecognizeDocumentsPageSnapshot(page.PageNumber, page.TotalPages, page.Snapshot);
	}
}
