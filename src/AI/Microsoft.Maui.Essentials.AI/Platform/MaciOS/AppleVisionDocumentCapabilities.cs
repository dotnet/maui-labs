using System.Runtime.Versioning;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>Describes the capabilities supported by Apple Vision document recognition.</summary>
[SupportedOSPlatform("ios26.0")]
[SupportedOSPlatform("maccatalyst26.0")]
[SupportedOSPlatform("macos26.0")]
public sealed class AppleVisionDocumentCapabilities
{
	internal AppleVisionDocumentCapabilities(VisionDocumentCapabilitiesNative native)
	{
		RecognitionLanguages = native.RecognitionLanguages;
		BarcodeSymbologies = native.BarcodeSymbologies;
		Revisions = [.. native.Revisions.Select(static revision => revision.Int32Value)];
	}

	/// <summary>Gets supported recognition-language identifiers.</summary>
	public IReadOnlyList<string> RecognitionLanguages { get; }

	/// <summary>Gets supported barcode symbology identifiers.</summary>
	public IReadOnlyList<string> BarcodeSymbologies { get; }

	/// <summary>Gets supported request revisions.</summary>
	public IReadOnlyList<int> Revisions { get; }
}
