using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace AIExtensions.Sample.ChatPlayground;

internal sealed class FoundryMistralDocumentExtractionClient : IDocumentExtractionClient
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

    public IAsyncEnumerable<DocumentExtractionPage> ExtractPagesAsync(
        Stream document, string identifier, string mediaType, DocumentExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        var request = options?.Clone();
        if (request?.AdditionalProperties.Count > 0)
            throw new NotSupportedException("This Mistral OCR adapter does not support additional request options.");
        return ExtractCoreAsync(document, identifier, mediaType, request?.ModelId ?? _deploymentName,
            request?.CaptureRawOutput ?? true, cancellationToken);
    }

    private async IAsyncEnumerable<DocumentExtractionPage> ExtractCoreAsync(
        Stream document, string identifier, string mediaType, string modelId, bool capture,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        DocumentArtifact? body = null;
        var handedOff = false;
        try
        {
            var pages = await _reader.ReadResultAsync(document, mediaType, modelId, (root, bytes, token) =>
            {
                if (capture)
                    body = new("application/json", new MemoryStream(bytes, writable: false));
                return MapPages(root, identifier, capture, body, token);
            }, includeImageBase64: true, cancellationToken).ConfigureAwait(false);
            foreach (var page in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                handedOff = true;
                yield return page;
            }
        }
        finally
        {
            // The first page transfers the shared operation artifact to the caller/finalizer.
            if (!handedOff)
                body?.Dispose();
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (serviceKey is not null)
            return null;
        if (serviceType.IsInstanceOfType(this))
            return this;
        return serviceType.IsInstanceOfType(_client) ? _client : null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ownsHttpClient)
            _client.Dispose();
    }

    internal static DocumentExtractionResult MapResult(
        JsonElement root, string identifier = "document", bool captureRawOutput = true,
        CancellationToken token = default) =>
        MapPages(root, identifier, captureRawOutput, null, token)
            .ToDocumentExtractionResult(identifier, captureRawOutput);

    private static IReadOnlyList<DocumentExtractionPage> MapPages(
        JsonElement root, string identifier, bool capture,
        DocumentArtifact? body, CancellationToken token)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("pages", out var sourcePages) || sourcePages.ValueKind != JsonValueKind.Array ||
            sourcePages.GetArrayLength() == 0)
            throw new InvalidDataException("Mistral OCR returned no pages.");
        var output = new DocumentSourceOutput($"{identifier}/mistral/ocr",
            rawRepresentation: capture ? root.Clone() : null, rawContent: capture ? body : null,
            captureDisabled: !capture, providerName: "Mistral.OCR.parsed-json", modelId: String(root, "model"));
        var pages = new List<DocumentExtractionPage>();
        var numbers = new HashSet<int>();
        foreach (var source in sourcePages.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            var index = Integer(source, "index");
            if (index is null or < 0 or int.MaxValue || !numbers.Add(index.Value + 1))
                throw new InvalidDataException("Mistral OCR returned an invalid or duplicate page index.");
            var number = index.Value + 1;
            DocumentPageDimensions? dimensions = null;
            if (source.TryGetProperty("dimensions", out var size) &&
                Number(size, "width") is { } width && Number(size, "height") is { } height)
                dimensions = new(width, height);
            var frame = new DocumentSourcePage($"{identifier}/page/{number}", number, dimensions,
                DocumentCoordinateUnit.Pixel, DocumentCoordinateOrigin.TopLeft, output);
            var elements = new List<DocumentExtractionObservation>();
            var tables = Indexed(source, "tables", "table_id");
            var images = Indexed(source, "images", "image_id");
            var usedTables = new HashSet<string>(StringComparer.Ordinal);
            var usedImages = new HashSet<string>(StringComparer.Ordinal);
            var hasBodyContent = false;
            AddMargin("header", DocumentTextKind.Header);
            if (source.TryGetProperty("blocks", out var blocks))
            {
                if (blocks.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Mistral OCR returned invalid blocks.");
                var blockIndex = 0;
                foreach (var block in blocks.EnumerateArray())
                {
                    token.ThrowIfCancellationRequested();
                    var id = $"{frame.Identifier}/block/{blockIndex++}";
                    switch (String(block, "type"))
                    {
                        case "title":
                        case "text":
                            if (String(block, "content") is { } text)
                            {
                                elements.Add(new DocumentExtractionText(id, elements.Count,
                                    String(block, "type") == "title" ? DocumentTextKind.Header : DocumentTextKind.Paragraph,
                                    text, text, annotations: Annotations(block, String(block, "type"))));
                                hasBodyContent |= !string.IsNullOrWhiteSpace(text);
                            }
                            break;
                        case "table":
                            var tableId = String(block, "table_id");
                            if (tableId is null || !tables.TryGetValue(tableId, out var table) || !usedTables.Add(tableId))
                                throw new InvalidDataException("Mistral OCR returned an invalid table reference.");
                            elements.Add(Table(table, tableId, block));
                            break;
                        case "image":
                            var imageId = String(block, "image_id");
                            if (imageId is not null)
                            {
                                if (!images.TryGetValue(imageId, out var image) || !usedImages.Add(imageId))
                                    throw new InvalidDataException("Mistral OCR returned an invalid image reference.");
                                elements.Add(Image(image, imageId, block));
                            }
                            else
                                elements.Add(Image(block, $"block/{blockIndex - 1}"));
                            break;
                        default:
                            if (String(block, "content") is { } content && String(block, "type") is { } type)
                            {
                                elements.Add(new DocumentExtractionExtension(id, elements.Count,
                                    $"mistral.{type}", content, content, annotations: Annotations(block, type)));
                                hasBodyContent |= !string.IsNullOrWhiteSpace(content);
                            }
                            break;
                    }
                }
            }
            if (!hasBodyContent && String(source, "markdown") is { Length: > 0 } markdown)
                elements.Add(new DocumentExtractionText($"{frame.Identifier}/body-markdown", elements.Count,
                    DocumentTextKind.Paragraph, markdown, annotations: [new("mistral.pageMarkdown", markdown)]));
            foreach (var (id, table) in tables)
                if (usedTables.Add(id))
                    elements.Add(Table(table, id));
            foreach (var (id, image) in images)
                if (usedImages.Add(id))
                    elements.Add(Image(image, id));
            AddMargin("footer", DocumentTextKind.Footer);
            pages.Add(new(number, elements, String(source, "markdown"), sourcePage: frame,
                annotations: Annotations(source, "page"), sourceOutputs: [output]));

            void AddMargin(string name, DocumentTextKind kind)
            {
                if (String(source, name) is { Length: > 0 } content)
                    elements.Add(new DocumentExtractionText($"{frame.Identifier}/{name}", elements.Count,
                        kind, content, content, annotations: [new($"mistral.{name}", content)]));
            }
            List<DocumentAnnotation> Annotations(JsonElement value, string? role)
            {
                var regions = new List<DocumentRegion>();
                if (Number(value, "top_left_x") is { } left && Number(value, "top_left_y") is { } top &&
                    Number(value, "bottom_right_x") is { } right && Number(value, "bottom_right_y") is { } bottom)
                    regions.Add(new(frame, [new(left, top), new(right, top), new(right, bottom), new(left, bottom)]));
                var annotations = new List<DocumentAnnotation>
                {
                    new("mistral.ocr", role: role, regions: regions, sourceOutput: output,
                        rawRepresentation: capture ? value.Clone() : null),
                };
                foreach (var name in new[] { "content", "caption" })
                    if (String(value, name) is { } content)
                        annotations.Add(new($"mistral.{name}", content, regions: regions, sourceOutput: output));
                if (value.TryGetProperty("confidence_scores", out var scores) && scores.ValueKind != JsonValueKind.Null)
                {
                    annotations.Add(new("mistral.confidence_scores", scores.Clone(), regions: regions, sourceOutput: output));
                    AddMeasurements(scores, "mistral.confidence_scores");
                }
                return annotations;

                void AddMeasurements(JsonElement measurement, string kind)
                {
                    if (measurement.ValueKind == JsonValueKind.Number)
                    {
                        if (!measurement.TryGetDouble(out var score) || !double.IsFinite(score))
                            throw new InvalidDataException("Mistral OCR returned invalid confidence scores.");
                        annotations.Add(new(kind, confidence: new(score, kind), regions: regions, sourceOutput: output));
                    }
                    else if (measurement.ValueKind == JsonValueKind.Object)
                        foreach (var property in measurement.EnumerateObject())
                            AddMeasurements(property.Value, $"{kind}.{property.Name}");
                    else if (measurement.ValueKind == JsonValueKind.Array)
                    {
                        var index = 0;
                        foreach (var item in measurement.EnumerateArray())
                            AddMeasurements(item, $"{kind}[{index++}]");
                    }
                }
            }
            DocumentExtractionTable Table(JsonElement value, string id, JsonElement? referringBlock = null)
            {
                token.ThrowIfCancellationRequested();
                if (String(value, "content") is not { Length: > 0 } html)
                    throw new InvalidDataException("Mistral OCR returned an empty table.");
                // Native HTML is retained without inventing a cell grid.
                var annotations = Annotations(value, "table");
                if (referringBlock is { } block)
                    annotations.AddRange(Annotations(block, "table"));
                return new($"{frame.Identifier}/table/{id}", elements.Count, html, 0, 0, annotations: annotations);
            }
            DocumentExtractionImage Image(JsonElement value, string id, JsonElement? referringBlock = null)
            {
                token.ThrowIfCancellationRequested();
                ReadOnlyMemory<byte>? bytes = null;
                string? mediaType = null;
                if (String(value, "image_base64") is { } encoded)
                {
                    mediaType = "application/octet-stream";
                    if (encoded.StartsWith("data:", StringComparison.Ordinal))
                    {
                        var separator = encoded.IndexOf(";base64,", StringComparison.Ordinal);
                        if (separator <= 5)
                            throw new InvalidDataException("Mistral OCR returned invalid image data.");
                        mediaType = encoded[5..separator];
                        encoded = encoded[(separator + 8)..];
                    }
                    try { bytes = Convert.FromBase64String(encoded); }
                    catch (FormatException) { throw new InvalidDataException("Mistral OCR returned invalid image data."); }
                }
                var annotations = Annotations(value, "image");
                if (referringBlock is { } block)
                    annotations.AddRange(Annotations(block, "image"));
                var text = referringBlock is { } referring
                    ? String(referring, "caption") ?? String(referring, "content") ?? String(value, "caption")
                    : String(value, "caption") ?? String(value, "content");
                return new($"{frame.Identifier}/image/{id}", elements.Count, String(value, "markdown") ?? "",
                    text: text, content: bytes, mediaType: mediaType, annotations: annotations);
            }
        }
        return pages;
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
