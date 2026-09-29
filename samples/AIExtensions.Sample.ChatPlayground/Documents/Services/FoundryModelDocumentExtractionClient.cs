using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using ExtractedDocumentPage = Microsoft.Extensions.DocumentExtraction.DocumentPage;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Uses a vision-capable deployed Foundry model as a low-geometry document extractor.</summary>
internal sealed class FoundryModelDocumentExtractionClient(
    IChatClient chatClient,
    string deploymentName,
    bool disposeChatClient = false)
    : IDocumentExtractionClient
{
    private readonly IChatClient _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
    private readonly string _deploymentName = string.IsNullOrWhiteSpace(deploymentName)
        ? throw new ArgumentException("A Foundry model deployment name is required.", nameof(deploymentName))
        : deploymentName;

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
        if (mediaType is not ("application/pdf" or "image/png" or "image/jpeg"))
        {
            throw new NotSupportedException(
                $"The Foundry model provider supports PDF, PNG, and JPEG input; '{mediaType}' is not supported.");
        }

        var bytes = await ReadAllBytesAsync(document, cancellationToken).ConfigureAwait(false);

        // A general vision model can provide semantic structure, but it must not invent OCR geometry or confidence.
        var prompt = """
            Extract this document into the supplied JSON schema.

            Requirements:
            - Preserve the document's page boundaries and one-based page numbers.
            - Transcribe each page accurately.
            - Return reading-order text blocks with concise kinds such as title, sectionHeading, paragraph, listItem, pageHeader, pageFooter, or footnote.
            - Return every table with row/column counts and cells.
            - Do not invent text, confidence scores, coordinates, or missing pages.
            - This schema intentionally has no geometry because a general multimodal model cannot provide provider-grade polygons reliably.
            """;
        var message = new ChatMessage(
            ChatRole.User,
            [
                new TextContent(prompt),
                new DataContent(bytes, mediaType),
            ]);
        var chatOptions = new ChatOptions
        {
            ModelId = string.IsNullOrWhiteSpace(options?.ModelId)
                ? _deploymentName
                : options.ModelId,
            ResponseFormat = ChatResponseFormat.ForJsonSchema<FoundryDocumentResponse>(
                FoundryDocumentJsonContext.Default.Options),
        };

        var response = await _chatClient
            .GetResponseAsync([message], chatOptions, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        using var responseJson = JsonDocument.Parse(response.Text);
        var rawResponse = responseJson.RootElement.Clone();
        var result = responseJson.RootElement.Deserialize(FoundryDocumentJsonContext.Default.FoundryDocumentResponse)
            ?? throw new InvalidDataException("The Foundry model returned an empty document result.");

        if (result.Pages.Count == 0)
            throw new InvalidDataException("The Foundry model did not return any pages.");

        for (var index = 0; index < result.Pages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = MapPage(result.Pages[index], index, rawResponse);
            var pagesProcessed = index + 1;

            yield return new DocumentExtractionPageResult(page)
            {
                PagesProcessed = pagesProcessed,
                TotalPages = result.Pages.Count,
                Usage = index == result.Pages.Count - 1
                    ? new DocumentExtractionUsage
                    {
                        PagesProcessed = result.Pages.Count,
                        InputTokenCount = ToTokenCount(response.Usage?.InputTokenCount),
                        OutputTokenCount = ToTokenCount(response.Usage?.OutputTokenCount),
                        TotalTokenCount = ToTokenCount(response.Usage?.TotalTokenCount),
                    }
                    : null,
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["foundry.model.deployment"] = _deploymentName,
                    ["foundry.model.geometryAvailable"] = false,
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
        if (serviceType == typeof(IChatClient))
            return _chatClient;
        if (serviceType == typeof(DocumentExtractionClientMetadata))
        {
            return new DocumentExtractionClientMetadata(
                "foundry-model",
                defaultModelId: _deploymentName);
        }
        return null;
    }

    public void Dispose()
    {
        if (disposeChatClient)
            _chatClient.Dispose();
    }

    private static ExtractedDocumentPage MapPage(FoundryDocumentPage source, int pageIndex, JsonElement rawResponse)
    {
        var elements = new List<DocumentElement>();

        foreach (var block in source.Blocks)
        {
            elements.Add(new DocumentBlock(block.Text)
            {
                Kind = MapBlockKind(block.Kind),
                RawRepresentation = JsonSerializer.SerializeToElement(
                    block,
                    FoundryDocumentJsonContext.Default.FoundryDocumentBlock),
            });
        }

        foreach (var table in source.Tables)
        {
            elements.Add(new DocumentTable(
                table.RowCount,
                table.ColumnCount,
                table.Cells.Select(cell => new DocumentTableCell(
                    cell.RowIndex,
                    cell.ColumnIndex,
                    cell.Content)
                {
                    RowSpan = Math.Max(1, cell.RowSpan),
                    ColumnSpan = Math.Max(1, cell.ColumnSpan),
                    Kind = MapCellKind(cell.Kind),
                    RawRepresentation = JsonSerializer.SerializeToElement(
                        cell,
                        FoundryDocumentJsonContext.Default.FoundryDocumentCell),
                }).ToArray())
            {
                RawRepresentation = JsonSerializer.SerializeToElement(
                    table,
                    FoundryDocumentJsonContext.Default.FoundryDocumentTable),
            });
        }

        return new ExtractedDocumentPage(
            source.PageNumber > 0 ? source.PageNumber : pageIndex + 1,
            source.Text)
        {
            Elements = elements,
            RawRepresentation = rawResponse,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["foundry.model.geometryAvailable"] = false,
                ["foundry.model.extractionMode"] = "structured-output",
            },
        };
    }

    private static DocumentBlockKind MapBlockKind(string kind) =>
        kind switch
        {
            "title" => DocumentBlockKind.Title,
            "paragraph" => DocumentBlockKind.Paragraph,
            _ when string.IsNullOrWhiteSpace(kind) => DocumentBlockKind.Paragraph,
            _ => new DocumentBlockKind(kind),
        };

    private static DocumentTableCellKind? MapCellKind(string kind) =>
        kind switch
        {
            "columnHeader" => DocumentTableCellKind.ColumnHeader,
            "rowHeader" => DocumentTableCellKind.RowHeader,
            "rowSection" => DocumentTableCellKind.RowSection,
            "content" => DocumentTableCellKind.Content,
            "" => null,
            _ => new DocumentTableCellKind(kind),
        };

    private static async Task<byte[]> ReadAllBytesAsync(
        Stream document,
        CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await document.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }

    private static int? ToTokenCount(long? value) =>
        value is null ? null : checked((int)value.Value);
}

[Description("Structured document extraction produced by a vision-capable Foundry model.")]
internal sealed class FoundryDocumentResponse
{
    [Description("Every document page in source order.")]
    public List<FoundryDocumentPage> Pages { get; set; } = [];
}

internal sealed class FoundryDocumentPage
{
    [Description("The one-based source page number.")]
    public int PageNumber { get; set; }

    [Description("Complete transcribed text for this page in reading order.")]
    public string Text { get; set; } = string.Empty;

    [Description("Reading-order text blocks outside or inside document structure.")]
    public List<FoundryDocumentBlock> Blocks { get; set; } = [];

    [Description("Tables found on the page.")]
    public List<FoundryDocumentTable> Tables { get; set; } = [];
}

internal sealed class FoundryDocumentBlock
{
    [Description("A concise semantic kind such as title, sectionHeading, paragraph, listItem, pageHeader, pageFooter, or footnote.")]
    public string Kind { get; set; } = "paragraph";

    [Description("The exact block text.")]
    public string Text { get; set; } = string.Empty;
}

internal sealed class FoundryDocumentTable
{
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public List<FoundryDocumentCell> Cells { get; set; } = [];
}

internal sealed class FoundryDocumentCell
{
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
    public int RowSpan { get; set; } = 1;
    public int ColumnSpan { get; set; } = 1;

    [Description("columnHeader, rowHeader, rowSection, content, or an empty string when unknown.")]
    public string Kind { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(FoundryDocumentResponse))]
[JsonSerializable(typeof(FoundryDocumentPage))]
[JsonSerializable(typeof(FoundryDocumentBlock))]
[JsonSerializable(typeof(FoundryDocumentTable))]
[JsonSerializable(typeof(FoundryDocumentCell))]
internal sealed partial class FoundryDocumentJsonContext : JsonSerializerContext;
