using System.Runtime.CompilerServices;
using Microsoft.Maui.Essentials.AI.Internal.DocumentExtraction;

namespace Microsoft.Maui.Essentials.AI;

internal sealed partial class AppleVisionDocumentExtractionClient
{
	internal AppleVisionDocumentExtractionClient() : this(RecognizePagesAsync, ReadCapabilities)
	{
	}

	private static AppleVisionDocumentCapabilities ReadCapabilities()
	{
		var capabilities = new AppleVisionDocumentRecognizer().GetCapabilities();
		return new AppleVisionDocumentCapabilities
		{
			RecognitionLanguages = capabilities.RecognitionLanguages,
			BarcodeSymbologies = capabilities.BarcodeSymbologies,
			Revisions = capabilities.Revisions,
		};
	}

	private static async IAsyncEnumerable<AppleVisionSourcePage> RecognizePagesAsync(
		Stream source, string mediaType, DocumentExtractionOptions? options,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var nativeOptions = options is null ? null : new AppleVisionRecognitionOptions
		{
			RecognitionLanguages = options.RecognitionLanguages,
			CustomWords = options.CustomWords,
			UseLanguageCorrection = options.UseLanguageCorrection,
			AutomaticallyDetectLanguage = options.AutomaticallyDetectLanguage,
			MaximumCandidateCount = options.MaximumCandidateCount,
			MinimumTextHeightFraction = options.MinimumTextHeightFraction,
			BarcodeDetectionEnabled = options.BarcodeDetectionEnabled,
			BarcodeSymbologies = options.BarcodeSymbologies,
			CoalesceCompositeSymbologies = options.CoalesceCompositeSymbologies,
			RegionOfInterest = options.RegionOfInterest,
			Revision = options.Revision,
		};
		var recognizer = new AppleVisionDocumentRecognizer();
		await foreach (var page in recognizer.RecognizePagesAsync(source, mediaType, nativeOptions, cancellationToken)
			.ConfigureAwait(false))
		{
			yield return new AppleVisionSourcePage
			{
				PageNumber = page.PageNumber,
				TotalPages = page.TotalPages,
				Snapshot = page.Snapshot,
				SourcePixelWidth = page.SourcePixelWidth,
				SourcePixelHeight = page.SourcePixelHeight,
				Revision = page.Revision,
				PdfPage = page.PdfPage is not { } pdf ? null : new DocumentPdfPageInfo
				{
					Label = pdf.Label, Rotation = pdf.Rotation, DisplayBox = pdf.DisplayBox,
					RequestedDpi = pdf.RequestedDpi, EffectiveDpi = pdf.EffectiveDpi,
					WidthPoints = pdf.WidthPoints, HeightPoints = pdf.HeightPoints,
				},
			};
		}
	}
}
