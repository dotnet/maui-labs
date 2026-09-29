using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;

namespace Microsoft.Maui.Essentials.AI;

internal static class AppleVisionDocumentMapper
{
	private static readonly DocumentBlockKind s_listItemKind = new("listItem");
	private static readonly DocumentBlockKind s_barcodeKind = new("barcode");

	internal static DocumentPage ToPage(
		JsonElement observationsJson,
		int pageNumber,
		int? sourcePixelWidth,
		int? sourcePixelHeight,
		int revision)
	{
		if (observationsJson.ValueKind != JsonValueKind.Array)
		{
			throw new InvalidDataException("Apple Vision returned an invalid document snapshot.");
		}

		var observations = observationsJson.EnumerateArray()
			.Select(static observation => observation.Clone())
			.ToArray();
		var nodes = observations
			.SelectMany(static observation =>
				GetArray(observation, "nodes")
					.Select(node => new Node(node.Clone())))
			.ToArray();
		var children = nodes
			.Where(static node => node.ParentPath is not null)
			.GroupBy(static node => node.ParentPath!, StringComparer.Ordinal)
			.ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);

		var elements = OrderByReadingPosition(nodes
			.Where(static node => node.ParentPath is null)
			.SelectMany(node => ToElements(node, children, pageNumber)))
			.ToArray();

		var pageProperties = new AdditionalPropertiesDictionary
		{
			["apple.vision.request"] = "recognize-documents",
			["apple.vision.revision"] = revision,
			["apple.vision.observationIds"] = observations
				.Select(static observation => GetString(observation, "uuid") ?? string.Empty)
				.ToArray(),
			["apple.vision.observationConfidences"] = observations
				.Select(static observation => GetDouble(observation, "confidence") ?? 0)
				.ToArray(),
			["apple.vision.structureTruncated"] = observations
				.Any(static observation => GetBoolean(observation, "structureTruncated") == true),
			["apple.vision.projectedNodeCount"] = observations
				.Sum(static observation => GetInt64(observation, "projectedNodeCount") ?? 0),
			["apple.vision.maximumTraversalDepth"] = observations.Length == 0
				? 0L
				: observations.Max(static observation =>
					GetInt64(observation, "maximumTraversalDepth") ?? 0),
			["apple.vision.repeatedContainersPruned"] = observations
				.Sum(static observation => GetInt64(observation, "repeatedContainerCount") ?? 0),
			["apple.vision.repeatedContainerExamples"] = observations
				.Select(static observation =>
				{
					var ancestor = GetString(observation, "firstRepeatedAncestorPath");
					var repeated = GetString(observation, "firstRepeatedContainerPath");
					return ancestor is null || repeated is null ? null : $"{ancestor} -> {repeated}";
				})
				.Where(static value => value is not null)
				.ToArray()!,
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
			string.Join(
				"\n\n",
				observations
					.Select(static observation => GetString(observation, "transcript"))
					.Where(static text => !string.IsNullOrEmpty(text))))
		{
			Elements = elements,
			Dimensions = new DocumentPageDimensions(1, 1),
			CoordinateUnit = DocumentCoordinateUnit.Normalized,
			CoordinateOrigin = DocumentCoordinateOrigin.BottomLeft,
			RawRepresentation = observationsJson.Clone(),
			AdditionalProperties = pageProperties,
		};
	}

	private static IEnumerable<DocumentElement> ToElements(
		Node node,
		IReadOnlyDictionary<string, Node[]> children,
		int pageNumber) =>
		node.Kind switch
		{
			"title" => [ToBlock(node, DocumentBlockKind.Title, pageNumber)],
			"paragraph" => [ToBlock(node, DocumentBlockKind.Paragraph, pageNumber)],
			"table" => [ToTable(node, children, pageNumber)],
			"list" => ToListItems(node, children, pageNumber),
			"listItem" => ToListItemElements(node, children, pageNumber),
			"barcode" => [ToBarcodeBlock(node, pageNumber)],
			"tableCell" => throw new InvalidOperationException(
				$"Table cell '{node.Path}' was not nested under a table."),
			_ => [],
		};

	private static DocumentBlock ToBlock(
		Node node,
		DocumentBlockKind kind,
		int pageNumber) =>
		new(node.Text ?? string.Empty)
		{
			Kind = kind,
			BoundingRegion = ToBoundingRegionOrNull(pageNumber, node.Json),
			Confidence = GetDouble(node.Json, "confidence"),
			RawRepresentation = node.Json.Clone(),
			AdditionalProperties = ToAdditionalProperties(node),
		};

	private static DocumentTable ToTable(
		Node node,
		IReadOnlyDictionary<string, Node[]> children,
		int pageNumber)
	{
		var cells = GetChildren(children, node.Path)
			.Where(static child => child.Kind == "tableCell")
			.OrderBy(static child => GetInt32(child.Json, "rowIndex") ?? 0)
			.ThenBy(static child => GetInt32(child.Json, "columnIndex") ?? 0)
			.Select(child => ToCell(child, children, pageNumber))
			.ToArray();
		var rowCount = cells.Length == 0 ? 0 : cells.Max(static cell => cell.RowIndex + cell.RowSpan);
		var columnCount = cells.Length == 0 ? 0 : cells.Max(static cell => cell.ColumnIndex + cell.ColumnSpan);

		return new DocumentTable(rowCount, columnCount, cells)
		{
			BoundingRegion = ToBoundingRegionOrNull(pageNumber, node.Json),
			Confidence = GetDouble(node.Json, "confidence"),
			RawRepresentation = node.Json.Clone(),
			AdditionalProperties = ToAdditionalProperties(node),
		};
	}

	private static DocumentTableCell ToCell(
		Node node,
		IReadOnlyDictionary<string, Node[]> children,
		int pageNumber)
	{
		var nested = OrderByReadingPosition(GetChildren(children, node.Path)
			.Where(static child => child.Kind != "tableCell")
			.SelectMany(child => ToElements(child, children, pageNumber)))
			.ToArray();

		return new DocumentTableCell(
			GetInt32(node.Json, "rowIndex") ?? 0,
			GetInt32(node.Json, "columnIndex") ?? 0,
			node.Text ?? string.Empty)
		{
			RowSpan = GetInt32(node.Json, "rowSpan") ?? 1,
			ColumnSpan = GetInt32(node.Json, "columnSpan") ?? 1,
			Elements = nested.Length == 0 ? null : nested,
			BoundingRegion = ToBoundingRegionOrNull(pageNumber, node.Json),
			Confidence = GetDouble(node.Json, "confidence"),
			RawRepresentation = node.Json.Clone(),
			AdditionalProperties = ToAdditionalProperties(node),
		};
	}

	private static IEnumerable<DocumentElement> ToListItems(
		Node list,
		IReadOnlyDictionary<string, Node[]> children,
		int pageNumber) =>
		GetChildren(children, list.Path)
			.Where(static child => child.Kind == "listItem")
			.SelectMany(item => ToListItemElements(item, children, pageNumber));

	private static IEnumerable<DocumentElement> ToListItemElements(
		Node item,
		IReadOnlyDictionary<string, Node[]> children,
		int pageNumber)
	{
		yield return ToBlock(item, s_listItemKind, pageNumber);
		foreach (var child in GetChildren(children, item.Path)
			.Where(child => !IsListItemSelfProjection(item, child, children))
			.SelectMany(child => ToElements(child, children, pageNumber)))
		{
			yield return child;
		}
	}

	private static DocumentBlock ToBarcodeBlock(Node node, int pageNumber)
	{
		var symbology = GetString(node.Json, "symbology") ?? "unknown";
		var payload = GetString(node.Json, "payloadString");
		return new DocumentBlock(payload ?? string.Empty)
		{
			Kind = s_barcodeKind,
			BoundingRegion = ToBoundingRegionOrNull(pageNumber, node.Json),
			Confidence = GetDouble(node.Json, "confidence"),
			RawRepresentation = node.Json.Clone(),
			AdditionalProperties = ToAdditionalProperties(node),
		};
	}

	private static AdditionalPropertiesDictionary ToAdditionalProperties(Node node)
	{
		var properties = new AdditionalPropertiesDictionary
		{
			["apple.vision.kind"] = node.Kind,
			["apple.vision.sourcePath"] = node.Path,
		};
		if (node.ParentPath is not null)
		{
			properties["apple.vision.parentPath"] = node.ParentPath;
		}
		CopyString(node.Json, properties, "textAlignment", "apple.textAlignment");
		CopyString(node.Json, properties, "itemString", "apple.vision.itemString");
		CopyString(node.Json, properties, "markerString", "apple.vision.markerString");
		CopyString(node.Json, properties, "markerType", "apple.vision.markerType");
		CopyString(node.Json, properties, "symbology", "apple.vision.barcodeSymbology");
		CopyString(node.Json, properties, "payloadString", "apple.vision.barcodePayload");
		CopyString(node.Json, properties, "payloadDataBase64", "apple.vision.barcodePayloadBase64");
		CopyString(node.Json, properties, "supplementalPayloadString", "apple.vision.supplementalPayload");
		CopyString(node.Json, properties, "supplementalPayloadDataBase64", "apple.vision.supplementalPayloadBase64");
		CopyString(node.Json, properties, "supplementalCompositeType", "apple.vision.supplementalCompositeType");
		CopyBoolean(node.Json, properties, "isGS1DataCarrier", "apple.vision.isGs1DataCarrier");
		CopyBoolean(node.Json, properties, "isColorInverted", "apple.vision.isColorInverted");
		if (TryGetProperty(node.Json, "recognitionLanguages", out var languages) &&
			languages.ValueKind == JsonValueKind.Array)
		{
			properties["detectedLanguages"] = languages
				.EnumerateArray()
				.Select(static value => value.GetString() ?? string.Empty)
				.ToArray();
		}
		CopyJson(node.Json, properties, "detectedData", "apple.detectedData");
		CopyJson(node.Json, properties, "candidates", "apple.textCandidates");
		CopyJson(node.Json, properties, "words", "apple.textWords");
		return properties;
	}

	private static bool IsListItemSelfProjection(
		Node item,
		Node child,
		IReadOnlyDictionary<string, Node[]> children)
	{
		if (!HasEquivalentPolygon(item.Json, child.Json))
		{
			return false;
		}

		if (child.Kind == "paragraph")
		{
			return HasEquivalentListItemText(item.Json, child.Text);
		}
		if (child.Kind != "list")
		{
			return false;
		}

		var childItems = GetChildren(children, child.Path)
			.Where(static candidate => candidate.Kind == "listItem")
			.ToArray();
		return childItems.Length > 0 &&
			childItems.All(candidate =>
				HasEquivalentPolygon(item.Json, candidate.Json) &&
				string.Equals(
					GetString(candidate.Json, "itemString"),
					GetString(item.Json, "itemString"),
					StringComparison.Ordinal) &&
				string.Equals(
					GetString(candidate.Json, "markerString"),
					GetString(item.Json, "markerString"),
					StringComparison.Ordinal));
	}

	private static bool HasEquivalentListItemText(JsonElement item, string? candidateText)
	{
		var itemText = (GetString(item, "itemString") ?? GetString(item, "text") ?? string.Empty).Trim();
		var candidate = candidateText?.Trim();
		if (string.Equals(candidate, itemText, StringComparison.Ordinal))
		{
			return true;
		}

		var marker = GetString(item, "markerString")?.Trim();
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

	private static bool HasEquivalentPolygon(JsonElement left, JsonElement right)
	{
		var leftPolygon = GetArray(left, "polygon").Select(static value => value.GetDouble()).ToArray();
		var rightPolygon = GetArray(right, "polygon").Select(static value => value.GetDouble()).ToArray();
		if (leftPolygon.Length == 0 || leftPolygon.Length != rightPolygon.Length)
		{
			return false;
		}
		for (var index = 0; index < leftPolygon.Length; index++)
		{
			if (Math.Abs(leftPolygon[index] - rightPolygon[index]) > 0.00001)
			{
				return false;
			}
		}
		return true;
	}

	private static DocumentBoundingRegion? ToBoundingRegionOrNull(
		int pageNumber,
		JsonElement node)
	{
		var values = GetArray(node, "polygon").ToArray();
		if (values.Length < 4 || values.Length % 2 != 0)
		{
			return null;
		}
		var points = new DocumentPoint[values.Length / 2];
		for (var index = 0; index < points.Length; index++)
		{
			points[index] = new DocumentPoint(
				values[index * 2].GetSingle(),
				values[(index * 2) + 1].GetSingle());
		}
		return new DocumentBoundingRegion(pageNumber, points);
	}

	private static Node[] GetChildren(
		IReadOnlyDictionary<string, Node[]> children,
		string path) =>
		children.TryGetValue(path, out var result) ? result : [];

	private static DocumentElement[] OrderByReadingPosition(IEnumerable<DocumentElement> elements) =>
		[.. elements
			.Select(static (element, index) => (Element: element, Index: index, Bounds: element.BoundingRegion?.GetBounds()))
			.OrderByDescending(static item => item.Bounds?.Bottom ?? float.MinValue)
			.ThenBy(static item => item.Bounds?.Left ?? float.MaxValue)
			.ThenBy(static item => item.Index)
			.Select(static item => item.Element)];

	private static IEnumerable<JsonElement> GetArray(JsonElement element, string name) =>
		TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.Array
			? value.EnumerateArray()
			: [];

	private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
	{
		if (element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty(name, out value))
		{
			return true;
		}
		value = default;
		return false;
	}

	private static string? GetString(JsonElement element, string name) =>
		TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static bool? GetBoolean(JsonElement element, string name) =>
		TryGetProperty(element, name, out var value) &&
		value.ValueKind is JsonValueKind.True or JsonValueKind.False
			? value.GetBoolean()
			: null;

	private static int? GetInt32(JsonElement element, string name) =>
		TryGetProperty(element, name, out var value) && value.TryGetInt32(out var number)
			? number
			: null;

	private static long? GetInt64(JsonElement element, string name) =>
		TryGetProperty(element, name, out var value) && value.TryGetInt64(out var number)
			? number
			: null;

	private static double? GetDouble(JsonElement element, string name) =>
		TryGetProperty(element, name, out var value) && value.TryGetDouble(out var number)
			? number
			: null;

	private static void CopyString(
		JsonElement source,
		AdditionalPropertiesDictionary destination,
		string sourceName,
		string destinationName)
	{
		if (GetString(source, sourceName) is { } value)
		{
			destination[destinationName] = value;
		}
	}

	private static void CopyBoolean(
		JsonElement source,
		AdditionalPropertiesDictionary destination,
		string sourceName,
		string destinationName)
	{
		if (GetBoolean(source, sourceName) is { } value)
		{
			destination[destinationName] = value;
		}
	}

	private static void CopyJson(
		JsonElement source,
		AdditionalPropertiesDictionary destination,
		string sourceName,
		string destinationName)
	{
		if (TryGetProperty(source, sourceName, out var value) &&
			value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
		{
			destination[destinationName] = value.Clone();
		}
	}

	private readonly record struct Node(JsonElement Json)
	{
		internal string Kind => GetString(Json, "kind") ?? "unknown";
		internal string Path => GetString(Json, "path") ?? string.Empty;
		internal string? ParentPath => GetString(Json, "parentPath");
		internal string? Text => GetString(Json, "text");
	}
}
