using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using ExtractedDocumentPage = Microsoft.Extensions.DocumentExtraction.DocumentPage;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Projects normalized document elements into rows for the playground tree.</summary>
public static class DocumentResultProjector
{
    private const int MaximumPreviewLength = 180;

    public static IReadOnlyList<DocumentResultNode> Project(DocumentExtractionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var nodes = new List<DocumentResultNode>();
        for (var pageIndex = 0; pageIndex < result.Pages.Count; pageIndex++)
        {
            var page = result.Pages[pageIndex];
            var pagePath = $"$.pages[{pageIndex}]";
            nodes.Add(new DocumentResultNode
            {
                Depth = 0,
                Title = $"Page {page.PageNumber}",
                JsonPath = pagePath,
                PageNumber = page.PageNumber,
                Subtitle = Truncate(page.Text),
                Metadata = FormatPageMetadata(page),
                RawJsonFactory = DocumentRawJson.CreateFactory(page.RawRepresentation),
            });
            AppendElements(nodes, page.Elements, page.PageNumber, depth: 1, $"{pagePath}.elements");
        }
        return nodes;
    }

    private static void AppendElements(
        List<DocumentResultNode> nodes,
        IReadOnlyList<DocumentElement> elements,
        int pageNumber,
        int depth,
        string path)
    {
        for (var index = 0; index < elements.Count; index++)
            AppendElement(nodes, elements[index], pageNumber, depth, $"{path}[{index}]");
    }

    private static void AppendElement(
        List<DocumentResultNode> nodes,
        DocumentElement element,
        int pageNumber,
        int depth,
        string path)
    {
        var raw = DocumentRawJson.CreateFactory(element.RawRepresentation);
        switch (element)
        {
            case DocumentTable table:
                nodes.Add(new DocumentResultNode
                {
                    Depth = depth,
                    Title = $"Table ({table.RowCount} x {table.ColumnCount})",
                    JsonPath = path,
                    PageNumber = pageNumber,
                    Subtitle = table.Cells is null ? Truncate(table.MarkdownRepresentation) : null,
                    Metadata = FormatElementMetadata(table),
                    BoundingRegion = table.BoundingRegion,
                    RegionKind = DocumentRegionKind.Table,
                    RawJsonFactory = raw,
                });
                if (table.Cells is { Count: > 0 } cells)
                {
                    for (var index = 0; index < cells.Count; index++)
                        AppendCell(nodes, cells[index], pageNumber, depth + 1, $"{path}.cells[{index}]");
                }
                break;

            case DocumentImage image:
                nodes.Add(new DocumentResultNode
                {
                    Depth = depth,
                    Title = "Image",
                    JsonPath = path,
                    PageNumber = pageNumber,
                    Subtitle = image.Caption ?? (image.Content is null ? "(no content)" : "(embedded content)"),
                    Metadata = FormatElementMetadata(image),
                    BoundingRegion = image.BoundingRegion,
                    RegionKind = DocumentRegionKind.Image,
                    RawJsonFactory = raw,
                });
                break;

            case DocumentBlock block:
                var kind = block.Kind?.Value;
                var marker = GetProperty(block.AdditionalProperties, "apple.vision.markerString") as string;
                var symbology = GetProperty(block.AdditionalProperties, "apple.vision.barcodeSymbology") as string;
                nodes.Add(new DocumentResultNode
                {
                    Depth = depth,
                    Title = kind switch
                    {
                        "listItem" when !string.IsNullOrWhiteSpace(marker) =>
                            $"List item {marker.Trim()}",
                        "listItem" => "List item",
                        "barcode" => $"Barcode ({symbology ?? "unknown"})",
                        _ => kind ?? "Block",
                    },
                    JsonPath = path,
                    PageNumber = pageNumber,
                    Subtitle = Truncate(block.Text),
                    Metadata = JoinMetadata(
                        GetProperty(block.AdditionalProperties, "apple.vision.markerType") is string markerType
                            ? $"marker {markerType}"
                            : null,
                        FormatElementMetadata(block)),
                    BoundingRegion = block.BoundingRegion,
                    RegionKind = kind switch
                    {
                        "listItem" => DocumentRegionKind.ListItem,
                        "barcode" => DocumentRegionKind.Barcode,
                        _ => DocumentRegionKind.Block,
                    },
                    RawJsonFactory = raw,
                });
                break;

            default:
                nodes.Add(new DocumentResultNode
                {
                    Depth = depth,
                    Title = element.GetType().Name,
                    JsonPath = path,
                    PageNumber = pageNumber,
                    Metadata = FormatElementMetadata(element),
                    BoundingRegion = element.BoundingRegion,
                    RawJsonFactory = raw,
                });
                break;
        }
    }

    private static void AppendCell(
        List<DocumentResultNode> nodes,
        DocumentTableCell cell,
        int pageNumber,
        int depth,
        string path)
    {
        var kind = cell.Kind?.Value;
        var span = cell.RowSpan > 1 || cell.ColumnSpan > 1
            ? $"span {cell.RowSpan} x {cell.ColumnSpan}"
            : null;
        nodes.Add(new DocumentResultNode
        {
            Depth = depth,
            Title = $"Cell [{cell.RowIndex}, {cell.ColumnIndex}]",
            JsonPath = path,
            PageNumber = pageNumber,
            Subtitle = Truncate(cell.Content),
            Metadata = JoinMetadata(kind, span, FormatBounds(cell.BoundingRegion)),
            BoundingRegion = cell.BoundingRegion,
            RegionKind = DocumentRegionKind.Cell,
            RawJsonFactory = DocumentRawJson.CreateFactory(cell.RawRepresentation),
        });
        if (cell.Elements is { Count: > 0 } elements)
            AppendElements(nodes, elements, pageNumber, depth + 1, $"{path}.elements");
    }

    private static string FormatPageMetadata(ExtractedDocumentPage page) =>
        JoinMetadata(
            $"{page.Elements.Count} top-level elements",
            $"{page.CoordinateUnit}/{page.CoordinateOrigin}",
            GetProperty(page.AdditionalProperties, "apple.vision.structureTruncated") is true
                ? "provider structure pruned"
                : null,
            GetProperty(page.AdditionalProperties, "apple.pdf.rotation") is { } rotation
                ? $"PDF rotation {rotation} degrees"
                : null);

    private static string FormatElementMetadata(DocumentElement element) =>
        JoinMetadata(
            element.Confidence is { } confidence ? $"confidence {confidence:P0}" : null,
            FormatBounds(element.BoundingRegion),
            GetProperty(element.AdditionalProperties, "detectedLanguages") is string[] languages
                ? string.Join(", ", languages)
                : null,
            GetProperty(element.AdditionalProperties, "apple.textAlignment") is string alignment
                ? $"alignment {alignment}"
                : null);

    private static string? FormatBounds(DocumentBoundingRegion? region)
    {
        if (region is null || region.Polygon.Count == 0)
            return null;

        if (region.GetBounds() is not { } bounds)
            return null;

        return $"bounds {bounds.Left:F2},{bounds.Top:F2} " +
            $"{bounds.Right - bounds.Left:F2} x {bounds.Bottom - bounds.Top:F2}";
    }

    private static object? GetProperty(
        AdditionalPropertiesDictionary? properties,
        string key) =>
        properties?.TryGetValue(key, out var value) == true ? value : null;

    private static string JoinMetadata(params string?[] values) =>
        string.Join(
            " | ",
            values.Where(static value => !string.IsNullOrWhiteSpace(value)));

    private static string? Truncate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var singleLine = text.Replace('\n', ' ').Replace('\r', ' ');
        return singleLine.Length <= MaximumPreviewLength
            ? singleLine
            : string.Concat(singleLine.AsSpan(0, MaximumPreviewLength), "\u2026");
    }
}
