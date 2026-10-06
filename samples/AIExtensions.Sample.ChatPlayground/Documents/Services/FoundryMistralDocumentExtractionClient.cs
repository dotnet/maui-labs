using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using ExtractedDocumentPage = Microsoft.Extensions.DocumentExtraction.DocumentPage;

namespace AIExtensions.Sample.ChatPlayground;

internal sealed class FoundryMistralDocumentExtractionClient : IDocumentExtractionClient
{
    private readonly HttpClient _client;
    private readonly FoundryMistralDocumentReader _reader;
    private readonly bool _ownsHttpClient;
    private readonly DocumentExtractionClientMetadata _metadata;
    private bool _disposed;

    public FoundryMistralDocumentExtractionClient(
        HttpClient httpClient, string deploymentName, bool ownsHttpClient = false)
    {
        _reader = new(httpClient, deploymentName);
        _client = httpClient;
        _ownsHttpClient = ownsHttpClient;
        _metadata = new("Azure.AI.Foundry.Mistral", httpClient.BaseAddress, deploymentName);
    }

    public Task<DocumentExtractionResult> ExtractAsync(
        Stream document, string mediaType, DocumentExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (options?.ModelId is { } modelId)
            ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        return _reader.ReadResultAsync(document, mediaType, options?.ModelId, MapResult, cancellationToken);
    }

    public async IAsyncEnumerable<DocumentExtractionPageResult> ExtractPagesAsync(
        Stream document, string mediaType, DocumentExtractionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // OCR returns a complete JSON document, not incremental server events.
        var result = await ExtractAsync(document, mediaType, options, cancellationToken);
        for (var index = 0; index < result.Pages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(result.Pages[index])
            {
                PagesProcessed = index + 1,
                TotalPages = result.Pages.Count,
                Usage = index == result.Pages.Count - 1 ? result.Usage : null,
                RawRepresentation = result.Pages[index].RawRepresentation,
            };
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (serviceKey is not null)
            return null;
        if (serviceType == typeof(DocumentExtractionClientMetadata))
            return _metadata;
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

    internal static DocumentExtractionResult MapResult(JsonElement root, CancellationToken token = default)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("pages", out var sourcePages) || sourcePages.ValueKind != JsonValueKind.Array ||
            sourcePages.GetArrayLength() == 0)
            throw new InvalidDataException("Mistral OCR returned no pages.");

        var pages = new List<ExtractedDocumentPage>();
        var numbers = new HashSet<int>();
        foreach (var source in sourcePages.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            var index = Integer(source, "index");
            if (index is null or < 0 or int.MaxValue || !numbers.Add(index.Value + 1))
                throw new InvalidDataException("Mistral OCR returned an invalid or duplicate page index.");
            var number = index.Value + 1;
            var elements = new List<DocumentElement>();
            var tables = Indexed(source, "tables", "table_id");
            var images = Indexed(source, "images", "image_id");
            var usedTables = new HashSet<string>(StringComparer.Ordinal);
            var usedImages = new HashSet<string>(StringComparer.Ordinal);
            AddMargin("header");
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
                                elements.Add(new DocumentBlock(text)
                                {
                                    Kind = String(block, "type") == "title" ? DocumentBlockKind.Title : DocumentBlockKind.Paragraph,
                                    RawRepresentation = block.Clone(),
                                });
                            break;
                        case "table":
                            var tableId = String(block, "table_id");
                            if (tableId is null || !tables.TryGetValue(tableId, out var table) || !usedTables.Add(tableId))
                                throw new InvalidDataException("Mistral OCR returned an invalid table reference.");
                            elements.Add(Table(table));
                            break;
                        case "image":
                            var imageId = String(block, "image_id");
                            if (imageId is not null)
                            {
                                if (!images.TryGetValue(imageId, out var image) || !usedImages.Add(imageId))
                                    throw new InvalidDataException("Mistral OCR returned an invalid image reference.");
                                elements.Add(Image(image, number));
                            }
                            else
                                elements.Add(Image(block, number));
                            break;
                        // Unknown native block kinds are preserved in the raw page only.
                    }
                }
            }
            foreach (var (id, table) in tables)
            {
                token.ThrowIfCancellationRequested();
                if (usedTables.Add(id))
                    elements.Add(Table(table));
            }
            foreach (var (id, image) in images)
            {
                token.ThrowIfCancellationRequested();
                if (usedImages.Add(id))
                    elements.Add(Image(image, number));
            }
            AddMargin("footer");
            DocumentPageDimensions? dimensions = null;
            if (source.TryGetProperty("dimensions", out var size) &&
                Number(size, "width") is { } width && Number(size, "height") is { } height)
                dimensions = new(width, height);
            pages.Add(new(number, String(source, "markdown") ?? "")
            {
                Elements = elements,
                Dimensions = dimensions,
                CoordinateUnit = dimensions is not null ? DocumentCoordinateUnit.Pixel : null,
                CoordinateOrigin = dimensions is not null ? DocumentCoordinateOrigin.TopLeft : null,
                RawRepresentation = source.Clone(),
            });

            void AddMargin(string name)
            {
                if (String(source, name) is { Length: > 0 } content)
                    elements.Add(new DocumentBlock(content) { RawRepresentation = source.GetProperty(name).Clone() });
            }
        }
        DocumentExtractionUsage? usage = null;
        if (root.TryGetProperty("usage_info", out var info))
        {
            var pagesProcessed = Integer(info, "pages_processed");
            if (pagesProcessed is >= 0)
                usage = new() { PagesProcessed = pagesProcessed };
        }
        return new(pages) { Usage = usage, RawRepresentation = root.Clone() };
    }

    private static DocumentTable Table(JsonElement source)
    {
        if (String(source, "content") is not { Length: > 0 } html)
            throw new InvalidDataException("Mistral OCR returned an empty table.");
        // HTML is the provider's representation; it does not report a native cell grid.
        return new(0, 0, markdownRepresentation: html) { RawRepresentation = source.Clone() };
    }

    private static DocumentImage Image(JsonElement source, int number)
    {
        DataContent? content = null;
        if (String(source, "image_base64") is { Length: > 0 } encoded)
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
            try
            {
                content = new DataContent(Convert.FromBase64String(encoded), mediaType);
            }
            catch (FormatException)
            {
                throw new InvalidDataException("Mistral OCR returned invalid image data.");
            }
        }
        var image = new DocumentImage
        {
            Content = content,
            Caption = String(source, "caption"),
            RawRepresentation = source.Clone(),
        };
        if (Number(source, "top_left_x") is { } left && Number(source, "top_left_y") is { } top &&
            Number(source, "bottom_right_x") is { } right && Number(source, "bottom_right_y") is { } bottom)
            image.BoundingRegion = DocumentBoundingRegion.FromRectangle(number, left, top, right, bottom);
        return image;
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

    private static float? Number(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.Number && property.TryGetSingle(out var number) && float.IsFinite(number)
            ? number : null;
}
