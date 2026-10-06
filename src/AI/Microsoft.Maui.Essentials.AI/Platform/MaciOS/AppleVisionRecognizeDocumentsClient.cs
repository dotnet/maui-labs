using System.Runtime.CompilerServices;

namespace Microsoft.Maui.Essentials.AI;

internal sealed partial class AppleVisionRecognizeDocumentsClient
{
	internal AppleVisionRecognizeDocumentsClient() : this(RecognizePagesAsync, ReadCapabilities)
	{
	}

	private static AppleVisionRecognizeDocumentsCapabilities ReadCapabilities()
	{
		var capabilities = new AppleVisionRecognizeDocumentsProcessor().GetCapabilities();
		return new AppleVisionRecognizeDocumentsCapabilities
		{
			SupportedRecognitionLanguages = capabilities.SupportedRecognitionLanguages,
			SupportedBarcodeSymbologies = capabilities.SupportedBarcodeSymbologies,
			SupportedRevisions = capabilities.SupportedRevisions,
		};
	}

	private static async IAsyncEnumerable<AppleVisionRecognizeDocumentsPageSnapshot> RecognizePagesAsync(
		Stream source, string mediaType, RecognizeDocumentsRequestOptions? options,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var recognizer = new AppleVisionRecognizeDocumentsProcessor();
		await foreach (var page in recognizer.RecognizePagesAsync(source, mediaType, options, cancellationToken)
			.ConfigureAwait(false))
		{
			yield return new AppleVisionRecognizeDocumentsPageSnapshot
			{
				PageNumber = page.PageNumber,
				TotalPages = page.TotalPages,
				Snapshot = page.Snapshot,
				SourcePixelWidth = page.SourcePixelWidth,
				SourcePixelHeight = page.SourcePixelHeight,
				Revision = page.Revision,
				PdfPage = page.PdfPage is not { } pdf ? null : new AppleVisionRecognizeDocumentsPdfPageInfo
				{
					Label = pdf.Label, Rotation = pdf.Rotation, DisplayBox = pdf.DisplayBox,
					RequestedDpi = pdf.RequestedDpi, EffectiveDpi = pdf.EffectiveDpi,
					WidthPoints = pdf.WidthPoints, HeightPoints = pdf.HeightPoints,
				},
			};
		}
	}
}
