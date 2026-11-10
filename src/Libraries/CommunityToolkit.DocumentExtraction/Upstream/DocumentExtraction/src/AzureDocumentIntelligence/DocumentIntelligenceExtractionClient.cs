// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Core;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace CommunityToolkit.DocumentExtraction.AzureDocumentIntelligence;

/// <summary>
/// A <see cref="DocumentExtractionClient"/> backed by Azure Document Intelligence.
/// </summary>
public sealed class DocumentIntelligenceExtractionClient : DocumentExtractionClient
{
    private const string ProviderName = "azure-document-intelligence";

    private readonly DocumentIntelligenceClient _client;
    private readonly string _defaultModel;
    private readonly bool _includeImages;

    /// <summary>Creates an adapter using the supplied Azure endpoint and credential.</summary>
    public DocumentIntelligenceExtractionClient(
        Uri endpoint,
        TokenCredential credential,
        DocumentIntelligenceExtractionOptions? options = null,
        DocumentIntelligenceClientOptions? clientOptions = null)
        : this(new DocumentIntelligenceClient(
            endpoint, credential, clientOptions ?? new DocumentIntelligenceClientOptions()), options)
    {
    }

    /// <summary>Creates an adapter around a borrowed SDK client.</summary>
    public DocumentIntelligenceExtractionClient(
        DocumentIntelligenceClient client, DocumentIntelligenceExtractionOptions? options = null)
    {
        Throw.IfNull(client);
        string defaultModel = options is null ? "prebuilt-layout" : options.DefaultModel;
        Throw.IfNullOrWhitespace(defaultModel);
        _client = client;
        _defaultModel = defaultModel;
        _includeImages = options?.IncludeImages ?? false;
    }

    /// <inheritdoc/>
    public override async Task<Microsoft.Extensions.Documents.ExtractionDocument> ExtractAsync(
        Stream document,
        string identifier,
        string mediaType,
        DocumentExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ProviderAdapterHelpers.ValidateInput(document, identifier, mediaType);
        DocumentExtractionOptions normalizedOptions = ProviderAdapterHelpers.NormalizeOptions(options);
        ProviderAdapterHelpers.RejectUnsupportedProperties(
            normalizedOptions,
            nameof(DocumentIntelligenceExtractionClient),
            "locale",
            "pages",
            "features");
        string model = ProviderAdapterHelpers.ResolveExtractor(
            normalizedOptions,
            _defaultModel,
            nameof(DocumentIntelligenceExtractionClient));

        using var buffer = new MemoryStream();
        await document.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);

        var analyzeOptions = new AnalyzeDocumentOptions(model, BinaryData.FromBytes(buffer.ToArray()))
        {
            OutputContentFormat = DocumentContentFormat.Markdown,
            Locale = ProviderAdapterHelpers.GetProperty<string>(normalizedOptions, "locale"),
            Pages = ProviderAdapterHelpers.GetProperty<string>(normalizedOptions, "pages"),
        };
        AddFeatures(
            analyzeOptions,
            ProviderAdapterHelpers.GetProperty<IEnumerable<string>>(normalizedOptions, "features"));
        if (_includeImages)
        {
            analyzeOptions.Output.Add(AnalyzeOutputOption.Figures);
        }

        Operation<AnalyzeResult> operation = await _client
            .AnalyzeDocumentAsync(WaitUntil.Completed, analyzeOptions, cancellationToken)
            .ConfigureAwait(false);
        AnalyzeResult result = operation.Value;
        using Response rawResponse = operation.GetRawResponse();
        var output = new Microsoft.Extensions.Documents.ExtractionDocument(identifier)
        {
            RawRepresentation = result,
        };
            IReadOnlyDictionary<int, PageFrame> frames = result.Pages.ToDictionary(
                page => page.PageNumber,
                page =>
                {
                    (ExtractionDocumentSourcePage sourcePage, _) =
                        ProviderAdapterHelpers.CreatePageFrame(
                            identifier,
                            ProviderName,
                            page.PageNumber,
                            page.Width,
                            page.Height,
                            MapUnit(page.Unit),
                            ExtractionDocumentCoordinateOrigin.TopLeft);
                    var section = new ExtractionDocumentSection();
                    section.Sources.Add(new ExtractionDocumentSourceReference(sourcePage, rawRepresentation: page));
                    foreach (DocumentSelectionMark mark in page.SelectionMarks ?? [])
                    {
                        section.Sources.Add(ProviderAdapterHelpers.CreateSource(sourcePage, mark.Polygon, mark)!);
                    }

                    foreach (DocumentBarcode barcode in page.Barcodes ?? [])
                    {
                        section.Sources.Add(ProviderAdapterHelpers.CreateSource(sourcePage, barcode.Polygon, barcode)!);
                    }

                    output.SourcePages.Add(sourcePage);
                    output.Sections.Add(section);
                    return new PageFrame(sourcePage, section);
                });

            var elements = new Dictionary<string, ExtractionDocumentElement>(StringComparer.Ordinal);
            MapParagraphs(output, result, frames, elements);
            MapTables(output, result, frames, elements);
                await MapImagesAsync(
                    result,
                    operation.Id,
                    model,
                    output,
                    frames,
                    elements,
                    cancellationToken).ConfigureAwait(false);
            if (result.Paragraphs.Count == 0)
            {
                foreach (var page in result.Pages)
                {
                    foreach (DocumentLine line in page.Lines)
                    {
                        ProviderAdapterHelpers.AddElement(frames[page.PageNumber].Section,
                            new ExtractionDocumentTextElement(new TextContent(line.Content)),
                            [ProviderAdapterHelpers.CreateSource(frames[page.PageNumber].SourcePage, line.Polygon, line)]);
                    }
                }
            }
            MapSections(output, result, frames, elements);
            MapFigureText(output, result, elements);
            OrderContent(output, result, elements);

            cancellationToken.ThrowIfCancellationRequested();
            return output;
    }

    private static void MapParagraphs(
        ExtractionDocument output,
        AnalyzeResult result,
        IReadOnlyDictionary<int, PageFrame> frames,
        Dictionary<string, ExtractionDocumentElement> elements)
    {
        foreach (var (paragraph, index) in (result.Paragraphs ?? []).Select((value, index) => (value, index)))
        {
            string text = paragraph.Content ?? string.Empty;
            ExtractionDocumentSourceReference[] sources = paragraph.BoundingRegions
                .Select(region => ProviderAdapterHelpers.CreateSource(
                    frames[region.PageNumber].SourcePage, region.Polygon, paragraph))
                .ToArray();
            var element = new ExtractionDocumentTextElement(new TextContent(text), role: ToTextRole(paragraph.Role));
            elements.Add($"/paragraphs/{index}", element);
            ProviderAdapterHelpers.AddElement(ProviderAdapterHelpers.GetSection(output, sources), element, sources);
        }
    }

    private static void MapTables(
        ExtractionDocument output,
        AnalyzeResult result,
        IReadOnlyDictionary<int, PageFrame> frames,
        Dictionary<string, ExtractionDocumentElement> elements)
    {
        foreach (var (table, index) in (result.Tables ?? []).Select((value, index) => (value, index)))
        {
            ExtractionDocumentSourceReference[] tableSources = table.BoundingRegions
                .Select(region => ProviderAdapterHelpers.CreateSource(
                    frames[region.PageNumber].SourcePage,
                    region.Polygon, table)!)
                .ToArray();

            var mappedTable = new ExtractionDocumentTableElement(null, table.RowCount, table.ColumnCount);
            elements.Add($"/tables/{index}", mappedTable);
            foreach (Azure.AI.DocumentIntelligence.DocumentTableCell cell in table.Cells)
            {
                ExtractionDocumentSourceReference[] cellSources = cell.BoundingRegions
                    .Select(region => ProviderAdapterHelpers.CreateSource(
                        frames[region.PageNumber].SourcePage,
                        region.Polygon, cell)!)
                    .ToArray();
                string? kind = cell.Kind.ToString();
                if (string.IsNullOrEmpty(kind))
                {
                    kind = null;
                }

                ExtractionDocumentElement? content = cell.Elements.Count > 0
                    ? new ExtractionDocumentSection()
                    : cell.Content is { } text ? new ExtractionDocumentTextElement(new TextContent(text)) : null;
                foreach (ExtractionDocumentSourceReference source in cellSources)
                {
                    content?.Sources.Add(source);
                }

                var mappedCell = content is null
                    ? new Microsoft.Extensions.Documents.ExtractionDocumentTableCell(
                        cell.RowSpan ?? 1, cell.ColumnSpan ?? 1, kind)
                    : new Microsoft.Extensions.Documents.ExtractionDocumentTableCell(
                        content, cell.RowSpan ?? 1, cell.ColumnSpan ?? 1, kind);
                foreach (ExtractionDocumentSourceReference source in cellSources)
                {
                    mappedCell.Sources.Add(source);
                }

                mappedTable.Cells[cell.RowIndex, cell.ColumnIndex] = mappedCell;
            }
            ProviderAdapterHelpers.AddElement(ProviderAdapterHelpers.GetSection(output, tableSources), mappedTable, tableSources);
        }
    }

    private async Task MapImagesAsync(
        AnalyzeResult result,
        string operationId,
        string model,
        ExtractionDocument output,
        IReadOnlyDictionary<int, PageFrame> frames,
        Dictionary<string, ExtractionDocumentElement> elements,
        CancellationToken cancellationToken)
    {
        foreach (var (figure, index) in (result.Figures ?? []).Select((value, index) => (value, index)))
        {
            ExtractionDocumentSourceReference[] sources = figure.BoundingRegions
                .Select(region => ProviderAdapterHelpers.CreateSource(
                    frames[region.PageNumber].SourcePage,
                    region.Polygon, figure)!)
                .ToArray();
            ReadOnlyMemory<byte>? content = null;
            if (_includeImages && !string.IsNullOrWhiteSpace(figure.Id))
            {
                Response<BinaryData> figureResponse = await _client
                    .GetAnalyzeResultFigureAsync(model, operationId, figure.Id, cancellationToken)
                    .ConfigureAwait(false);
                using Response figureRawResponse = figureResponse.GetRawResponse();
                content = figureResponse.Value.ToMemory().ToArray();
            }

            var image = new ExtractionDocumentImageElement()
            {
                Content = content is null ? null : new DataContent(content.Value, "image/png"),
                AlternativeText = figure.Caption?.Content,
            };
            elements.Add($"/figures/{index}", image);

            ProviderAdapterHelpers.AddElement(ProviderAdapterHelpers.GetSection(output, sources), image, sources);
        }
    }

    private static void MapSections(
        ExtractionDocument output, AnalyzeResult result, IReadOnlyDictionary<int, PageFrame> frames,
        IReadOnlyDictionary<string, ExtractionDocumentElement> elements)
    {
        var sections = new List<(ExtractionDocumentSection, IReadOnlyList<string>)>();
        foreach (Azure.AI.DocumentIntelligence.DocumentSection native in result.Sections ?? [])
        {
            var section = new ExtractionDocumentSection();
            foreach (var page in result.Pages)
            {
                if ((native.Spans ?? []).Any(span => (page.Spans ?? []).Any(pageSpan =>
                    pageSpan.Offset < (long)span.Offset + span.Length && span.Offset < (long)pageSpan.Offset + pageSpan.Length)))
                {
                    section.Sources.Add(new ExtractionDocumentSourceReference(frames[page.PageNumber].SourcePage,
                        rawRepresentation: native));
                }
            }

            sections.Add((section, native.Elements.ToArray()));
        }

        var cells = new List<SectionHierarchy.CellContent>();
        for (int index = 0; index < result.Tables.Count; index++)
        {
            var table = (ExtractionDocumentTableElement)elements[$"/tables/{index}"];
            foreach (Azure.AI.DocumentIntelligence.DocumentTableCell cell in result.Tables[index].Cells)
            {
                if (cell.Elements.Count > 0)
                {
                    cells.Add(new SectionHierarchy.CellContent(
                        table, table.Cells[cell.RowIndex, cell.ColumnIndex]!, cell.Elements.ToArray()));
                }
            }
        }

        SectionHierarchy.Apply(output, elements, sections, cells);
    }

    private static void MapFigureText(
        ExtractionDocument output, AnalyzeResult result,
        IReadOnlyDictionary<string, ExtractionDocumentElement> elements)
    {
        var figures = result.Figures.Select((figure, index) => new FigureTextMapping.FigureContent(
            (ExtractionDocumentImageElement)elements[$"/figures/{index}"], figure.Elements.ToArray())).ToArray();
        DocumentCaption[] captions = result.Figures
            .SelectMany(figure => figure.Caption is { } caption ? new[] { caption } : [])
            .Concat(result.Tables.SelectMany(table => table.Caption is { } caption ? new[] { caption } : [])).ToArray();
        DocumentFootnote[] footnotes = result.Figures.SelectMany(figure => figure.Footnotes)
            .Concat(result.Tables.SelectMany(table => table.Footnotes)).ToArray();
        IEnumerable<string> owned = result.Sections.SelectMany(section => section.Elements)
            .Concat(result.Tables.SelectMany(table => table.Cells).SelectMany(cell => cell.Elements))
            .Concat(captions.SelectMany(caption => caption.Elements))
            .Concat(footnotes.SelectMany(footnote => footnote.Elements));
        DocumentSpan[] excludedSpans = captions.SelectMany(caption => caption.Spans ?? [])
            .Concat(footnotes.SelectMany(footnote => footnote.Spans ?? []))
            .Concat(result.Tables.SelectMany(table => table.Cells).SelectMany(cell => cell.Spans ?? [])).ToArray();
        owned = owned.Concat(result.Paragraphs.Select((paragraph, index) => (paragraph, index))
            .Where(item => (item.paragraph.Spans ?? []).Any(span => excludedSpans.Any(excluded =>
                span.Offset < (long)excluded.Offset + excluded.Length && excluded.Offset < (long)span.Offset + span.Length)))
            .Select(item => $"/paragraphs/{item.index}"));
        IEnumerable<string?> descriptions = captions.Select(caption => caption.Content)
            .Concat(footnotes.Select(footnote => footnote.Content));
        FigureTextMapping.Apply(output, elements, figures, owned, descriptions);
    }

    private static void OrderContent(
        ExtractionDocument output, AnalyzeResult result,
        IReadOnlyDictionary<string, ExtractionDocumentElement> elements)
    {
        var offsets = new Dictionary<ExtractionDocumentElement, int>();
        for (int index = 0; index < result.Paragraphs.Count; index++)
        {
            offsets.Add(elements[$"/paragraphs/{index}"], (result.Paragraphs[index].Spans ?? []).Select(span => span.Offset).DefaultIfEmpty(int.MaxValue).Min());
        }

        for (int index = 0; index < result.Tables.Count; index++)
        {
            offsets.Add(elements[$"/tables/{index}"], (result.Tables[index].Spans ?? []).Select(span => span.Offset).DefaultIfEmpty(int.MaxValue).Min());
        }

        for (int index = 0; index < result.Figures.Count; index++)
        {
            offsets.Add(elements[$"/figures/{index}"], (result.Figures[index].Spans ?? []).Select(span => span.Offset).DefaultIfEmpty(int.MaxValue).Min());
        }

        SectionHierarchy.Order(output, offsets);
    }


    private static ExtractionDocumentCoordinateUnit? MapUnit(LengthUnit? unit)
        => unit?.ToString() switch
        {
            "pixel" or "Pixel" => ExtractionDocumentCoordinateUnit.Pixel,
            "inch" or "Inch" => ExtractionDocumentCoordinateUnit.Inch,
            null => null,
            string value => throw new NotSupportedException(
                $"Azure Document Intelligence coordinate unit '{value}' is not supported."),
        };

    private static void AddFeatures(
        AnalyzeDocumentOptions options,
        IEnumerable<string>? requestedFeatures)
    {
        if (requestedFeatures is null)
        {
            return;
        }

        foreach (string feature in requestedFeatures)
        {
            options.Features.Add(feature.ToLowerInvariant() switch
            {
                "ocrhighresolution" => DocumentAnalysisFeature.OcrHighResolution,
                "languages" => DocumentAnalysisFeature.Languages,
                "barcodes" => DocumentAnalysisFeature.Barcodes,
                "formulas" => DocumentAnalysisFeature.Formulas,
                "keyvaluepairs" => DocumentAnalysisFeature.KeyValuePairs,
                "fontstyling" => DocumentAnalysisFeature.FontStyling,
                "queryfields" => DocumentAnalysisFeature.QueryFields,
                _ => throw new NotSupportedException(
                    $"Azure Document Intelligence feature '{feature}' is not supported by this adapter."),
            });
        }
    }

    internal static string? ToTextRole(ParagraphRole? role)
        => ProviderAdapterHelpers.NormalizeTextRole(role?.ToString());

    private sealed record PageFrame(
        ExtractionDocumentSourcePage SourcePage,
        ExtractionDocumentSection Section);

    /// <inheritdoc/>
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        Throw.IfNull(serviceType);
        return serviceKey is null && serviceType == typeof(DocumentIntelligenceClient)
            ? _client : base.GetService(serviceType, serviceKey);
    }
}
