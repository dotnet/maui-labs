using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI.Internal.DocumentExtraction;

namespace Microsoft.Maui.Essentials.AI;

internal static class AppleVisionDocumentMapper
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
			var page = new DocumentPage
			{
				PageNumber = pageNumber,
				Text = string.Join("\n\n", snapshot.EnumerateArray().Select(value => String(value, "transcript"))
					.Where(value => !string.IsNullOrEmpty(value))),
				RawRepresentation = snapshot.Clone(),
			};
			foreach (var source in snapshot.EnumerateArray())
				page.Observations.Add(ReadObservation(source, pageNumber));

			var properties = page.AdditionalProperties;
			properties["apple.vision.request"] = "recognize-documents";
			properties["apple.vision.revision"] = revision;
			properties["apple.readingOrderStrategy"] = "snapshot-paragraph-order";
			properties["apple.vision.observationIds"] = page.Observations.Select(value => value.Id).ToArray();
			properties["apple.vision.observationConfidences"] = page.Observations.Select(value => value.Confidence).ToArray();
			properties["apple.vision.structureTruncated"] = page.Observations.Any(value =>
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

	private static DocumentObservation ReadObservation(JsonElement source, int pageNumber)
	{
		var values = source.GetProperty("nodes");
		if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > MaximumNodes)
			throw new InvalidDataException("Vision returned invalid document nodes.");
		var observation = new DocumentObservation
		{
			Id = String(source, "uuid"),
			Text = String(source, "transcript") ?? "",
			Confidence = Number(source, "confidence"),
			AdditionalProperties = Properties(source, "nodes"),
			RawRepresentation = source.Clone(),
		};
		var byPath = new Dictionary<string, DocumentElement>(StringComparer.Ordinal);
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
			if (element is DocumentTableCell && element.Parent is not DocumentTable ||
				element is DocumentBlock { Kind: DocumentBlockKind.ListItem } &&
				element.Parent is not DocumentBlock { Kind: DocumentBlockKind.List })
				throw new InvalidDataException("Vision returned an invalid document node parent.");
			if (element is DocumentTable table)
				ValidateTable(table);
		}
		observation.ReadingOrderElements.AddRange(OrderElements(observation));
		return observation;
	}

	private static IEnumerable<DocumentElement> OrderElements(DocumentObservation observation)
	{
		var candidates = new List<DocumentElement>();
		foreach (var node in observation.Nodes)
		{
			if (node.Parent is null && node is DocumentBlock { Kind: DocumentBlockKind.Title } &&
				!string.IsNullOrWhiteSpace(node.Text))
				candidates.Add(node);
			else if (node.Parent is null && node is DocumentTable)
				candidates.Add(node);
			else if (node is DocumentBlock { Kind: DocumentBlockKind.ListItem } && IsWithinRootList(node) &&
				!string.IsNullOrWhiteSpace(node.Text) && !candidates.Any(candidate =>
					candidate is DocumentBlock { Kind: DocumentBlockKind.ListItem } && candidate.Text == node.Text &&
					Overlap(candidate, node) >= 0.85 && Overlap(node, candidate) >= 0.85))
				candidates.Add(node);
		}

		// Vision's paragraph sequence preserves columns; replace overlapping structured regions in that order.
		var emitted = new HashSet<string>(StringComparer.Ordinal);
		foreach (var paragraph in observation.Elements.OfType<DocumentBlock>()
			.Where(node => node.Kind == DocumentBlockKind.Paragraph))
		{
			var match = candidates.FindIndex(candidate => candidate switch
			{
				DocumentBlock { Kind: DocumentBlockKind.Title } =>
					paragraph.Text == candidate.Text && Overlap(paragraph, candidate) >= 0.7,
				DocumentTable => Overlap(paragraph, candidate) >= 0.7,
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
		foreach (var block in observation.Elements.OfType<DocumentBlock>()
			.Where(node => node.Kind is DocumentBlockKind.Barcode or DocumentBlockKind.Unknown))
			yield return block;
	}

	private static bool IsWithinRootList(DocumentElement node)
	{
		while (node.Parent is { } parent)
			node = parent;
		return node is DocumentBlock { Kind: DocumentBlockKind.List };
	}

	private static double Overlap(DocumentElement first, DocumentElement second) =>
		first.BoundingRegion?.Overlap(second.BoundingRegion) ?? 0;

	private static DocumentElement ReadElement(JsonElement source, int pageNumber)
	{
		var path = String(source, "path");
		if (string.IsNullOrEmpty(path))
			throw new InvalidDataException("Vision returned a node without a path.");
		var kind = String(source, "kind");
		var text = String(source, "text") ?? "";
		var region = ReadRegion(source, pageNumber);
		var confidence = Number(source, "confidence");
		var raw = source.Clone();
		var properties = Properties(source, "text", "polygon");
		return kind switch
		{
			"table" => new DocumentTable
			{
				Path = path, Text = text, BoundingRegion = region, Confidence = confidence,
				AdditionalProperties = properties, RawRepresentation = raw,
			},
			"tableCell" => new DocumentTableCell
			{
				Path = path, Text = text, BoundingRegion = region, Confidence = confidence,
				AdditionalProperties = properties, RawRepresentation = raw,
				RowIndex = source.GetProperty("rowIndex").GetInt32(),
				ColumnIndex = source.GetProperty("columnIndex").GetInt32(),
				RowSpan = source.GetProperty("rowSpan").GetInt32(),
				ColumnSpan = source.GetProperty("columnSpan").GetInt32(),
			},
			_ => new DocumentBlock
			{
				Path = path, Text = kind == "barcode" ? String(source, "payloadString") ?? text : text,
				BoundingRegion = region, Confidence = confidence, AdditionalProperties = properties, RawRepresentation = raw,
				Kind = kind switch
				{
					"title" => DocumentBlockKind.Title,
					"paragraph" => DocumentBlockKind.Paragraph,
					"list" => DocumentBlockKind.List,
					"listItem" => DocumentBlockKind.ListItem,
					"barcode" => DocumentBlockKind.Barcode,
					_ => DocumentBlockKind.Unknown,
				},
			},
		};
	}

	private static DocumentBoundingRegion? ReadRegion(JsonElement source, int pageNumber)
	{
		if (!source.TryGetProperty("polygon", out var polygon))
			return null;
		var values = polygon.EnumerateArray().Select(value => value.GetDouble()).ToArray();
		if (values.Length % 2 != 0 || values.Any(value => !double.IsFinite(value)))
			throw new InvalidDataException("Vision returned an invalid document polygon.");
		if (values.Length == 0)
			return null;
		return new DocumentBoundingRegion
		{
			PageNumber = pageNumber,
			Polygon = Enumerable.Range(0, values.Length / 2)
				.Select(index => new DocumentPoint(values[index * 2], values[index * 2 + 1])).ToArray(),
		};
	}

	private static void ValidateTable(DocumentTable table)
	{
		long rows = 0, columns = 0;
		foreach (var cell in table.Cells)
		{
			if (cell.RowIndex < 0 || cell.ColumnIndex < 0 || cell.RowSpan < 1 || cell.ColumnSpan < 1)
				throw new InvalidDataException("Vision returned an invalid table cell.");
			rows = Math.Max(rows, (long)cell.RowIndex + cell.RowSpan);
			columns = Math.Max(columns, (long)cell.ColumnIndex + cell.ColumnSpan);
		}
		if (rows > MaximumNodes || columns > MaximumNodes || rows * columns > MaximumNodes)
			throw new InvalidDataException("Vision returned invalid table dimensions.");
		table.RowCount = (int)rows;
		table.ColumnCount = (int)columns;
		var occupied = new bool[table.RowCount, table.ColumnCount];
		foreach (var cell in table.Cells)
			for (var row = cell.RowIndex; row < cell.RowIndex + cell.RowSpan; row++)
				for (var column = cell.ColumnIndex; column < cell.ColumnIndex + cell.ColumnSpan; column++)
				{
					if (occupied[row, column])
						throw new InvalidDataException("Vision returned overlapping table cells.");
					occupied[row, column] = true;
				}
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
