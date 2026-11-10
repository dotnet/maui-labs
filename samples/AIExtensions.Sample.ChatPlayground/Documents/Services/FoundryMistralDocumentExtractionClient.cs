using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace AIExtensions.Sample.ChatPlayground;

internal sealed class FoundryMistralDocumentExtractionClient : DocumentExtractionClient
{
    private readonly HttpClient _client;
    private readonly FoundryMistralDocumentReader _reader;
    private readonly string _deploymentName;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public FoundryMistralDocumentExtractionClient(
        HttpClient httpClient, string deploymentName, bool ownsHttpClient = false)
    {
        _reader = new(httpClient, deploymentName);
        _client = httpClient;
        _deploymentName = deploymentName;
        _ownsHttpClient = ownsHttpClient;
    }

    public override Task<ExtractionDocument> ExtractAsync(
        Stream document, string identifier, string mediaType, DocumentExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        var request = options?.Clone();
        if (request?.AdditionalProperties.Count > 0)
            throw new NotSupportedException("This Mistral OCR adapter does not support additional request options.");
        return _reader.ReadResultAsync(document, mediaType, request?.ExtractorName ?? _deploymentName,
            (root, _, token) => MapResult(root, identifier, token), includeImageBase64: true, cancellationToken);
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (serviceKey is not null)
            return null;
        if (serviceType.IsInstanceOfType(this))
            return this;
        return serviceType.IsInstanceOfType(_client) ? _client : null;
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;
        _disposed = true;
        if (disposing && _ownsHttpClient)
            _client.Dispose();
    }

    internal static ExtractionDocument MapResult(
        JsonElement root, string identifier = "document", CancellationToken token = default)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("pages", out var sourcePages) || sourcePages.ValueKind != JsonValueKind.Array ||
            sourcePages.GetArrayLength() == 0)
            throw new InvalidDataException("Mistral OCR returned no pages.");
        var document = new ExtractionDocument(identifier) { RawRepresentation = root.Clone() };
        var numbers = new HashSet<int>();
        foreach (var source in sourcePages.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            var index = Integer(source, "index");
            if (index is null or < 0 or int.MaxValue || !numbers.Add(index.Value + 1))
                throw new InvalidDataException("Mistral OCR returned an invalid or duplicate page index.");
            var number = index.Value + 1;
            ExtractionDocumentPageDimensions? dimensions = null;
            if (source.TryGetProperty("dimensions", out var size) &&
                Number(size, "width") is { } width && Number(size, "height") is { } height)
                dimensions = new(width, height);
            var frame = new ExtractionDocumentSourcePage($"{identifier}/page/{number}", number, dimensions,
                ExtractionDocumentCoordinateUnit.Pixel, ExtractionDocumentCoordinateOrigin.TopLeft);
            document.SourcePages.Add(frame);
            var section = new ExtractionDocumentSection();
            section.Sources.Add(Reference(source));
            document.Sections.Add(section);
            var elements = section.Elements;
            var tables = Indexed(source, "tables", "table_id");
            var images = Indexed(source, "images", "image_id");
            var usedTables = new HashSet<string>(StringComparer.Ordinal);
            var usedImages = new HashSet<string>(StringComparer.Ordinal);
            var hasBodyContent = false;
            AddMargin("header", "pageHeader");
            if (source.TryGetProperty("blocks", out var blocks))
            {
                if (blocks.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Mistral OCR returned invalid blocks.");
                foreach (var block in blocks.EnumerateArray())
                {
                    token.ThrowIfCancellationRequested();
                    switch (String(block, "type"))
                    {
                        case "title":
                        case "text":
                            if (String(block, "content") is { } text)
                            {
                                elements.Add(Text(text, block, String(block, "type") == "title" ? "heading" : null));
                                hasBodyContent |= !string.IsNullOrWhiteSpace(text);
                            }
                            break;
                        case "table":
                            var tableId = String(block, "table_id");
                            if (tableId is null || !tables.TryGetValue(tableId, out var table) || !usedTables.Add(tableId))
                                throw new InvalidDataException("Mistral OCR returned an invalid table reference.");
                            elements.Add(Table(table, block));
                            break;
                        case "image":
                            var imageId = String(block, "image_id");
                            if (imageId is not null)
                            {
                                if (!images.TryGetValue(imageId, out var image) || !usedImages.Add(imageId))
                                    throw new InvalidDataException("Mistral OCR returned an invalid image reference.");
                                elements.Add(Image(image, block));
                            }
                            else
                                elements.Add(Image(block));
                            break;
                        default:
                            if (String(block, "content") is { } content && String(block, "type") is { } type)
                            {
                                elements.Add(Text(content, block, type));
                                hasBodyContent |= !string.IsNullOrWhiteSpace(content);
                            }
                            break;
                    }
                }
            }
            if (!hasBodyContent && String(source, "markdown") is { Length: > 0 } markdown &&
                !IsAggregateMarkdown(markdown))
                elements.Add(Text(markdown, source));
            foreach (var (id, table) in tables)
                if (usedTables.Add(id))
                    elements.Add(Table(table));
            foreach (var (id, image) in images)
                if (usedImages.Add(id))
                    elements.Add(Image(image));
            AddMargin("footer", "pageFooter");

            bool IsAggregateMarkdown(string markdown) =>
                tables.Values.Any(table => String(table, "content") is { Length: > 0 } html &&
                    markdown.Contains(html, StringComparison.Ordinal)) ||
                tables.Keys.Concat(images.Keys).Any(id => markdown.Contains($"]({id})", StringComparison.Ordinal)) ||
                new[] { "header", "footer" }.Any(name => String(source, name) is { Length: > 0 } margin &&
                    markdown.Contains(margin, StringComparison.Ordinal));

            void AddMargin(string name, string role)
            {
                if (String(source, name) is { Length: > 0 } content)
                    elements.Add(Text(content, source, role));
            }

            ExtractionDocumentTextElement Text(string text, JsonElement native, string? role = null)
            {
                var element = new ExtractionDocumentTextElement(new TextContent(text) { RawRepresentation = native.Clone() }, role);
                element.Sources.Add(Reference(native));
                return element;
            }

            ExtractionDocumentSourceReference Reference(JsonElement value)
            {
                ExtractionDocumentPoint[]? polygon = null;
                if (Number(value, "top_left_x") is { } left && Number(value, "top_left_y") is { } top &&
                    Number(value, "bottom_right_x") is { } right && Number(value, "bottom_right_y") is { } bottom)
                    polygon = [new(left, top), new(right, top), new(right, bottom), new(left, bottom)];
                return new(frame, polygon, value.Clone());
            }

            ExtractionDocumentTableElement Table(JsonElement value, JsonElement? referringBlock = null)
            {
                token.ThrowIfCancellationRequested();
                if (String(value, "content") is not { Length: > 0 } html)
                    throw new InvalidDataException("Mistral OCR returned an empty table.");
                // Native HTML is local payload; no cell grid is inferred from it.
                var table = new ExtractionDocumentTableElement(new TextContent(html) { RawRepresentation = value.Clone() }, 0, 0);
                table.Sources.Add(Reference(value));
                if (referringBlock is { } block)
                    table.Sources.Add(Reference(block));
                return table;
            }

            ExtractionDocumentImageElement Image(JsonElement value, JsonElement? referringBlock = null)
            {
                token.ThrowIfCancellationRequested();
                DataContent? content = null;
                if (String(value, "image_base64") is { } encoded)
                {
                    var mediaType = "application/octet-stream";
                    if (encoded.StartsWith("data:", StringComparison.Ordinal))
                    {
                        var separator = encoded.IndexOf(";base64,", StringComparison.Ordinal);
                        if (separator <= 5)
                            throw new InvalidDataException("Mistral OCR returned invalid image data.");
                        mediaType = encoded[5..separator];
                        encoded = encoded[(separator + 8)..];
                    }
                    try { content = new DataContent(Convert.FromBase64String(encoded), mediaType) { RawRepresentation = value.Clone() }; }
                    catch (FormatException) { throw new InvalidDataException("Mistral OCR returned invalid image data."); }
                }
                var image = new ExtractionDocumentImageElement(content, String(value, "ocr_text") ?? String(value, "text"))
                {
                    AlternativeText = referringBlock is { } referring
                        ? String(referring, "caption") ?? String(referring, "content") ?? String(value, "caption")
                        : String(value, "caption"),
                };
                image.Sources.Add(Reference(value));
                if (referringBlock is { } block)
                    image.Sources.Add(Reference(block));
                return image;
            }
        }
        token.ThrowIfCancellationRequested();
        return document;
    }

    private static Dictionary<string, JsonElement> Indexed(JsonElement page, string name, string alternativeId)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!page.TryGetProperty(name, out var items))
            return result;
        if (items.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Mistral OCR returned invalid {name}.");
        foreach (var item in items.EnumerateArray())
        {
            var id = String(item, "id") ?? String(item, alternativeId);
            if (string.IsNullOrWhiteSpace(id) || !result.TryAdd(id, item))
                throw new InvalidDataException($"Mistral OCR returned invalid {name} IDs.");
        }
        return result;
    }

    private static string? String(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    private static int? Integer(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number) ? number : null;
    private static double? Number(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number) && double.IsFinite(number)
            ? number : null;
}
