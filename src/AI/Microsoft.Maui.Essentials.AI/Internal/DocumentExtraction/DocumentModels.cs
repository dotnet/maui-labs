using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI.Internal.DocumentExtraction;

// Temporary private contract: replace with the official extraction abstraction when it is available.
internal sealed class DocumentExtractionOptions
{
	internal string? ModelId { get; init; }
	internal string[]? RecognitionLanguages { get; init; }
	internal string[]? CustomWords { get; init; }
	internal bool? UseLanguageCorrection { get; init; }
	internal bool? AutomaticallyDetectLanguage { get; init; }
	internal int? MaximumCandidateCount { get; init; }
	internal float? MinimumTextHeightFraction { get; init; }
	internal bool? BarcodeDetectionEnabled { get; init; }
	internal string[]? BarcodeSymbologies { get; init; }
	internal bool? CoalesceCompositeSymbologies { get; init; }
	internal float[]? RegionOfInterest { get; init; }
	internal int? Revision { get; init; }
	internal AdditionalPropertiesDictionary AdditionalProperties { get; init; } = new();
}

internal sealed class DocumentExtractionResult
{
	internal string ModelId { get; init; } = "recognize-documents";
	internal List<DocumentPage> Pages { get; } = [];
	internal DocumentExtractionUsage Usage { get; } = new();
	internal AdditionalPropertiesDictionary AdditionalProperties { get; } = new();
	internal object? RawRepresentation { get; set; }
}

internal sealed class DocumentExtractionUsage
{
	internal int PagesProcessed { get; set; }
	internal int? TotalPages { get; set; }
}

internal sealed class DocumentExtractionPageResult
{
	internal required DocumentPage Page { get; init; }
	internal string ModelId { get; init; } = "recognize-documents";
	internal required DocumentExtractionUsage Usage { get; init; }
	internal AdditionalPropertiesDictionary AdditionalProperties { get; init; } = new();
	internal object? RawRepresentation { get; init; }
}

internal sealed class DocumentPage
{
	internal required int PageNumber { get; init; }
	internal string Text { get; init; } = "";
	internal DocumentPageDimensions Dimensions { get; } = new(1, 1);
	internal DocumentCoordinateUnit CoordinateUnit => DocumentCoordinateUnit.Normalized;
	internal DocumentCoordinateOrigin CoordinateOrigin => DocumentCoordinateOrigin.BottomLeft;
	internal List<DocumentObservation> Observations { get; } = [];
	internal IEnumerable<DocumentElement> Elements => Observations.SelectMany(observation => observation.ReadingOrderElements);
	internal DocumentPdfPageInfo? PdfPage { get; set; }
	internal AdditionalPropertiesDictionary AdditionalProperties { get; } = new();
	internal object? RawRepresentation { get; init; }
}

internal sealed class DocumentObservation
{
	internal string? Id { get; init; }
	internal string Text { get; init; } = "";
	internal double? Confidence { get; init; }
	internal List<DocumentElement> Elements { get; } = [];
	internal List<DocumentElement> ReadingOrderElements { get; } = [];
	// Snapshot order, including descendants, remains distinct from the retained hierarchy.
	internal List<DocumentElement> Nodes { get; } = [];
	internal AdditionalPropertiesDictionary AdditionalProperties { get; init; } = new();
	internal object? RawRepresentation { get; init; }
}

internal enum DocumentCoordinateUnit { Normalized }
internal enum DocumentCoordinateOrigin { BottomLeft }
internal enum DocumentBlockKind { Title, Paragraph, List, ListItem, Barcode, Unknown }

internal readonly struct DocumentPageDimensions(double width, double height)
{
	internal double Width { get; } = width;
	internal double Height { get; } = height;
}

internal readonly struct DocumentPoint(double x, double y)
{
	internal double X { get; } = x;
	internal double Y { get; } = y;
}

internal sealed class DocumentBoundingRegion
{
	internal required int PageNumber { get; init; }
	internal required IReadOnlyList<DocumentPoint> Polygon { get; init; }

	internal double Overlap(DocumentBoundingRegion? other)
	{
		if (other is null || Polygon.Count == 0 || other.Polygon.Count == 0)
			return 0;
		var left = Polygon.Min(point => point.X);
		var bottom = Polygon.Min(point => point.Y);
		var right = Polygon.Max(point => point.X);
		var top = Polygon.Max(point => point.Y);
		var area = (right - left) * (top - bottom);
		return area <= 0 ? 0 :
			Math.Max(0, Math.Min(right, other.Polygon.Max(point => point.X)) -
				Math.Max(left, other.Polygon.Min(point => point.X))) *
			Math.Max(0, Math.Min(top, other.Polygon.Max(point => point.Y)) -
				Math.Max(bottom, other.Polygon.Min(point => point.Y))) / area;
	}
}

internal abstract class DocumentElement
{
	internal required string Path { get; init; }
	internal string Text { get; init; } = "";
	internal DocumentElement? Parent { get; set; }
	internal List<DocumentElement> Elements { get; } = [];
	internal DocumentBoundingRegion? BoundingRegion { get; init; }
	internal double? Confidence { get; init; }
	internal AdditionalPropertiesDictionary AdditionalProperties { get; init; } = new();
	internal object? RawRepresentation { get; init; }
}

internal sealed class DocumentBlock : DocumentElement
{
	internal required DocumentBlockKind Kind { get; init; }
}

internal sealed class DocumentTable : DocumentElement
{
	internal int RowCount { get; set; }
	internal int ColumnCount { get; set; }
	internal IEnumerable<DocumentTableCell> Cells => Elements.OfType<DocumentTableCell>();
}

internal sealed class DocumentTableCell : DocumentElement
{
	internal required int RowIndex { get; init; }
	internal required int ColumnIndex { get; init; }
	internal required int RowSpan { get; init; }
	internal required int ColumnSpan { get; init; }
}

internal sealed class DocumentPdfPageInfo
{
	internal string? Label { get; init; }
	internal required int Rotation { get; init; }
	internal required string DisplayBox { get; init; }
	internal required double RequestedDpi { get; init; }
	internal required double EffectiveDpi { get; init; }
	internal required double WidthPoints { get; init; }
	internal required double HeightPoints { get; init; }
}
