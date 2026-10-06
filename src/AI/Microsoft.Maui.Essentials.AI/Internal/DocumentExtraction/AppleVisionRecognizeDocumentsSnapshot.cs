using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI.Internal.DocumentExtraction;

namespace Microsoft.Maui.Essentials.AI;

internal sealed class AppleVisionRecognizeDocumentsObservationSnapshot
{
	internal string? Id { get; init; }
	internal string Text { get; init; } = "";
	internal double? Confidence { get; init; }
	internal List<AppleVisionRecognizeDocumentsNodeSnapshot> Elements { get; } = [];
	internal List<AppleVisionRecognizeDocumentsNodeSnapshot> ReadingOrderElements { get; } = [];
	internal List<AppleVisionRecognizeDocumentsNodeSnapshot> Nodes { get; } = [];
	internal AdditionalPropertiesDictionary AdditionalProperties { get; init; } = new();
	internal object? RawRepresentation { get; init; }
}

internal sealed class AppleVisionRecognizeDocumentsNodeSnapshot
{
	internal required string Path { get; init; }
	internal required string Kind { get; init; }
	internal string Text { get; init; } = "";
	internal AppleVisionRecognizeDocumentsNodeSnapshot? Parent { get; set; }
	internal List<AppleVisionRecognizeDocumentsNodeSnapshot> Elements { get; } = [];
	internal DocumentElement? Element { get; set; }
	internal DocumentTableCell? Cell { get; init; }
	internal DocumentBoundingRegion? BoundingRegion { get; init; }
	internal AppleVisionRecognizeDocumentsBounds? Bounds { get; init; }
	internal double? Confidence { get; init; }
	internal AdditionalPropertiesDictionary AdditionalProperties { get; init; } = new();
	internal object? RawRepresentation { get; init; }
}

// Retain native precision for the existing canonical .7/.55/.85 overlap decisions.
internal readonly record struct AppleVisionRecognizeDocumentsBounds(double Left, double Bottom, double Right, double Top)
{
	internal double Overlap(AppleVisionRecognizeDocumentsBounds? other)
	{
		if (other is not { } bounds)
			return 0;
		var area = (Right - Left) * (Top - Bottom);
		return area <= 0 ? 0 :
			Math.Max(0, Math.Min(Right, bounds.Right) - Math.Max(Left, bounds.Left)) *
			Math.Max(0, Math.Min(Top, bounds.Top) - Math.Max(Bottom, bounds.Bottom)) / area;
	}
}

internal sealed class AppleVisionRecognizeDocumentsPdfPageInfo
{
	internal string? Label { get; init; }
	internal required int Rotation { get; init; }
	internal required string DisplayBox { get; init; }
	internal required double RequestedDpi { get; init; }
	internal required double EffectiveDpi { get; init; }
	internal required double WidthPoints { get; init; }
	internal required double HeightPoints { get; init; }
}
