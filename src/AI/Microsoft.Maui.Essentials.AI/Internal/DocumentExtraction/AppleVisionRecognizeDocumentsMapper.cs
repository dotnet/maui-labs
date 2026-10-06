using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI.Internal.DocumentExtraction;

namespace Microsoft.Maui.Essentials.AI;

internal static class AppleVisionRecognizeDocumentsMapper
{
	private const int MaximumNodes = 20_000;
	private const int MaximumDepth = 64;

	internal static DocumentPage ToPage(
		JsonElement snapshot, int pageNumber, int? sourcePixelWidth = null, int? sourcePixelHeight = null, int revision = 1)
	{
		try
		{
			if (snapshot.ValueKind != JsonValueKind.Array)
				throw new InvalidDataException("Vision returned an invalid document snapshot.");
			var page = new DocumentPage(pageNumber,
				string.Join("\n\n", snapshot.EnumerateArray().Select(value => String(value, "transcript"))
					.Where(value => !string.IsNullOrEmpty(value))))
			{
				Dimensions = new(1, 1),
				CoordinateUnit = DocumentCoordinateUnit.Normalized,
				CoordinateOrigin = DocumentCoordinateOrigin.BottomLeft,
				RawRepresentation = snapshot.Clone(),
				AdditionalProperties = new(),
			};
			var observations = snapshot.EnumerateArray().Select(source => ReadObservation(source, pageNumber)).ToArray();
			page.Elements = observations.SelectMany(observation => observation.ReadingOrderElements)
				.Select(node => node.Element!).ToArray();

			var properties = page.AdditionalProperties;
			properties["apple.vision.observations"] = observations;
			properties["apple.vision.request"] = "recognize-documents";
			properties["apple.vision.revision"] = revision;
			properties["apple.readingOrderStrategy"] = "snapshot-paragraph-order";
			properties["apple.vision.observationIds"] = observations.Select(value => value.Id).ToArray();
			properties["apple.vision.observationConfidences"] = observations.Select(value => value.Confidence).ToArray();
			properties["apple.vision.structureTruncated"] = observations.Any(value =>
				value.AdditionalProperties.TryGetValue("structureTruncated", out var flag) && flag is true);
			if (sourcePixelWidth is { } width) properties["apple.sourcePixelWidth"] = width;
			if (sourcePixelHeight is { } height) properties["apple.sourcePixelHeight"] = height;
			return page;
		}
		catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or
			FormatException or OverflowException or ArgumentException)
		{
			throw new InvalidDataException("Vision returned an invalid document snapshot.", exception);
		}
	}

	private static AppleVisionRecognizeDocumentsObservationSnapshot ReadObservation(JsonElement source, int pageNumber)
	{
		var values = source.GetProperty("nodes");
		if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > MaximumNodes)
			throw new InvalidDataException("Vision returned invalid document nodes.");
		var observation = new AppleVisionRecognizeDocumentsObservationSnapshot
		{
			Id = String(source, "uuid"),
			Text = String(source, "transcript") ?? "",
			Confidence = Number(source, "confidence"),
			AdditionalProperties = Properties(source, "nodes"),
			RawRepresentation = source.Clone(),
		};
		var byPath = new Dictionary<string, AppleVisionRecognizeDocumentsNodeSnapshot>(StringComparer.Ordinal);
		var parents = new Dictionary<string, string?>(StringComparer.Ordinal);
		foreach (var value in values.EnumerateArray())
		{
			var element = ReadElement(value, pageNumber);
			if (!byPath.TryAdd(element.Path, element))
				throw new InvalidDataException("Vision returned duplicate document paths.");
			parents.Add(element.Path, String(value, "parentPath"));
			observation.Nodes.Add(element);
		}
		foreach (var element in observation.Nodes)
		{
			var visited = new HashSet<string>(StringComparer.Ordinal) { element.Path };
			var path = element.Path;
			var depth = 0;
			while (parents[path] is { } parent)
			{
				if (++depth > MaximumDepth || !visited.Add(parent) || !byPath.ContainsKey(parent))
					throw new InvalidDataException("Vision returned an invalid document node hierarchy.");
				path = parent;
			}
			if (parents[element.Path] is { } parentPath)
			{
				element.Parent = byPath[parentPath];
				element.Parent.Elements.Add(element);
			}
			else
				observation.Elements.Add(element);
		}
		foreach (var element in observation.Nodes)
		{
			if (element.Kind == "tableCell" && element.Parent?.Kind != "table" ||
				element.Kind == "listItem" && element.Parent?.Kind != "list")
				throw new InvalidDataException("Vision returned an invalid document node parent.");
		}
		foreach (var root in observation.Elements)
			BuildElement(root);
		observation.ReadingOrderElements.AddRange(OrderElements(observation));
		return observation;
	}

	private static IEnumerable<AppleVisionRecognizeDocumentsNodeSnapshot> OrderElements(
		AppleVisionRecognizeDocumentsObservationSnapshot observation)
	{
		var candidates = new List<AppleVisionRecognizeDocumentsNodeSnapshot>();
		foreach (var node in observation.Nodes)
		{
			if (node.Parent is null && node.Kind == "title" &&
				!string.IsNullOrWhiteSpace(node.Text))
				candidates.Add(node);
			else if (node.Parent is null && node.Kind == "table")
				candidates.Add(node);
			else if (node.Kind == "listItem" && IsWithinRootList(node) &&
				!string.IsNullOrWhiteSpace(node.Text) && !candidates.Any(candidate =>
					candidate.Kind == "listItem" && candidate.Text == node.Text &&
					Overlap(candidate, node) >= 0.85 && Overlap(node, candidate) >= 0.85))
				candidates.Add(node);
		}

		// Vision's paragraph sequence preserves columns; replace overlapping structured regions in that order.
		var emitted = new HashSet<string>(StringComparer.Ordinal);
		foreach (var paragraph in observation.Elements.Where(node => node.Kind == "paragraph"))
		{
			var match = candidates.FindIndex(candidate => candidate.Kind switch
			{
				"title" =>
					paragraph.Text == candidate.Text && Overlap(paragraph, candidate) >= 0.7,
				"table" => Overlap(paragraph, candidate) >= 0.7,
				_ => Overlap(paragraph, candidate) >= 0.55 &&
					paragraph.Text.Contains(candidate.Text, StringComparison.OrdinalIgnoreCase),
			});
			if (match >= 0)
			{
				var candidate = candidates[match];
				if (emitted.Add(candidate.Path))
					yield return candidate;
			}
			else if (!string.IsNullOrWhiteSpace(paragraph.Text))
				yield return paragraph;
		}
		foreach (var candidate in candidates)
			if (emitted.Add(candidate.Path))
				yield return candidate;
		foreach (var block in observation.Elements.Where(node =>
			node.Kind is not ("title" or "paragraph" or "table" or "tableCell" or "list" or "listItem")))
			yield return block;
	}

	private static bool IsWithinRootList(AppleVisionRecognizeDocumentsNodeSnapshot node)
	{
		while (node.Parent is { } parent)
			node = parent;
		return node.Kind == "list";
	}

	private static double Overlap(AppleVisionRecognizeDocumentsNodeSnapshot first, AppleVisionRecognizeDocumentsNodeSnapshot second) =>
		first.Bounds?.Overlap(second.Bounds) ?? 0;

	private static AppleVisionRecognizeDocumentsNodeSnapshot ReadElement(JsonElement source, int pageNumber)
	{
		var path = String(source, "path");
		if (string.IsNullOrEmpty(path))
			throw new InvalidDataException("Vision returned a node without a path.");
		var kind = String(source, "kind") ?? "unknown";
		var text = (kind == "barcode" ? String(source, "payloadString") : null) ?? String(source, "text") ?? "";
		var (region, bounds) = ReadRegion(source, pageNumber);
		var confidence = Number(source, "confidence");
		var raw = source.Clone();
		var properties = Properties(source, "text", "polygon");
		var node = new AppleVisionRecognizeDocumentsNodeSnapshot
		{
			Path = path, Kind = kind, Text = text, BoundingRegion = region, Bounds = bounds,
			Confidence = confidence, AdditionalProperties = properties, RawRepresentation = raw,
			Cell = kind != "tableCell" ? null : new DocumentTableCell(
				source.GetProperty("rowIndex").GetInt32(), source.GetProperty("columnIndex").GetInt32(), text)
			{
				BoundingRegion = region, Confidence = confidence,
				AdditionalProperties = properties, RawRepresentation = raw,
				RowSpan = source.GetProperty("rowSpan").GetInt32(),
				ColumnSpan = source.GetProperty("columnSpan").GetInt32(),
			},
		};
		properties["apple.vision.node"] = node;
		return node;
	}

	private static void BuildElement(AppleVisionRecognizeDocumentsNodeSnapshot node)
	{
		foreach (var child in node.Elements)
			BuildElement(child);
		var children = node.Elements.Where(child => child.Element is not null).Select(child => child.Element!).ToArray();
		node.AdditionalProperties["apple.vision.children"] = children;
		if (node.Cell is { } cell)
			cell.Elements = children;
		else
		{
			node.Element = node.Kind == "table" ? BuildTable(node) : new DocumentBlock(node.Text)
			{
				Kind = new(node.Kind),
			};
			node.Element.BoundingRegion = node.BoundingRegion;
			node.Element.Confidence = node.Confidence;
			node.Element.AdditionalProperties = node.AdditionalProperties;
			node.Element.RawRepresentation = node.RawRepresentation;
		}
	}

	private static (DocumentBoundingRegion?, AppleVisionRecognizeDocumentsBounds?) ReadRegion(JsonElement source, int pageNumber)
	{
		if (!source.TryGetProperty("polygon", out var polygon))
			return (null, null);
		var values = polygon.EnumerateArray().Select(value => value.GetDouble()).ToArray();
		if (values.Length % 2 != 0 || values.Any(value => !double.IsFinite(value)))
			throw new InvalidDataException("Vision returned an invalid document polygon.");
		if (values.Length == 0)
			return (null, null);
		var points = new DocumentPoint[values.Length / 2];
		double left = double.MaxValue, bottom = double.MaxValue, right = double.MinValue, top = double.MinValue;
		for (var index = 0; index < points.Length; index++)
		{
			var x = values[index * 2];
			var y = values[index * 2 + 1];
			if (!float.IsFinite((float)x) || !float.IsFinite((float)y))
				throw new InvalidDataException("Vision returned an invalid document polygon.");
			points[index] = new((float)x, (float)y);
			left = Math.Min(left, x);
			right = Math.Max(right, x);
			bottom = Math.Min(bottom, y);
			top = Math.Max(top, y);
		}
		return (new(pageNumber, points), new(left, bottom, right, top));
	}

	private static DocumentTable BuildTable(AppleVisionRecognizeDocumentsNodeSnapshot node)
	{
		var cells = node.Elements.Where(child => child.Cell is not null).Select(child => child.Cell!).ToArray();
		long rows = 0, columns = 0;
		foreach (var cell in cells)
		{
			if (cell.RowIndex < 0 || cell.ColumnIndex < 0 || cell.RowSpan < 1 || cell.ColumnSpan < 1)
				throw new InvalidDataException("Vision returned an invalid table cell.");
			rows = Math.Max(rows, (long)cell.RowIndex + cell.RowSpan);
			columns = Math.Max(columns, (long)cell.ColumnIndex + cell.ColumnSpan);
		}
		if (rows > MaximumNodes || columns > MaximumNodes || rows * columns > MaximumNodes)
			throw new InvalidDataException("Vision returned invalid table dimensions.");
		var occupied = new bool[(int)rows, (int)columns];
		foreach (var cell in cells)
			for (var row = cell.RowIndex; row < cell.RowIndex + cell.RowSpan; row++)
				for (var column = cell.ColumnIndex; column < cell.ColumnIndex + cell.ColumnSpan; column++)
				{
					if (occupied[row, column])
						throw new InvalidDataException("Vision returned overlapping table cells.");
					occupied[row, column] = true;
				}
		return new((int)rows, (int)columns, cells);
	}

	private static AdditionalPropertiesDictionary Properties(JsonElement source, params string[] excluded)
	{
		var properties = new AdditionalPropertiesDictionary();
		foreach (var property in source.EnumerateObject())
		{
			if (excluded.Contains(property.Name, StringComparer.Ordinal))
				continue;
			properties[property.Name] = property.Value.ValueKind switch
			{
				JsonValueKind.String => property.Value.GetString(),
				JsonValueKind.True => true,
				JsonValueKind.False => false,
				JsonValueKind.Number => property.Value.GetDouble(),
				JsonValueKind.Null => null,
				_ => property.Value.Clone(),
			};
		}
		return properties;
	}

	private static string? String(JsonElement source, string name) =>
		source.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

	private static double? Number(JsonElement source, string name)
	{
		if (!source.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
			return null;
		var number = value.GetDouble();
		if (!double.IsFinite(number))
			throw new InvalidDataException("Vision returned a nonfinite document value.");
		return number;
	}
}
