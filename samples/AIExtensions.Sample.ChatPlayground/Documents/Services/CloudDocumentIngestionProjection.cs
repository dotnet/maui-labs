using System.Net;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Documents;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>A sample-local, structural projection; provider interpretation remains entirely in the Toolkit.</summary>
internal static class CloudDocumentIngestionProjection
{
    public static IngestionDocument Project(ExtractionDocument source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new IngestionDocument(source.Identifier);
        if (source.Content is TextContent { Text: { Length: > 0 } text })
        {
            var local = new IngestionDocumentSection();
            local.Elements.Add(new IngestionDocumentParagraph(text) { Text = text });
            result.Sections.Add(local);
        }
        foreach (var section in source.Sections)
            result.Sections.Add(ProjectSection(section, null, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static IngestionDocumentSection ProjectSection(
        ExtractionDocumentSection source, int? inheritedPage, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var page = PageNumber(source, inheritedPage);
        var result = new IngestionDocumentSection { PageNumber = page };
        if (source.Content is TextContent { Text: { Length: > 0 } text })
            result.Elements.Add(new IngestionDocumentParagraph(text) { Text = text, PageNumber = page });
        foreach (var child in source.Elements)
            if (ProjectElement(child, page, token) is { } projected)
                result.Elements.Add(projected);
        return result;
    }

    private static IngestionDocumentElement? ProjectElement(
        ExtractionDocumentElement source, int? inheritedPage, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var page = PageNumber(source, inheritedPage);
        if (source is ExtractionDocumentSection section)
            return ProjectSection(section, page, token);
        if (source is ExtractionDocumentTableElement table)
        {
            var cells = new IngestionDocumentElement?[table.Cells.RowCount, table.Cells.ColumnCount];
            foreach (var cell in table.Cells)
                if (cell?.Content is { } content)
                    cells[cell.RowIndex, cell.ColumnIndex] = ProjectElement(content,
                        cell.Sources.Count > 0 ? cell.Sources[0].Page.PageNumber : page, token);
            var markdown = RenderTable(table);
            return markdown.Length == 0 ? null : new IngestionDocumentTable(markdown, cells)
                { Text = Text(table), PageNumber = page };
        }
        if (source is ExtractionDocumentImageElement image)
        {
            var data = image.Content as DataContent;
            return new IngestionDocumentImage($"![{image.AlternativeText ?? ""}]({ImageUri(image)})")
            {
                Content = data?.Data,
                MediaType = data?.MediaType,
                AlternativeText = image.AlternativeText,
                Text = image.Text,
                PageNumber = page,
            };
        }
        var text = Text(source);
        if (text.Length == 0)
            return null;
        // A provider's heading role is not a measured heading level; never infer one from Markdown.
        if (source is ExtractionDocumentTextElement { Role: "heading", HeadingLevel: { } level })
            return new IngestionDocumentHeader(text) { Level = level, Text = text, PageNumber = page };
        return new IngestionDocumentParagraph(text) { Text = text, PageNumber = page };
    }

    private static int? PageNumber(ExtractionDocumentElement source, int? fallback) =>
        source.Sources.Count > 0 ? source.Sources[0].Page.PageNumber : fallback;

    private static string Text(ExtractionDocumentElement source)
    {
        var local = (source.Content as TextContent)?.Text;
        IEnumerable<string> children = source switch
        {
            ExtractionDocumentSection section => section.Elements.Select(Text),
            ExtractionDocumentTableElement table => table.Cells.Where(cell => cell?.Content is not null)
                .Select(cell => Text(cell!.Content!)),
            _ => [],
        };
        return string.Join("\n", new[] { local }.Concat(children).Where(value => value is not null));
    }

    private static string RenderTable(ExtractionDocumentTableElement table)
    {
        var local = (table.Content as TextContent)?.Text ?? "";
        if (table.Cells.RowCount == 0 || table.Cells.ColumnCount == 0)
            return local;
        var output = new StringBuilder(local).Append("<table>");
        var covered = new bool[table.Cells.RowCount, table.Cells.ColumnCount];
        foreach (var cell in table.Cells)
            if (cell is not null)
                for (var row = cell.RowIndex; row < cell.RowIndex + cell.RowSpan; row++)
                    for (var column = cell.ColumnIndex; column < cell.ColumnIndex + cell.ColumnSpan; column++)
                        covered[row, column] = true;
        for (var row = 0; row < table.Cells.RowCount; row++)
        {
            output.Append("<tr>");
            for (var column = 0; column < table.Cells.ColumnCount; column++)
            {
                var cell = table.Cells[row, column];
                if (cell is null)
                {
                    if (!covered[row, column])
                        output.Append("<td></td>");
                    continue;
                }
                var tag = cell.Role is "columnHeader" or "rowHeader" or "stubHead" ? "th" : "td";
                output.Append('<').Append(tag);
                if (cell.RowSpan > 1)
                    output.Append(" rowspan=\"").Append(cell.RowSpan).Append('"');
                if (cell.ColumnSpan > 1)
                    output.Append(" colspan=\"").Append(cell.ColumnSpan).Append('"');
                output.Append('>').Append(cell.Content is { } content ? Html(content) : "")
                    .Append("</").Append(tag).Append('>');
            }
            output.Append("</tr>");
        }
        return output.Append("</table>").ToString();
    }

    private static string Html(ExtractionDocumentElement element) => element switch
    {
        ExtractionDocumentTableElement table => RenderTable(table),
        ExtractionDocumentImageElement image =>
            $"<img src=\"{WebUtility.HtmlEncode(ImageUri(image))}\" alt=\"{WebUtility.HtmlEncode(image.AlternativeText ?? "")}\">" +
            WebUtility.HtmlEncode(image.Text ?? ""),
        ExtractionDocumentSection section =>
            WebUtility.HtmlEncode((section.Content as TextContent)?.Text ?? "") +
            string.Join("\n", section.Elements.Select(Html)),
        _ => WebUtility.HtmlEncode(Text(element)).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal),
    };

    private static string ImageUri(ExtractionDocumentImageElement image) =>
        image.Source?.ToString() ?? (image.Content is DataContent data
            ? $"data:{data.MediaType};base64,{Convert.ToBase64String(data.Data.Span)}" : "");
}
