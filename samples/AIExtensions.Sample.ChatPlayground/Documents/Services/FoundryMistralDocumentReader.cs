using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.DataIngestion;
using HtmlElement = AngleSharp.Dom.IElement;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Sample-only Foundry Mistral OCR reader; the supplied client owns authentication.</summary>
internal sealed class FoundryMistralDocumentReader : IngestionDocumentReader
{
    private const int MaximumDocumentBytes = 20 * 1024 * 1024;
    private const int MaximumResponseBytes = 32 * 1024 * 1024;
    private const int MaximumRows = 1000;
    private const int MaximumColumns = 100;
    private const string OcrPath = "providers/mistral/azure/ocr";
    private readonly HttpClient _client;
    private readonly string _deploymentName;

    public FoundryMistralDocumentReader(HttpClient httpClient, string deploymentName)
    {
        _client = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (_client.BaseAddress is null)
            throw new ArgumentException("The Foundry client needs a base address.", nameof(httpClient));
        _deploymentName = string.IsNullOrWhiteSpace(deploymentName)
            ? throw new ArgumentException("A deployment name is required.", nameof(deploymentName))
            : deploymentName;
    }

    public override async Task<IngestionDocument> ReadAsync(
        Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        if (!source.CanRead)
            throw new ArgumentException("The document stream must be readable.", nameof(source));
        if (mediaType is not ("application/pdf" or "image/png" or "image/jpeg" or "image/heic" or "image/tiff"))
            throw new NotSupportedException("The document media type is not supported.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var token = deadline.Token;
        using var input = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (input.Length + count > MaximumDocumentBytes)
                throw new InvalidOperationException("Documents must be 20 MB or smaller.");
            await input.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            model = _deploymentName,
            document = new { type = "document_url", document_url = $"data:{mediaType};base64,{Convert.ToBase64String(input.ToArray())}" },
            include_image_base64 = false,
            include_blocks = true,
            table_format = "html",
            extract_header = true,
            extract_footer = true,
        });

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            token.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Post, OcrPath);
            request.Content = new ByteArrayContent(payload);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                throw new HttpRequestException("Mistral OCR request failed.");
            }
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.ServiceUnavailable && attempt < 5)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 3), token).ConfigureAwait(false);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"Mistral OCR returned HTTP {(int)response.StatusCode}.");

                using var content = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                using var bytes = new MemoryStream();
                while ((count = await content.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    if (bytes.Length + count > MaximumResponseBytes)
                        throw new InvalidDataException("Mistral OCR response exceeds 32 MB.");
                    await bytes.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                try
                {
                    using var json = JsonDocument.Parse(bytes.ToArray());
                    var document = MapResult(json.RootElement, identifier, token);
                    token.ThrowIfCancellationRequested();
                    return document;
                }
                catch (JsonException)
                {
                    throw new InvalidDataException("Mistral OCR returned invalid JSON.");
                }
            }
        }
        throw new InvalidOperationException("Mistral OCR retry limit reached.");
    }

    internal static IngestionDocument MapResult(JsonElement root, string identifier) =>
        MapResult(root, identifier, CancellationToken.None);

    private static IngestionDocument MapResult(JsonElement root, string identifier, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array ||
            pages.GetArrayLength() == 0)
            throw new InvalidDataException("Mistral OCR returned no pages.");

        var document = new IngestionDocument(identifier);
        var pageNumbers = new HashSet<int>();
        foreach (var page in pages.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            if (page.ValueKind != JsonValueKind.Object || !page.TryGetProperty("index", out var index) ||
                index.ValueKind != JsonValueKind.Number || !index.TryGetInt32(out var pageIndex) ||
                pageIndex < 0 || pageIndex == int.MaxValue)
                throw new InvalidDataException("Mistral OCR returned an invalid page index.");
            var pageNumber = pageIndex + 1;
            if (!pageNumbers.Add(pageNumber))
                throw new InvalidDataException("Mistral OCR returned duplicate page indexes.");
            var section = new IngestionDocumentSection { PageNumber = pageNumber };
            document.Sections.Add(section);
            AppendParagraph(page, "header", section, pageNumber);

            var tables = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (page.TryGetProperty("tables", out var tableArray))
            {
                if (tableArray.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Mistral OCR returned invalid tables.");
                foreach (var table in tableArray.EnumerateArray())
                {
                    var id = GetString(table, "id") ?? GetString(table, "table_id");
                    if (string.IsNullOrWhiteSpace(id) || !tables.TryAdd(id, table))
                        throw new InvalidDataException("Mistral OCR returned an invalid table ID.");
                }
            }

            var usedTables = new HashSet<string>(StringComparer.Ordinal);
            var blockElementCount = section.Elements.Count;
            if (page.TryGetProperty("blocks", out var blocks))
            {
                if (blocks.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Mistral OCR returned invalid blocks.");
                foreach (var block in blocks.EnumerateArray())
                {
                    token.ThrowIfCancellationRequested();
                    var type = GetString(block, "type");
                    var content = GetString(block, "content");
                    switch (type)
                    {
                        case "title":
                            if (!string.IsNullOrWhiteSpace(content))
                                section.Elements.Add(CreateHeader(content, pageNumber));
                            break;
                        case "text":
                            if (!string.IsNullOrWhiteSpace(content))
                                section.Elements.Add(CreateParagraph(content, pageNumber));
                            break;
                        case "table":
                            var tableId = GetString(block, "table_id");
                            if (tableId is null || !tables.TryGetValue(tableId, out var table))
                                throw new InvalidDataException("Mistral OCR referenced an unknown table.");
                            if (!usedTables.Add(tableId))
                                throw new InvalidDataException("Mistral OCR referenced a table more than once.");
                            section.Elements.Add(MapTable(table, pageNumber, token));
                            break;
                        default:
                            if (type != "image" && !string.IsNullOrWhiteSpace(content))
                                section.Elements.Add(CreateParagraph(content, pageNumber));
                            break;
                    }
                }
            }
            if (section.Elements.Count == blockElementCount)
            {
                var markdown = GetString(page, "markdown");
                if (!string.IsNullOrWhiteSpace(markdown))
                    section.Elements.Add(CreateParagraph(markdown, pageNumber));
            }
            foreach (var (id, table) in tables)
            {
                token.ThrowIfCancellationRequested();
                if (!usedTables.Contains(id))
                    section.Elements.Add(MapTable(table, pageNumber, token));
            }
            AppendParagraph(page, "footer", section, pageNumber);
        }
        return document;
    }

    private static void AppendParagraph(JsonElement page, string name, IngestionDocumentSection section, int pageNumber)
    {
        var content = GetString(page, name);
        if (!string.IsNullOrWhiteSpace(content))
            section.Elements.Add(CreateParagraph(content, pageNumber));
    }

    private static IngestionDocumentParagraph CreateParagraph(string markdown, int pageNumber) =>
        new(markdown) { Text = ReadableText(markdown), PageNumber = pageNumber };

    private static IngestionDocumentHeader CreateHeader(string markdown, int pageNumber)
    {
        var level = 1;
        while (level <= 6 && level <= markdown.Length && markdown[level - 1] == '#')
            level++;
        level--;
        var text = level > 0 && markdown.Length > level && markdown[level] == ' '
            ? markdown[(level + 1)..]
            : markdown;
        return new IngestionDocumentHeader(text)
        {
            Level = level > 0 && text != markdown ? level : 1,
            Text = ReadableText(text),
            PageNumber = pageNumber,
        };
    }

    private static string ReadableText(string markdown)
    {
        var text = WebUtility.HtmlDecode(markdown);
        try
        {
            text = Regex.Replace(text, @"!?\[([^\]]+)\]\([^)]+\)", "$1", RegexOptions.None, TimeSpan.FromSeconds(2));
            text = Regex.Replace(text, @"(\*\*|__|~~|`)(.*?)\1", "$2", RegexOptions.None, TimeSpan.FromSeconds(2));
        }
        catch (RegexMatchTimeoutException)
        {
            throw new InvalidDataException("Mistral OCR text formatting exceeded its processing limit.");
        }
        return text.Trim();
    }

    private static string? GetString(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private static IngestionDocumentTable MapTable(JsonElement source, int pageNumber, CancellationToken token)
    {
        var html = GetString(source, "content");
        if (string.IsNullOrWhiteSpace(html))
            throw new InvalidDataException("Mistral OCR returned an empty table.");
        var parsed = new HtmlParser().ParseDocument(html);
        var table = parsed.QuerySelector("table");
        if (table is null || parsed.QuerySelectorAll("table").Length != 1)
            throw new InvalidDataException("Mistral OCR returned invalid table HTML.");
        var rows = table.QuerySelectorAll("tr")
            .Where(row => row.Closest("table") == table).ToArray();
        if (rows.Length is < 1 or > MaximumRows)
            throw new InvalidDataException("Mistral OCR returned invalid table dimensions.");

        var cells = new IngestionDocumentElement?[rows.Length, MaximumColumns];
        var occupied = new bool[rows.Length, MaximumColumns];
        var display = new StringBuilder("<table>");
        var text = new StringBuilder();
        var columns = 0;
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            token.ThrowIfCancellationRequested();
            display.Append("<tr>");
            var column = 0;
            foreach (var cell in rows[rowIndex].Children.Where(child => child.LocalName is "td" or "th"))
            {
                while (column < MaximumColumns && occupied[rowIndex, column])
                    column++;
                var rowSpan = ParseSpan(cell, "rowspan");
                var columnSpan = ParseSpan(cell, "colspan");
                if (column >= MaximumColumns || (long)rowIndex + rowSpan > rows.Length ||
                    (long)column + columnSpan > MaximumColumns)
                    throw new InvalidDataException("Mistral OCR returned a table cell outside its dimensions.");
                for (var r = rowIndex; r < rowIndex + rowSpan; r++)
                    for (var c = column; c < column + columnSpan; c++)
                    {
                        if (occupied[r, c])
                            throw new InvalidDataException("Mistral OCR returned overlapping table cells.");
                        occupied[r, c] = true;
                    }
                var value = ReadCellText(cell, token);
                if (value.Length > 0)
                {
                    cells[rowIndex, column] = new IngestionDocumentParagraph(value)
                    {
                        Text = value,
                        PageNumber = pageNumber,
                    };
                    if (text.Length > 0)
                        text.Append(' ');
                    text.Append(value);
                }
                display.Append('<').Append(cell.LocalName);
                if (rowSpan > 1)
                    display.Append(" rowspan=\"").Append(rowSpan).Append('"');
                if (columnSpan > 1)
                    display.Append(" colspan=\"").Append(columnSpan).Append('"');
                display.Append('>').Append(WebUtility.HtmlEncode(value).Replace("\n", "<br>", StringComparison.Ordinal))
                    .Append("</").Append(cell.LocalName).Append('>');
                column += columnSpan;
                columns = Math.Max(columns, column);
            }
            display.Append("</tr>");
        }
        if (columns == 0)
            throw new InvalidDataException("Mistral OCR returned an empty table grid.");
        display.Append("</table>");
        var grid = new IngestionDocumentElement?[rows.Length, columns];
        for (var row = 0; row < rows.Length; row++)
            for (var column = 0; column < columns; column++)
                grid[row, column] = cells[row, column];
        return new IngestionDocumentTable(display.ToString(), grid)
        {
            Text = text.ToString(),
            PageNumber = pageNumber,
        };
    }

    private static int ParseSpan(HtmlElement cell, string name)
    {
        var attribute = cell.GetAttribute(name);
        if (attribute is null)
            return 1;
        if (!int.TryParse(attribute, out var span) || span is < 1 or > MaximumRows)
            throw new InvalidDataException("Mistral OCR returned an invalid table span.");
        return span;
    }

    private static string ReadCellText(HtmlElement cell, CancellationToken token)
    {
        var output = new StringBuilder();
        var pending = new Stack<(INode Node, bool Closing)>();
        pending.Push((cell, false));
        while (pending.TryPop(out var entry))
        {
            token.ThrowIfCancellationRequested();
            var (node, closing) = entry;
            if (node.NodeType == NodeType.Text)
            {
                output.Append(node.TextContent);
                continue;
            }
            if (node is HtmlElement element && element.LocalName is "br" or "p" or "div" or "li")
            {
                if (output.Length > 0 && output[^1] != '\n')
                    output.Append('\n');
                if (closing || element.LocalName == "br")
                    continue;
                pending.Push((node, true));
            }
            for (var index = node.ChildNodes.Length - 1; index >= 0; index--)
                pending.Push((node.ChildNodes[index], false));
        }
        return output.ToString().Trim();
    }
}
