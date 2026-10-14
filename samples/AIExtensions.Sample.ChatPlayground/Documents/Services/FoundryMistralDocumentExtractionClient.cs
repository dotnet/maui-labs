using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace AIExtensions.Sample.ChatPlayground;

internal sealed class FoundryMistralDocumentExtractionClient : IDocumentExtractionClient
{
    private readonly HttpClient _client;
    private readonly FoundryMistralDocumentReader _reader;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public FoundryMistralDocumentExtractionClient(
        HttpClient httpClient, string deploymentName, bool ownsHttpClient = false)
    {
        _reader = new(httpClient, deploymentName);
        _client = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

    public async IAsyncEnumerable<DocumentExtractionPage> ExtractPagesAsync(
        Stream document, string identifier, string mediaType,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // OCR returns a complete JSON document, not incremental server events.
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        var pages = await _reader.ReadResultAsync(document, mediaType, MapPages, cancellationToken).ConfigureAwait(false);
        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return page;
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

    internal static IReadOnlyList<DocumentExtractionPage> MapPages(JsonElement root, CancellationToken token = default)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("pages", out var sourcePages) || sourcePages.ValueKind != JsonValueKind.Array ||
            sourcePages.GetArrayLength() == 0)
            throw new InvalidDataException("Mistral OCR returned no pages.");

        var pages = new List<DocumentExtractionPage>();
        var numbers = new HashSet<int>();
        foreach (var source in sourcePages.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            var index = Integer(source, "index");
            if (index is null or < 0 or int.MaxValue || !numbers.Add(index.Value + 1))
                throw new InvalidDataException("Mistral OCR returned an invalid or duplicate page index.");
            var number = index.Value + 1;
            var elements = new List<DocumentExtractionObservation>();
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
                            if (String(block, "content") is { Length: > 0 } text)
                                elements.Add(new DocumentExtractionText(Id(), elements.Count,
                                    String(block, "type") == "title" ? DocumentTextKind.Header : DocumentTextKind.Paragraph,
                                    text, text));
                            break;
                        case "table":
                            var tableId = String(block, "table_id");
                            if (tableId is null || !tables.TryGetValue(tableId, out var table) || !usedTables.Add(tableId))
                                throw new InvalidDataException("Mistral OCR returned an invalid table reference.");
                            elements.Add(Table(table, Id(), elements.Count));
                            break;
                        case "image":
                            var imageId = String(block, "image_id");
                            if (imageId is not null)
                            {
                                if (!images.TryGetValue(imageId, out var image) || !usedImages.Add(imageId))
                                    throw new InvalidDataException("Mistral OCR returned an invalid image reference.");
                                AddImage(image);
                            }
                            else
                                AddImage(block);
                            break;
                        // Unknown native block kinds have no supported observation projection.
                    }
                }
            }
            foreach (var (id, table) in tables)
            {
                token.ThrowIfCancellationRequested();
                if (usedTables.Add(id))
                    elements.Add(Table(table, Id(), elements.Count));
            }
            foreach (var (id, image) in images)
            {
                token.ThrowIfCancellationRequested();
                if (usedImages.Add(id))
                    AddImage(image);
            }
            AddMargin("footer");
            var markdown = String(source, "markdown");
            pages.Add(new DocumentExtractionPage(number, elements, string.IsNullOrEmpty(markdown) ? null : markdown));
            string Id() => $"page-{number}/observation-{elements.Count}";

            void AddImage(JsonElement image)
            {
                if (Image(image, Id(), elements.Count) is { } observation)
                    elements.Add(observation);
            }

            void AddMargin(string name)
            {
                if (String(source, name) is { Length: > 0 } content)
                    elements.Add(new DocumentExtractionText(Id(), elements.Count,
                        name == "header" ? DocumentTextKind.Header : DocumentTextKind.Footer, content, content));
            }
        }
        return pages;
    }

    private static DocumentExtractionTable Table(JsonElement source, string id, int order)
    {
        if (String(source, "content") is not { Length: > 0 } html)
            throw new InvalidDataException("Mistral OCR returned an empty table.");
        // HTML is the provider's representation; it does not report a native cell grid.
        return new(id, order, html, 0, 0);
    }

    private static DocumentExtractionImage? Image(JsonElement source, string id, int order)
    {
        var markdown = String(source, "markdown") ?? String(source, "caption");
        if (string.IsNullOrEmpty(markdown))
            return null;
        ReadOnlyMemory<byte>? content = null;
        string? mediaType = null;
        if (String(source, "image_base64") is { Length: > 0 } encoded)
        {
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
                content = Convert.FromBase64String(encoded);
            }
            catch (FormatException)
            {
                throw new InvalidDataException("Mistral OCR returned invalid image data.");
            }
        }
        return new DocumentExtractionImage(id, order, markdown,
            text: String(source, "caption"), content: content, mediaType: mediaType);
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

}
