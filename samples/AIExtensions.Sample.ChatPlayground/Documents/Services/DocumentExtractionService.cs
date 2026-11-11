using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Displays the shared exchange model without implicitly inspecting native objects.</summary>
public sealed class DocumentExtractionService
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    public async Task<DocumentExtractionRunResult> ExtractAsync(
        DocumentExtractionClient client, InputFile input, DocumentExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(input);
        using var source = new MemoryStream(input.Bytes, writable: false);
        var document = await client.ExtractAsync(source, input.FileName, input.MediaType, options, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new(document, JsonSerializer.Serialize(new
        {
            document.Identifier,
            Content = Payload(document.Content),
            SourcePages = document.SourcePages.Select(Frame).ToArray(),
            Sections = document.Sections.Select(section => Element(section, new(ReferenceEqualityComparer.Instance))).ToArray(),
        }, s_json));
    }

    private static object? Payload(AIContent? content) => content switch
    {
        null => null,
        TextContent text => new { Type = nameof(TextContent), text.Text },
        DataContent data => new { Type = nameof(DataContent), data.MediaType, ContentLength = data.Data.Length },
        UriContent uri => new { Type = nameof(UriContent), uri.Uri, uri.MediaType },
        _ => new { Type = content.GetType().Name },
    };

    private static object Frame(ExtractionDocumentSourcePage frame) => new
    {
        frame.Identifier, frame.PageNumber, frame.Dimensions,
        CoordinateUnit = frame.CoordinateUnit?.ToString(), CoordinateOrigin = frame.CoordinateOrigin?.ToString(),
    };

    private static object Sources(IEnumerable<ExtractionDocumentSourceReference> sources) =>
        sources.Select(source => new { Page = Frame(source.Page), source.Polygon }).ToArray();

    private static object Element(ExtractionDocumentElement element, HashSet<object> active)
    {
        if (!active.Add(element))
            throw new InvalidDataException("The extracted document has a cyclic hierarchy.");
        try
        {
            object? structure = element switch
            {
                ExtractionDocumentTextElement text => new { text.Role, text.HeadingLevel },
                ExtractionDocumentImageElement image => new { image.Text, image.AlternativeText, image.Source },
                ExtractionDocumentSection section => new
                {
                    Elements = section.Elements.Select(child => Element(child, active)).ToArray(),
                },
                ExtractionDocumentTableElement table => Table(table, active),
                _ => null,
            };
            return new
            {
                Type = element.GetType().Name, Content = Payload(element.Content),
                Sources = Sources(element.Sources), Structure = structure,
            };
        }
        finally { active.Remove(element); }
    }

    private static object Table(ExtractionDocumentTableElement table, HashSet<object> active)
    {
        var covered = new bool[table.Cells.RowCount, table.Cells.ColumnCount];
        foreach (var cell in table.Cells)
            if (cell is not null)
                for (var row = cell.RowIndex; row < cell.RowIndex + cell.RowSpan; row++)
                    for (var column = cell.ColumnIndex; column < cell.ColumnIndex + cell.ColumnSpan; column++)
                        covered[row, column] = row != cell.RowIndex || column != cell.ColumnIndex;
        return new
        {
            table.Cells.RowCount, table.Cells.ColumnCount,
            Cells = Enumerable.Range(0, table.Cells.RowCount).SelectMany(row =>
                Enumerable.Range(0, table.Cells.ColumnCount).Select(column =>
                {
                    var cell = table.Cells[row, column];
                    return new
                    {
                        Row = row, Column = column, IsCovered = covered[row, column],
                        State = covered[row, column] ? "covered" : cell is null ? "missing" : cell.Content is null ? "empty" : "content",
                        cell?.RowSpan, cell?.ColumnSpan, cell?.Role,
                        Content = cell?.Content is { } child ? Element(child, active) : null,
                        Sources = cell is null ? null : Sources(cell.Sources),
                    };
                })).ToArray(),
        };
    }
}

/// <summary>Keeps the non-disposable exchange document available for deliberate native inspection.</summary>
public sealed class DocumentExtractionRunResult(ExtractionDocument document, string output)
{
    public int PageCount => document.SourcePages.Count;
    public string Output { get; } = output;
    public bool HasRawOutput => document.RawRepresentation is not null || document.SourcePages.Count > 0;

    public async Task<string> InspectRawAsync(CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await JsonSerializer.SerializeAsync(buffer, NativeObjects(),
            new JsonSerializerOptions { WriteIndented = true }, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private IEnumerable<object> NativeObjects()
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        if (document.RawRepresentation is { } raw && seen.Add(raw))
            yield return raw;
        if (document.Content?.RawRepresentation is { } payload && seen.Add(payload))
            yield return payload;
        foreach (var section in document.Sections)
            foreach (var value in Visit(section, new(ReferenceEqualityComparer.Instance)))
                if (seen.Add(value))
                    yield return value;
    }

    private static IEnumerable<object> Visit(ExtractionDocumentElement element, HashSet<object> active)
    {
        if (!active.Add(element))
            throw new InvalidDataException("The extracted document has a cyclic hierarchy.");
        try
        {
            if (element.Content?.RawRepresentation is { } raw)
                yield return raw;
            foreach (var source in element.Sources)
                if (source.GetRawRepresentation() is { } native)
                    yield return native;
            if (element is ExtractionDocumentSection section)
                foreach (var child in section.Elements)
                    foreach (var value in Visit(child, active))
                        yield return value;
            if (element is ExtractionDocumentTableElement table)
                foreach (var cell in table.Cells)
                    if (cell is not null)
                    {
                        foreach (var source in cell.Sources)
                            if (source.GetRawRepresentation() is { } native)
                                yield return native;
                        if (cell.Content is { } child)
                            foreach (var value in Visit(child, active))
                                yield return value;
                    }
        }
        finally { active.Remove(element); }
    }
}
