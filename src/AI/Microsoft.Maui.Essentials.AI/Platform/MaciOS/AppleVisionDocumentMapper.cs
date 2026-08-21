using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;

namespace Microsoft.Maui.Essentials.AI;

internal static class AppleVisionDocumentMapper
{
	internal static DocumentPage ToPage(
		VisionDocumentResultNative result,
		int pageNumber,
		int? sourcePixelWidth,
		int? sourcePixelHeight,
		int revision)
	{
		var observations = result.Observations ?? [];
		var nodes = observations
			.SelectMany(static observation => observation.Nodes.Select(node => (Observation: observation, Node: node)))
			.ToArray();
		var children = nodes
			.Where(static pair => pair.Node.ParentPath is not null)
			.GroupBy(static pair => pair.Node.ParentPath!, StringComparer.Ordinal)
			.ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);

		var elements = OrderByReadingPosition(nodes
			.Where(static pair => pair.Node.ParentPath is null)
			.Select(pair => ToElement(pair.Observation, pair.Node, children, pageNumber)))
			.ToArray();

		var pageProperties = new AdditionalPropertiesDictionary
		{
			["apple.vision.request"] = "recognize-documents",
			["apple.vision.revision"] = revision,
			["apple.vision.observationIds"] = observations.Select(static observation => observation.UuidString).ToArray(),
			["apple.vision.observationConfidences"] = observations.Select(static observation => observation.Confidence).ToArray(),
			["apple.vision.structureTruncated"] = observations.Any(static observation => observation.StructureTruncated),
			["apple.vision.projectedNodeCount"] = observations.Sum(static observation => (long)observation.ProjectedNodeCount),
			["apple.vision.maximumTraversalDepth"] = observations.Length == 0
				? 0L
				: observations.Max(static observation => (long)observation.MaximumTraversalDepth),
			["apple.vision.repeatedContainersPruned"] = observations.Sum(static observation => (long)observation.RepeatedContainerCount),
			["apple.vision.repeatedContainerExamples"] = observations
				.Where(static observation => observation.FirstRepeatedContainerPath is not null)
				.Select(static observation => $"{observation.FirstRepeatedAncestorPath} -> {observation.FirstRepeatedContainerPath}")
				.ToArray(),
			["apple.readingOrderStrategy"] = "spatial-bottom-left",
		};
		if (sourcePixelWidth is not null)
		{
			pageProperties["apple.sourcePixelWidth"] = sourcePixelWidth.Value;
		}
		if (sourcePixelHeight is not null)
		{
			pageProperties["apple.sourcePixelHeight"] = sourcePixelHeight.Value;
		}

		return new DocumentPage(
			pageNumber,
			string.Join("\n\n", observations.Select(static observation => observation.Transcript).Where(static text => !string.IsNullOrEmpty(text))))
		{
			Elements = elements,
			Dimensions = new DocumentPageDimensions(1, 1),
			CoordinateUnit = DocumentCoordinateUnit.Normalized,
			CoordinateOrigin = DocumentCoordinateOrigin.BottomLeft,
			RawRepresentation = new AppleVisionDocumentNodeReference(observations),
			AdditionalProperties = pageProperties,
		};
	}

	internal static DocumentBoundingRegion ToBoundingRegion(int pageNumber, NSNumber[] polygon)
	{
		var points = new DocumentPoint[polygon.Length / 2];
		for (var index = 0; index < points.Length; index++)
		{
			points[index] = new DocumentPoint(
				polygon[index * 2].FloatValue,
				polygon[(index * 2) + 1].FloatValue);
		}
		return new DocumentBoundingRegion(pageNumber, points);
	}

	private static DocumentElement ToElement(
		VisionDocumentObservationNative observation,
		VisionDocumentNodeNative node,
		IReadOnlyDictionary<string, (VisionDocumentObservationNative Observation, VisionDocumentNodeNative Node)[]> children,
		int pageNumber) =>
		node.Kind switch
		{
			VisionDocumentNodeKindNative.Title => ToBlock(observation, node, DocumentBlockKind.Title, pageNumber),
			VisionDocumentNodeKindNative.Paragraph => ToBlock(observation, node, DocumentBlockKind.Paragraph, pageNumber),
			VisionDocumentNodeKindNative.Table => ToTable(observation, node, children, pageNumber),
			VisionDocumentNodeKindNative.List => ToList(observation, node, children, pageNumber),
			VisionDocumentNodeKindNative.ListItem => ToListItem(observation, node, children, pageNumber),
			VisionDocumentNodeKindNative.Barcode => ToBarcode(observation, node, pageNumber),
			VisionDocumentNodeKindNative.TableCell =>
				throw new InvalidOperationException($"Table cell '{node.Path}' was not nested under a table."),
			_ => throw new NotSupportedException($"Unsupported Apple Vision document node kind '{node.Kind}'."),
		};

	private static DocumentBlock ToBlock(
		VisionDocumentObservationNative observation,
		VisionDocumentNodeNative node,
		DocumentBlockKind kind,
		int pageNumber) =>
		new(node.Text ?? string.Empty)
		{
			Kind = kind,
			BoundingRegion = ToBoundingRegionOrNull(pageNumber, node.Polygon),
			Confidence = node.Confidence?.DoubleValue,
			RawRepresentation = new AppleVisionDocumentNodeReference(observation, node),
			AdditionalProperties = ToAdditionalProperties(node),
		};

	private static DocumentTable ToTable(
		VisionDocumentObservationNative observation,
		VisionDocumentNodeNative node,
		IReadOnlyDictionary<string, (VisionDocumentObservationNative Observation, VisionDocumentNodeNative Node)[]> children,
		int pageNumber)
	{
		var cellNodes = GetChildren(children, node.Path)
			.Where(static child => child.Node.Kind == VisionDocumentNodeKindNative.TableCell)
			.OrderBy(static child => child.Node.RowIndex?.Int32Value ?? 0)
			.ThenBy(static child => child.Node.ColumnIndex?.Int32Value ?? 0)
			.ToArray();
		var cells = cellNodes.Select(child => ToCell(child.Observation, child.Node, children, pageNumber)).ToArray();
		var rowCount = cells.Length == 0 ? 0 : cells.Max(static cell => cell.RowIndex + cell.RowSpan);
		var columnCount = cells.Length == 0 ? 0 : cells.Max(static cell => cell.ColumnIndex + cell.ColumnSpan);

		return new DocumentTable(rowCount, columnCount, cells)
		{
			BoundingRegion = ToBoundingRegionOrNull(pageNumber, node.Polygon),
			Confidence = node.Confidence?.DoubleValue,
			RawRepresentation = new AppleVisionDocumentNodeReference(observation, node),
			AdditionalProperties = ToAdditionalProperties(node),
		};
	}

	private static DocumentTableCell ToCell(
		VisionDocumentObservationNative observation,
		VisionDocumentNodeNative node,
		IReadOnlyDictionary<string, (VisionDocumentObservationNative Observation, VisionDocumentNodeNative Node)[]> children,
		int pageNumber)
	{
		var nested = OrderByReadingPosition(GetChildren(children, node.Path)
			.Where(static child => child.Node.Kind != VisionDocumentNodeKindNative.TableCell)
			.Select(child => ToElement(child.Observation, child.Node, children, pageNumber)))
			.ToArray();

		return new DocumentTableCell(
			node.RowIndex?.Int32Value ?? 0,
			node.ColumnIndex?.Int32Value ?? 0,
			node.Text ?? string.Empty)
		{
			RowSpan = node.RowSpan?.Int32Value ?? 1,
			ColumnSpan = node.ColumnSpan?.Int32Value ?? 1,
			Elements = nested.Length == 0 ? null : nested,
			BoundingRegion = ToBoundingRegionOrNull(pageNumber, node.Polygon),
			Confidence = node.Confidence?.DoubleValue,
			RawRepresentation = new AppleVisionDocumentNodeReference(observation, node),
			AdditionalProperties = ToAdditionalProperties(node),
		};
	}

	private static AppleListElement ToList(
		VisionDocumentObservationNative observation,
		VisionDocumentNodeNative node,
		IReadOnlyDictionary<string, (VisionDocumentObservationNative Observation, VisionDocumentNodeNative Node)[]> children,
		int pageNumber)
	{
		var items = GetChildren(children, node.Path)
			.Where(static child => child.Node.Kind == VisionDocumentNodeKindNative.ListItem)
			.Select(child => ToListItem(child.Observation, child.Node, children, pageNumber))
			.ToArray();

		return new AppleListElement(items)
		{
			BoundingRegion = ToBoundingRegionOrNull(pageNumber, node.Polygon),
			Confidence = node.Confidence?.DoubleValue,
			RawRepresentation = new AppleVisionDocumentNodeReference(observation, node),
			AdditionalProperties = ToAdditionalProperties(node),
		};
	}

	private static AppleListItemElement ToListItem(
		VisionDocumentObservationNative observation,
		VisionDocumentNodeNative node,
		IReadOnlyDictionary<string, (VisionDocumentObservationNative Observation, VisionDocumentNodeNative Node)[]> children,
		int pageNumber)
	{
		var nested = OrderByReadingPosition(GetChildren(children, node.Path)
			.Where(static child => child.Node.Kind != VisionDocumentNodeKindNative.ListItem)
			.Where(child => !IsListItemSelfProjection(node, child.Node, children))
			.Select(child => ToElement(child.Observation, child.Node, children, pageNumber)))
			.ToArray();

		return new AppleListItemElement(node.Text ?? string.Empty)
		{
			ItemString = node.ItemString,
			MarkerString = node.MarkerString,
			MarkerType = node.MarkerType,
			Elements = nested,
			BoundingRegion = ToBoundingRegionOrNull(pageNumber, node.Polygon),
			Confidence = node.Confidence?.DoubleValue,
			RawRepresentation = new AppleVisionDocumentNodeReference(observation, node),
			AdditionalProperties = ToAdditionalProperties(node),
		};
	}

	private static bool IsListItemSelfProjection(
		VisionDocumentNodeNative item,
		VisionDocumentNodeNative child,
		IReadOnlyDictionary<string, (VisionDocumentObservationNative Observation, VisionDocumentNodeNative Node)[]> children)
	{
		if (!HasEquivalentPolygon(item.Polygon, child.Polygon))
		{
			return false;
		}

		if (child.Kind == VisionDocumentNodeKindNative.Paragraph)
		{
			return HasEquivalentListItemText(item, child.Text);
		}

		if (child.Kind != VisionDocumentNodeKindNative.List)
		{
			return false;
		}

		var childItems = GetChildren(children, child.Path)
			.Where(static candidate => candidate.Node.Kind == VisionDocumentNodeKindNative.ListItem)
			.Select(static candidate => candidate.Node)
			.ToArray();
		return childItems.Length > 0 &&
			childItems.All(candidate =>
				HasEquivalentPolygon(item.Polygon, candidate.Polygon) &&
				string.Equals(candidate.ItemString, item.ItemString, StringComparison.Ordinal) &&
				string.Equals(candidate.MarkerString, item.MarkerString, StringComparison.Ordinal));
	}

	private static bool HasEquivalentListItemText(
		VisionDocumentNodeNative item,
		string? candidateText)
	{
		var itemText = (item.ItemString ?? item.Text ?? string.Empty).Trim();
		var candidate = candidateText?.Trim();
		if (string.Equals(candidate, itemText, StringComparison.Ordinal))
		{
			return true;
		}

		var marker = item.MarkerString?.Trim();
		if (string.IsNullOrEmpty(marker) ||
			candidate?.StartsWith(marker, StringComparison.Ordinal) != true)
		{
			return false;
		}

		return string.Equals(
			candidate[marker.Length..].TrimStart(),
			itemText,
			StringComparison.Ordinal);
	}

	private static bool HasEquivalentPolygon(NSNumber[]? left, NSNumber[]? right)
	{
		if (left is null ||
			right is null ||
			left.Length == 0 ||
			left.Length != right.Length)
		{
			return false;
		}

		for (var index = 0; index < left.Length; index++)
		{
			if (Math.Abs(left[index].DoubleValue - right[index].DoubleValue) > 0.00001)
			{
				return false;
			}
		}
		return true;
	}

	private static AppleBarcodeElement ToBarcode(
		VisionDocumentObservationNative observation,
		VisionDocumentNodeNative node,
		int pageNumber) =>
		new(node.Symbology ?? "unknown")
		{
			PayloadString = node.PayloadString,
			PayloadData = node.PayloadData?.ToArray(),
			IsGs1DataCarrier = node.IsGs1DataCarrier?.BoolValue,
			IsColorInverted = node.IsColorInverted?.BoolValue,
			SupplementalPayloadString = node.SupplementalPayloadString,
			SupplementalPayloadData = node.SupplementalPayloadData?.ToArray(),
			SupplementalCompositeType = node.SupplementalCompositeType,
			BoundingRegion = ToBoundingRegionOrNull(pageNumber, node.Polygon),
			Confidence = node.Confidence?.DoubleValue,
			RawRepresentation = new AppleVisionDocumentNodeReference(observation, node),
			AdditionalProperties = ToAdditionalProperties(node),
		};

	private static AdditionalPropertiesDictionary ToAdditionalProperties(VisionDocumentNodeNative node)
	{
		var properties = new AdditionalPropertiesDictionary
		{
			["apple.vision.sourcePath"] = node.Path,
		};
		if (node.RecognitionLanguages is { Length: > 0 })
		{
			properties["detectedLanguages"] = node.RecognitionLanguages;
		}
		if (node.TextAlignment is not null)
		{
			properties["apple.textAlignment"] = node.TextAlignment;
		}
		if (node.DetectedDataJson is not null)
		{
			properties["apple.detectedData"] = Encoding.UTF8.GetString(node.DetectedDataJson.ToArray());
		}
		if (node.CandidatesJson is not null)
		{
			properties["apple.textCandidates"] = Encoding.UTF8.GetString(node.CandidatesJson.ToArray());
		}
		return properties;
	}

	private static (VisionDocumentObservationNative Observation, VisionDocumentNodeNative Node)[] GetChildren(
		IReadOnlyDictionary<string, (VisionDocumentObservationNative Observation, VisionDocumentNodeNative Node)[]> children,
		string path) =>
		children.TryGetValue(path, out var result) ? result : [];

	private static DocumentBoundingRegion? ToBoundingRegionOrNull(int pageNumber, NSNumber[]? polygon) =>
		polygon is { Length: > 1 } ? ToBoundingRegion(pageNumber, polygon) : null;

	private static DocumentElement[] OrderByReadingPosition(IEnumerable<DocumentElement> elements) =>
		[.. elements
			.Select(static (element, index) => (Element: element, Index: index, Bounds: element.BoundingRegion?.GetBounds()))
			.OrderByDescending(static item => item.Bounds?.Bottom ?? float.MinValue)
			.ThenBy(static item => item.Bounds?.Left ?? float.MaxValue)
			.ThenBy(static item => item.Index)
			.Select(static item => item.Element)];
}
