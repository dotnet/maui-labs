namespace Microsoft.Maui.Essentials.AI;

internal sealed class AppleVisionDocumentCapabilities
{
	internal AppleVisionDocumentCapabilities(AppleVisionRecognitionCapabilities capabilities)
	{
		RecognitionLanguages = capabilities.RecognitionLanguages;
		BarcodeSymbologies = capabilities.BarcodeSymbologies;
		Revisions = capabilities.Revisions;
	}

	internal IReadOnlyList<string> RecognitionLanguages { get; }

	internal IReadOnlyList<string> BarcodeSymbologies { get; }

	internal IReadOnlyList<int> Revisions { get; }
}
