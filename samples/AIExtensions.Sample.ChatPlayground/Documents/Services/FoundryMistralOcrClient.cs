using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using ExtractedDocumentPage = Microsoft.Extensions.DocumentExtraction.DocumentPage;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Calls a Mistral OCR deployment hosted by a Microsoft Foundry resource.</summary>
public sealed class FoundryMistralOcrClient : IDocumentExtractionClient
{
    private const int MaximumDocumentBytes = 30 * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string _apiKey;
    private readonly string _defaultModel;
    private readonly bool _disposeHttpClient;

    public FoundryMistralOcrClient(
        HttpClient httpClient,
        Uri endpoint,
        string apiKey,
        string defaultModel = "mistral-ocr-4-0",
        bool disposeHttpClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _endpoint = NormalizeEndpoint(endpoint ?? throw new ArgumentNullException(nameof(endpoint)));
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new ArgumentException("A Foundry API key is required.", nameof(apiKey))
            : apiKey;
        _defaultModel = string.IsNullOrWhiteSpace(defaultModel)
            ? throw new ArgumentException("A Mistral OCR model ID is required.", nameof(defaultModel))
            : defaultModel;
        _disposeHttpClient = disposeHttpClient;
    }

    public async Task<DocumentExtractionResult> ExtractAsync(
        Stream document,
        string mediaType,
        DocumentExtractionOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await ExtractPagesAsync(document, mediaType, options, cancellationToken)
            .ToDocumentExtractionResultAsync(cancellationToken)
            .ConfigureAwait(false);

    public async IAsyncEnumerable<DocumentExtractionPageResult> ExtractPagesAsync(
        Stream document,
        string mediaType,
        DocumentExtractionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        if (!document.CanRead)
            throw new ArgumentException("The document stream must be readable.", nameof(document));

        var bytes = await ReadAllBytesAsync(document, cancellationToken).ConfigureAwait(false);
        if (bytes.Length > MaximumDocumentBytes)
            throw new InvalidOperationException("Mistral OCR accepts documents up to 30 MB.");

        var model = string.IsNullOrWhiteSpace(options?.ModelId)
            ? _defaultModel
            : options.ModelId;
        var includeImages = GetOption(options, "mistral.includeImages");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(_endpoint, "providers/mistral/azure/ocr"));
        request.Headers.Add("api-key", _apiKey);
        request.Content = CreateRequestContent(
            model,
            mediaType,
            bytes,
            includeImages);

        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        await using var responseStream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var responseDocument = await JsonDocument
            .ParseAsync(responseStream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var root = responseDocument.RootElement;
        var rawJson = root.GetRawText();
        var pages = root.GetProperty("pages").EnumerateArray().ToArray();
        if (pages.Length == 0)
            throw new InvalidDataException("Mistral OCR did not return any pages.");
        var usage = GetUsage(root, pages.Length);

        for (var index = 0; index < pages.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = MapPage(pages[index], index, rawJson);
            var pagesProcessed = index + 1;
            yield return new DocumentExtractionPageResult(page)
            {
                PagesProcessed = pagesProcessed,
                TotalPages = pages.Length,
                Usage = index == pages.Length - 1 ? usage : null,
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["foundry.mistral.modelId"] = GetString(root, "model") ?? model,
                    ["foundry.mistral.totalPages"] = pages.Length,
                },
            };
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is not null)
            return null;
        if (serviceType.IsInstanceOfType(this))
            return this;
        if (serviceType == typeof(HttpClient))
            return _httpClient;
        if (serviceType == typeof(DocumentExtractionClientMetadata))
        {
            return new DocumentExtractionClientMetadata(
                "foundry.mistral-ocr",
                _endpoint,
                _defaultModel);
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
            _httpClient.Dispose();
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        byte[] requestBytes = await request.Content!
            .ReadAsByteArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        MediaTypeHeaderValue? contentType = request.Content.Headers.ContentType;
        request.Dispose();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var retry = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(_endpoint, "providers/mistral/azure/ocr"));
            retry.Headers.Add("api-key", _apiKey);
            retry.Content = new ByteArrayContent(requestBytes);
            retry.Content.Headers.ContentType = contentType;
            var response = await _httpClient.SendAsync(
                retry,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            retry.Dispose();
            if (response.StatusCode != HttpStatusCode.ServiceUnavailable || attempt == 5)
                return response;

            response.Dispose();
            await Task.Delay(TimeSpan.FromSeconds(attempt * 3), cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException("Mistral OCR retry loop exited unexpectedly.");
    }

    private static HttpContent CreateRequestContent(
        string model,
        string mediaType,
        byte[] document,
        bool includeImages)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model);
            writer.WritePropertyName("document");
            writer.WriteStartObject();
            writer.WriteString("type", "document_url");
            writer.WriteString(
                "document_url",
                $"data:{mediaType};base64,{Convert.ToBase64String(document)}");
            writer.WriteEndObject();
            writer.WriteBoolean("include_image_base64", includeImages);
            writer.WriteBoolean("include_blocks", true);
            writer.WriteString("table_format", "html");
            writer.WriteBoolean("extract_header", true);
            writer.WriteBoolean("extract_footer", true);
            writer.WriteString("confidence_scores_granularity", "page");
            writer.WriteEndObject();
        }
        var content = new ByteArrayContent(stream.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    private static ExtractedDocumentPage MapPage(
        JsonElement source,
        int pageIndex,
        string rawJson)
    {
        var sourceIndex = GetInt32(source, "index") ?? pageIndex;
        var pageNumber = sourceIndex + 1;
        var markdown = GetString(source, "markdown") ?? string.Empty;
        var dimensions = GetDimensions(source);
        var imageLookup = GetImageLookup(source);
        var tableLookup = GetContentLookup(source, "tables");
        var elements = new List<DocumentElement>();

        if (source.TryGetProperty("blocks", out var blocks) &&
            blocks.ValueKind == JsonValueKind.Array)
        {
            var blockIndex = 0;
            foreach (var block in blocks.EnumerateArray())
            {
                elements.Add(MapBlock(
                    block,
                    blockIndex,
                    pageIndex,
                    pageNumber,
                    imageLookup,
                    tableLookup));
                blockIndex++;
            }
        }
        else
        {
            elements.Add(new DocumentBlock(markdown)
            {
                Kind = DocumentBlockKind.Paragraph,
            });
            AppendUnreferencedTables(elements, source, pageIndex);
            AppendUnreferencedImages(elements, source, pageNumber, pageIndex);
        }

        return new ExtractedDocumentPage(pageNumber, markdown)
        {
            Elements = elements,
            Dimensions = dimensions,
            CoordinateUnit = DocumentCoordinateUnit.Pixel,
            CoordinateOrigin = DocumentCoordinateOrigin.TopLeft,
            RawRepresentation = new FoundryMistralOcrRawReference("$.pages", rawJson),
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["foundry.mistral.header"] = GetString(source, "header"),
                ["foundry.mistral.footer"] = GetString(source, "footer"),
                ["foundry.mistral.hyperlinkCount"] =
                    source.TryGetProperty("hyperlinks", out var hyperlinks) &&
                    hyperlinks.ValueKind == JsonValueKind.Array
                        ? hyperlinks.GetArrayLength()
                        : 0,
                ["foundry.mistral.geometryAvailable"] = dimensions is not null,
            },
        };
    }

    private static DocumentElement MapBlock(
        JsonElement block,
        int blockIndex,
        int pageIndex,
        int pageNumber,
        IReadOnlyDictionary<string, JsonElement> images,
        IReadOnlyDictionary<string, JsonElement> tables)
    {
        var type = GetString(block, "type") ?? "text";
        var content = GetString(block, "content") ?? string.Empty;
        var region = GetRegion(block, pageNumber);
        var raw = new FoundryMistralOcrRawReference(
            $"$.pages[{pageIndex}].blocks[{blockIndex}]",
            block.GetRawText());

        if (type == "image")
        {
            var imageId = GetString(block, "image_id");
            var image = imageId is not null && images.TryGetValue(imageId, out var sourceImage)
                ? sourceImage
                : default;
            return new DocumentImage
            {
                Caption = content,
                Content = image.ValueKind == JsonValueKind.Object
                    ? GetImageContent(image)
                    : null,
                BoundingRegion = region,
                RawRepresentation = raw,
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["foundry.mistral.imageId"] = imageId,
                },
            };
        }

        if (type == "table")
        {
            var tableId = GetString(block, "table_id");
            var tableContent = tableId is not null && tables.TryGetValue(tableId, out var sourceTable)
                ? GetString(sourceTable, "content") ?? content
                : content;
            return new DocumentTable(0, 0, markdownRepresentation: tableContent)
            {
                BoundingRegion = region,
                RawRepresentation = raw,
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["foundry.mistral.tableId"] = tableId,
                    ["foundry.mistral.tableFormat"] = "html",
                },
            };
        }

        return new DocumentBlock(content)
        {
            Kind = MapBlockKind(type),
            Confidence = GetConfidence(block),
            BoundingRegion = region,
            RawRepresentation = raw,
        };
    }

    private static DocumentBlockKind MapBlockKind(string type) =>
        type switch
        {
            "title" => DocumentBlockKind.Title,
            "text" => DocumentBlockKind.Paragraph,
            _ => new DocumentBlockKind(type),
        };

    private static void AppendUnreferencedTables(
        List<DocumentElement> elements,
        JsonElement page,
        int pageIndex)
    {
        if (!page.TryGetProperty("tables", out var tables) ||
            tables.ValueKind != JsonValueKind.Array)
            return;

        var index = 0;
        foreach (var table in tables.EnumerateArray())
        {
            elements.Add(new DocumentTable(
                0,
                0,
                markdownRepresentation: GetString(table, "content") ?? string.Empty)
            {
                RawRepresentation = new FoundryMistralOcrRawReference(
                    $"$.pages[{pageIndex}].tables[{index}]",
                    table.GetRawText()),
            });
            index++;
        }
    }

    private static void AppendUnreferencedImages(
        List<DocumentElement> elements,
        JsonElement page,
        int pageNumber,
        int pageIndex)
    {
        if (!page.TryGetProperty("images", out var images) ||
            images.ValueKind != JsonValueKind.Array)
            return;

        var index = 0;
        foreach (var image in images.EnumerateArray())
        {
            elements.Add(new DocumentImage
            {
                Content = GetImageContent(image),
                BoundingRegion = GetRegion(image, pageNumber),
                RawRepresentation = new FoundryMistralOcrRawReference(
                    $"$.pages[{pageIndex}].images[{index}]",
                    image.GetRawText()),
            });
            index++;
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> GetImageLookup(JsonElement page) =>
        GetLookup(page, "images", "id", "image_id");

    private static IReadOnlyDictionary<string, JsonElement> GetContentLookup(
        JsonElement page,
        string propertyName) =>
        GetLookup(page, propertyName, "id", "table_id");

    private static IReadOnlyDictionary<string, JsonElement> GetLookup(
        JsonElement page,
        string propertyName,
        params string[] idNames)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!page.TryGetProperty(propertyName, out var values) ||
            values.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var value in values.EnumerateArray())
        {
            var id = idNames.Select(name => GetString(value, name))
                .FirstOrDefault(static candidate => !string.IsNullOrWhiteSpace(candidate));
            if (id is not null)
                result[id] = value.Clone();
        }
        return result;
    }

    private static DataContent? GetImageContent(JsonElement image)
    {
        var raw = GetString(image, "image_base64");
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? new DataContent(raw)
            : new DataContent(Convert.FromBase64String(raw), "image/png");
    }

    private static DocumentBoundingRegion? GetRegion(
        JsonElement element,
        int pageNumber)
    {
        var left = GetSingle(element, "top_left_x");
        var top = GetSingle(element, "top_left_y");
        var right = GetSingle(element, "bottom_right_x");
        var bottom = GetSingle(element, "bottom_right_y");
        return left is not null && top is not null && right is not null && bottom is not null
            ? DocumentBoundingRegion.FromRectangle(
                pageNumber,
                left.Value,
                top.Value,
                right.Value,
                bottom.Value)
            : null;
    }

    private static DocumentPageDimensions? GetDimensions(JsonElement page)
    {
        if (!page.TryGetProperty("dimensions", out var dimensions) ||
            dimensions.ValueKind != JsonValueKind.Object)
            return null;
        var width = GetSingle(dimensions, "width");
        var height = GetSingle(dimensions, "height");
        return width is not null && height is not null
            ? new(width.Value, height.Value)
            : null;
    }

    private static double? GetConfidence(JsonElement block)
    {
        if (!block.TryGetProperty("confidence_scores", out var scores) ||
            scores.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var name in new[] { "confidence", "page_confidence", "block_confidence" })
        {
            if (scores.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.Number)
                return value.GetDouble();
        }
        return null;
    }

    private static DocumentExtractionUsage GetUsage(
        JsonElement root,
        int pageCount)
    {
        var usage = new DocumentExtractionUsage
        {
            PagesProcessed = pageCount,
        };
        if (root.TryGetProperty("usage_info", out var usageInfo) &&
            usageInfo.ValueKind == JsonValueKind.Object)
        {
            usage.AdditionalProperties = new AdditionalPropertiesDictionary();
            foreach (var property in usageInfo.EnumerateObject())
            {
                usage.AdditionalProperties[property.Name] =
                    property.Value.ValueKind == JsonValueKind.Number
                        ? property.Value.GetDouble()
                        : property.Value.ToString();
            }
        }
        return usage;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string? message = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
                message = GetString(error, "message");
        }
        catch (JsonException)
        {
        }
        throw new HttpRequestException(
            $"Mistral OCR returned {(int)response.StatusCode} ({response.ReasonPhrase}): " +
            (message ?? body));
    }

    private static async Task<byte[]> ReadAllBytesAsync(
        Stream document,
        CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await document.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }

    private static bool GetOption(
        DocumentExtractionOptions? options,
        string key) =>
        options?.AdditionalProperties?.TryGetValue(key, out var value) == true &&
        value is true;

    private static Uri NormalizeEndpoint(Uri endpoint) =>
        endpoint.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? endpoint
            : new Uri(endpoint.AbsoluteUri + "/", UriKind.Absolute);

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt32(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static float? GetSingle(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number
            ? value.GetSingle()
            : null;
}
