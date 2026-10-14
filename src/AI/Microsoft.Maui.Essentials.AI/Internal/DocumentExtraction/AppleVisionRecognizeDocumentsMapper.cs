using System.Text.Json;
using Microsoft.Extensions.DocumentExtraction;

namespace Microsoft.Maui.Essentials.AI;

internal static class AppleVisionRecognizeDocumentsMapper
{
	private const int MaximumNodes = 20_000;
	private const int MaximumDepth = 64;

	internal static DocumentPage ToPage(JsonElement snapshot, int pageNumber)
	{
		if (snapshot.ValueKind != JsonValueKind.Array)
			throw new InvalidDataException("Vision returned an invalid document snapshot.");

		var elements = new List<DocumentElement>();
		var transcripts = new List<string>();
		foreach (var observation in snapshot.EnumerateArray())
		{
			var transcript = GetString(observation, "transcript");
			if (!string.IsNullOrEmpty(transcript))
				transcripts.Add(transcript);
			elements.AddRange(ReadObservation(observation, pageNumber));
		}

		return new DocumentPage(pageNumber, string.Join("\n\n", transcripts))
		{
			Elements = elements,
			Dimensions = new DocumentPageDimensions(1, 1),
			CoordinateUnit = DocumentCoordinateUnit.Normalized,
			CoordinateOrigin = DocumentCoordinateOrigin.BottomLeft,
			RawRepresentation = snapshot.Clone(),
		};
	}

	private static IReadOnlyList<DocumentElement> ReadObservation(JsonElement observation, int pageNumber)
	{
		var values = observation.GetProperty("nodes");
		if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > MaximumNodes)
			throw new InvalidDataException("Vision returned invalid document nodes.");

		var nodes = values.EnumerateArray().Select(value => new Node(value, pageNumber)).ToArray();
		var byPath = new Dictionary<string, Node>(StringComparer.Ordinal);
		foreach (var node in nodes)
			if (!byPath.TryAdd(node.Path, node))
				throw new InvalidDataException("Vision returned duplicate document paths.");

		foreach (var node in nodes)
		{
			var visited = new HashSet<string>(StringComparer.Ordinal) { node.Path };
			var current = node;
			for (var depth = 0; current.ParentPath is { } parentPath; depth++)
			{
				if (depth >= MaximumDepth || !visited.Add(parentPath) || !byPath.TryGetValue(parentPath, out var parent))
					throw new InvalidDataException("Vision returned an invalid document node hierarchy.");
				current = parent;
			}
			if (node.ParentPath is { } path)
				byPath[path].Children.Add(node);
			if (node.Kind == "tableCell" && (node.ParentPath is null || byPath[node.ParentPath].Kind != "table") ||
				node.Kind == "listItem" && (node.ParentPath is null || byPath[node.ParentPath].Kind != "list"))
				throw new InvalidDataException("Vision returned an invalid document node parent.");
		}

		var roots = nodes.Where(node => node.ParentPath is null).ToArray();
		foreach (var root in roots)
			BuildElement(root, pageNumber);

		// Vision supplies paragraph order. Replace paragraphs inside titles/tables, without spatially sorting columns.
		var structured = roots.Where(node => node.Element is DocumentTable ||
			node.Element is DocumentBlock { Kind: var kind } && kind == DocumentBlockKind.Title).ToArray();
		var emitted = new HashSet<string>(StringComparer.Ordinal);
		var result = new List<DocumentElement>();
		foreach (var paragraph in roots.Where(node => node.Kind == "paragraph" && node.Element is not null))
		{
			var match = structured.FirstOrDefault(candidate => candidate.Kind == "title"
				? paragraph.Text == candidate.Text && Overlap(paragraph, candidate) >= 0.7
				: Overlap(paragraph, candidate) >= 0.7);
			var selected = match ?? paragraph;
			if (emitted.Add(selected.Path))
				result.Add(selected.Element!);
		}
		foreach (var candidate in structured)
			if (emitted.Add(candidate.Path))
				result.Add(candidate.Element!);
		return result;
	}

	private static void BuildElement(Node node, int pageNumber)
	{
		foreach (var child in node.Children)
			BuildElement(child, pageNumber);

		node.Element = node.Kind switch
		{
			"title" when !string.IsNullOrWhiteSpace(node.Text) => new DocumentBlock(node.Text) { Kind = DocumentBlockKind.Title },
			"paragraph" when !string.IsNullOrWhiteSpace(node.Text) => new DocumentBlock(node.Text) { Kind = DocumentBlockKind.Paragraph },
			"table" => ReadTable(node, pageNumber),
			_ => null,
		};
		if (node.Element is { } element)
		{
			element.BoundingRegion = node.Region;
			element.Confidence = node.Confidence;
			element.RawRepresentation = node.Json.Clone();
		}
	}

	private static DocumentTable? ReadTable(Node table, int pageNumber)
	{
		var cells = table.Children.Where(child => child.Kind == "tableCell").Select(child =>
		{
			var row = child.Json.GetProperty("rowIndex").GetInt32();
			var column = child.Json.GetProperty("columnIndex").GetInt32();
			var rowSpan = child.Json.GetProperty("rowSpan").GetInt32();
			var columnSpan = child.Json.GetProperty("columnSpan").GetInt32();
			if (row < 0 || column < 0 || rowSpan < 1 || columnSpan < 1)
				throw new InvalidDataException("Vision returned an invalid table cell.");
			var nested = child.Children.Where(node => node.Element is not null).Select(node => node.Element!).ToArray();
			return new DocumentTableCell(row, column, child.Text)
			{
				RowSpan = rowSpan,
				ColumnSpan = columnSpan,
				BoundingRegion = child.Region,
				Confidence = child.Confidence,
				Elements = nested.Length == 0 ? null : nested,
				RawRepresentation = child.Json.Clone(),
			};
		}).ToArray();
		if (cells.Length == 0)
			return null;

		var rows = cells.Length == 0 ? 0 : cells.Max(cell => (long)cell.RowIndex + cell.RowSpan);
		var columns = cells.Length == 0 ? 0 : cells.Max(cell => (long)cell.ColumnIndex + cell.ColumnSpan);
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
		return new DocumentTable((int)rows, (int)columns, cells);
	}

	private static double Overlap(Node first, Node second)
	{
		if (first.Bounds is not { } a || second.Bounds is not { } b)
			return 0;
		var area = (a.Right - a.Left) * (a.Top - a.Bottom);
		return area <= 0 ? 0 : Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left)) *
			Math.Max(0, Math.Min(a.Top, b.Top) - Math.Max(a.Bottom, b.Bottom)) / area;
	}

	private static string? GetString(JsonElement value, string name) =>
		value.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property.GetString() : null;

	private sealed class Node
	{
		internal Node(JsonElement json, int pageNumber)
		{
			Json = json;
			Path = GetString(json, "path") ?? throw new InvalidDataException("Vision returned a node without a path.");
			ParentPath = GetString(json, "parentPath");
			Kind = GetString(json, "kind") ?? "";
			Text = GetString(json, "text") ?? "";
			if (json.TryGetProperty("confidence", out var confidence) && confidence.ValueKind != JsonValueKind.Null)
			{
				Confidence = confidence.GetDouble();
				if (!double.IsFinite(Confidence.Value) || Confidence is < 0 or > 1)
					throw new InvalidDataException("Vision returned invalid confidence.");
			}
			if (!json.TryGetProperty("polygon", out var polygon))
				return;
			var coordinates = polygon.EnumerateArray().Select(value => value.GetDouble()).ToArray();
			if (coordinates.Length == 0)
				return;
			if (coordinates.Length < 6 || coordinates.Length % 2 != 0 ||
				coordinates.Any(value => !double.IsFinite(value) || !float.IsFinite((float)value)))
				throw new InvalidDataException("Vision returned an invalid normalized polygon.");
			var points = new DocumentPoint[coordinates.Length / 2];
			for (var index = 0; index < points.Length; index++)
				points[index] = new DocumentPoint((float)coordinates[index * 2], (float)coordinates[index * 2 + 1]);
			Region = new DocumentBoundingRegion(pageNumber, points);
			var xs = coordinates.Where((_, index) => index % 2 == 0).ToArray();
			var ys = coordinates.Where((_, index) => index % 2 == 1).ToArray();
			Bounds = (xs.Min(), ys.Min(), xs.Max(), ys.Max());
		}

		internal JsonElement Json { get; }
		internal string Path { get; }
		internal string? ParentPath { get; }
		internal string Kind { get; }
		internal string Text { get; }
		internal double? Confidence { get; }
		internal DocumentBoundingRegion? Region { get; }
		internal (double Left, double Bottom, double Right, double Top)? Bounds { get; }
		internal List<Node> Children { get; } = [];
		internal DocumentElement? Element { get; set; }
	}
}
