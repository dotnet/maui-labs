namespace Microsoft.Maui.Essentials.AI;

internal sealed class AppleVisionDocumentCapabilities
{
	internal AppleVisionDocumentCapabilities(VisionDocumentCapabilitiesNative native)
	{
		RecognitionLanguages = native.RecognitionLanguages;
		BarcodeSymbologies = native.BarcodeSymbologies;
		Revisions = [.. native.Revisions.Select(static revision => revision.Int32Value)];
	}

	internal IReadOnlyList<string> RecognitionLanguages { get; }

	internal IReadOnlyList<string> BarcodeSymbologies { get; }

	internal IReadOnlyList<int> Revisions { get; }
}
