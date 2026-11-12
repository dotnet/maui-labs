using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;

namespace Microsoft.Maui.Essentials.AI;

internal static class AppleVisionRecognizeDocumentsMapper
{
	private const int MaximumNodes = 20_000;
	private const int MaximumDepth = 64;

	internal static void AddPage(ExtractionDocument document, JsonElement snapshot, int pageNumber)
	{
		if (document.SourcePages.Any(page => page.PageNumber == pageNumber))
			throw new InvalidDataException($"Vision returned duplicate source page number '{pageNumber}'.");
		if (snapshot.ValueKind != JsonValueKind.Array)
			throw new InvalidDataException("Vision returned an invalid document snapshot.");
		var frame = new ExtractionDocumentSourcePage($"{document.Identifier}/page/{pageNumber}", pageNumber,
			new(1, 1), ExtractionDocumentCoordinateUnit.Normalized, ExtractionDocumentCoordinateOrigin.BottomLeft);
		var section = new ExtractionDocumentSection();
		section.Sources.Add(new(frame, rawRepresentation: snapshot.Clone()));
		var observationIds = new HashSet<string>(StringComparer.Ordinal);
		var observationIndex = 0;
		foreach (var observation in snapshot.EnumerateArray())
		{
			var identity = GetString(observation, "uuid") ?? $"snapshot-observation-{observationIndex}";
			observationIndex++;
			if (!observationIds.Add(identity))
				throw new InvalidDataException("Vision returned duplicate observation identities.");
			var transcript = GetString(observation, "transcript");
			ValidateConfidence(observation);
			_ = Region(observation, frame);
			var nodes = ReadNodes(observation, frame);
			var roots = nodes.Where(node => node.ParentPath is null).ToArray();
			var selected = SelectContent(roots);
			var firstElement = section.Elements.Count;
			foreach (var node in selected)
			{
				var element = BuildElement(node);
				if (element is not null)
					section.Elements.Add(element);
			}
			if (section.Elements.Count == firstElement && !string.IsNullOrEmpty(transcript))
			{
				var text = new ExtractionDocumentTextElement(new TextContent(transcript) { RawRepresentation = observation.Clone() });
				text.Sources.Add(Region(observation, frame) ?? new(frame, rawRepresentation: observation.Clone()));
				section.Elements.Add(text);
			}
		}
		document.SourcePages.Add(frame);
		document.Sections.Add(section);
	}

	private static Node[] ReadNodes(JsonElement observation, ExtractionDocumentSourcePage frame)
	{
		if (!observation.TryGetProperty("nodes", out var values) ||
			values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > MaximumNodes)
			throw new InvalidDataException("Vision returned invalid document nodes.");
		var nodes = values.EnumerateArray().Select(value => new Node(value, frame)).ToArray();
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
		return nodes;
	}

	private static IReadOnlyList<Node> SelectContent(IReadOnlyList<Node> roots)
	{
		// Keep Apple's paragraph order; native structure may duplicate a paragraph projection.
		var structured = roots.Where(node => node.Kind is "title" or "table" or "list").ToArray();
		var result = new List<Node>();
		var emitted = new HashSet<string>(StringComparer.Ordinal);
		foreach (var paragraph in roots.Where(node => node.Kind == "paragraph"))
		{
			var selected = structured.FirstOrDefault(candidate => candidate.Kind switch
			{
				"title" => paragraph.Text == candidate.Text && Overlap(paragraph, candidate) >= 0.7,
				"list" => MatchListProjection(paragraph, candidate),
				_ => Overlap(paragraph, candidate) >= 0.7,
			}) ?? paragraph;
			if (!ReferenceEquals(selected, paragraph) && !selected.ParagraphProjections.Contains(paragraph))
				selected.ParagraphProjections.Add(paragraph);
			if (emitted.Add(selected.Path))
				result.Add(selected);
		}
		foreach (var node in roots)
			if (node.Kind != "paragraph" && emitted.Add(node.Path))
				result.Add(node);
		return result;
	}

	private static ExtractionDocumentElement? BuildElement(Node node)
	{
		ExtractionDocumentElement? result;
		switch (node.Kind)
		{
			case "title":
			case "paragraph":
				result = new ExtractionDocumentTextElement(
					new TextContent(node.Text) { RawRepresentation = node.Json.Clone() },
					node.Kind == "title" ? "heading" : null);
				break;
			case "table":
				var children = node.Children.Where(child => child.Kind == "tableCell").ToArray();
				if (children.Length == 0)
					return null;
				long rows = 0, columns = 0;
				foreach (var child in children)
				{
					var row = Integer(child.Json, "rowIndex");
					var column = Integer(child.Json, "columnIndex");
					var rowSpan = Integer(child.Json, "rowSpan");
					var columnSpan = Integer(child.Json, "columnSpan");
					if (row < 0 || column < 0 || rowSpan < 1 || columnSpan < 1)
						throw new InvalidDataException("Vision returned an invalid table cell.");
					rows = Math.Max(rows, (long)row + rowSpan);
					columns = Math.Max(columns, (long)column + columnSpan);
				}
				if (rows > MaximumNodes || columns > MaximumNodes || rows * columns > MaximumNodes)
					throw new InvalidDataException("Vision returned invalid table dimensions.");
				var table = new ExtractionDocumentTableElement(null, (int)rows, (int)columns);
				var occupied = new bool[(int)rows, (int)columns];
				foreach (var child in children)
				{
					var row = Integer(child.Json, "rowIndex");
					var column = Integer(child.Json, "columnIndex");
					var rowSpan = Integer(child.Json, "rowSpan");
					var columnSpan = Integer(child.Json, "columnSpan");
					for (var r = row; r < row + rowSpan; r++)
						for (var c = column; c < column + columnSpan; c++)
						{
							if (occupied[r, c])
								throw new InvalidDataException("Vision returned overlapping table cells.");
							occupied[r, c] = true;
						}
					var nested = SelectContent(child.Children).Select(BuildElement).OfType<ExtractionDocumentElement>().ToArray();
					ExtractionDocumentElement? content = null;
					if (nested.Length == 1)
						content = nested[0];
					else if (nested.Length > 1)
					{
						var mixed = new ExtractionDocumentSection();
						mixed.Elements.AddRange(nested);
						content = mixed;
					}
					else if (child.Text.Length > 0)
						content = new ExtractionDocumentTextElement(new TextContent(child.Text) { RawRepresentation = child.Json.Clone() });
					var cell = content is null
						? new ExtractionDocumentTableCell(rowSpan, columnSpan)
						: new ExtractionDocumentTableCell(content, rowSpan, columnSpan);
					cell.Sources.Add(child.Region ?? new(child.Frame, rawRepresentation: child.Json.Clone()));
					table.Cells[row, column] = cell;
				}
				result = table;
				break;
			case "list":
			case "barcode":
				var text = node.Kind == "list" ? RenderList(node) :
					GetString(node.Json, "payloadString") ?? node.Text;
				result = new ExtractionDocumentTextElement(new TextContent(text) { RawRepresentation = node.Json.Clone() });
				break;
			default:
				return null;
		}
		result.Sources.Add(node.Region ?? new(node.Frame, rawRepresentation: node.Json.Clone()));
		foreach (var projection in node.ParagraphProjections)
			result.Sources.Add(projection.Region ?? new(projection.Frame, rawRepresentation: projection.Json.Clone()));
		return result;
	}

	private static bool MatchListProjection(Node paragraph, Node list)
	{
		var items = list.Children.Where(child => child.Kind == "listItem").ToArray();
		foreach (var item in items)
			if (MatchesItemText(paragraph.Text, item) &&
				(Overlap(paragraph, item) >= 0.7 || item.Region is null && Overlap(paragraph, list) >= 0.7))
			{
				list.ItemProjections.TryAdd(item.Path, paragraph.Text);
				return true;
			}
		var lines = paragraph.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		if (items.Length > 0 && lines.Length == items.Length && Overlap(paragraph, list) >= 0.7 &&
			items.Select((item, index) => MatchesItemText(lines[index], item)).All(match => match))
		{
			list.WholeListProjection ??= paragraph;
			return true;
		}
		return false;
	}

	private static bool MatchesItemText(string text, Node item)
	{
		var content = GetString(item.Json, "itemString") ?? item.Text;
		if (text == content)
			return true;
		var marker = GetString(item.Json, "markerString");
		return !string.IsNullOrEmpty(marker) && text.StartsWith(marker, StringComparison.Ordinal) &&
			text[marker.Length..].TrimStart() == content;
	}

	private static string RenderList(Node list)
	{
		if (list.WholeListProjection is { } whole)
			return whole.Text;
		return string.Join("\n", list.Children.Where(child => child.Kind == "listItem").Select(item =>
		{
			var text = list.ItemProjections.GetValueOrDefault(item.Path) ?? GetString(item.Json, "itemString") ?? item.Text;
			var marker = GetString(item.Json, "markerString");
			if (!string.IsNullOrEmpty(marker) && !text.StartsWith(marker, StringComparison.Ordinal))
				text = marker + (char.IsWhiteSpace(marker[^1]) ? "" : " ") + text;
			foreach (var nested in SelectContent(item.Children))
			{
				// Vision can expose an item's own region again through its recursive content.
				if (nested.Kind == "paragraph" && MatchesItemText(nested.Text, item) && Overlap(nested, item) >= 0.85 ||
					IsRepeatedItemList(nested, item))
					continue;
				var childText = nested.Kind == "list" ? RenderList(nested) : nested.Kind == "paragraph" ? nested.Text : "";
				if (childText.Length == 0)
					continue;
				text += "\n" + childText;
			}
			return text;
		}));
	}

	private static bool IsRepeatedItemList(Node node, Node item) =>
		node.Kind == "list" && node.Children.Count > 0 && node.Children.All(child =>
			child.Kind == "listItem" && child.Children.Count == 0 &&
			child.Text == item.Text &&
			GetString(child.Json, "itemString") == GetString(item.Json, "itemString") &&
			GetString(child.Json, "markerString") == GetString(item.Json, "markerString") &&
			Overlap(child, item) >= 0.85);

	private static void ValidateConfidence(JsonElement source)
	{
		if (!source.TryGetProperty("confidence", out var confidence) || confidence.ValueKind == JsonValueKind.Null)
			return;
		if (!confidence.TryGetDouble(out var value) || !double.IsFinite(value) || value is < 0 or > 1)
			throw new InvalidDataException("Vision returned invalid confidence.");
	}

	private static ExtractionDocumentSourceReference? Region(JsonElement source, ExtractionDocumentSourcePage frame)
	{
		if (!source.TryGetProperty("polygon", out var polygon))
			return null;
		if (polygon.ValueKind != JsonValueKind.Array)
			throw new InvalidDataException("Vision returned an invalid polygon.");
		var coordinates = polygon.EnumerateArray().Select(value => value.GetDouble()).ToArray();
		if (coordinates.Length == 0)
			return null;
		if (coordinates.Length < 6 || coordinates.Length % 2 != 0 || coordinates.Any(value => !double.IsFinite(value)))
			throw new InvalidDataException("Vision returned an invalid normalized polygon.");
		return new(frame, Enumerable.Range(0, coordinates.Length / 2)
			.Select(index => new ExtractionDocumentPoint(coordinates[index * 2], coordinates[index * 2 + 1])).ToArray(),
			rawRepresentation: source.Clone());
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

	private static int Integer(JsonElement value, string name) =>
		value.TryGetProperty(name, out var number) && number.TryGetInt32(out var result) ? result :
			throw new InvalidDataException("Vision returned an invalid table cell.");

	private sealed class Node
	{
		internal Node(JsonElement json, ExtractionDocumentSourcePage frame)
		{
			Json = json;
			Frame = frame;
			Path = GetString(json, "path") ?? throw new InvalidDataException("Vision returned a node without a path.");
			ParentPath = GetString(json, "parentPath");
			Kind = GetString(json, "kind") ?? "";
			Text = GetString(json, "text") ?? "";
			ValidateConfidence(json);
			if (json.TryGetProperty("words", out var words) && words.ValueKind == JsonValueKind.Array)
				foreach (var word in words.EnumerateArray())
				{
					ValidateConfidence(word);
					_ = AppleVisionRecognizeDocumentsMapper.Region(word, frame);
				}
			Region = AppleVisionRecognizeDocumentsMapper.Region(json, frame);
			if (Region is { } region)
				Bounds = (region.Polygon.Min(point => point.X), region.Polygon.Min(point => point.Y),
					region.Polygon.Max(point => point.X), region.Polygon.Max(point => point.Y));
		}
		internal JsonElement Json { get; }
		internal ExtractionDocumentSourcePage Frame { get; }
		internal string Path { get; }
		internal string? ParentPath { get; }
		internal string Kind { get; }
		internal string Text { get; }
		internal ExtractionDocumentSourceReference? Region { get; }
		internal (double Left, double Bottom, double Right, double Top)? Bounds { get; }
		internal List<Node> Children { get; } = [];
		internal List<Node> ParagraphProjections { get; } = [];
		internal Dictionary<string, string> ItemProjections { get; } = new(StringComparer.Ordinal);
		internal Node? WholeListProjection { get; set; }
	}
}
