using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Finalizes extraction and transfers ownership to an explicit inspection handle.</summary>
public sealed class DocumentExtractionService
{
    private const int MaximumInspectionBytes = 128 * 1024;
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    public async Task<DocumentExtractionRunResult> ExtractAsync(
        IDocumentExtractionClient client, SelectedDocument input, bool streamPages,
        DocumentExtractionOptions? options = null, IProgress<DocumentExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        var request = options?.Clone();
        using var source = new MemoryStream(input.Bytes, writable: false);
        DocumentExtractionResult extraction;
        if (streamPages)
        {
            using var builder = new DocumentExtractionDocumentBuilder(input.FileName, request?.CaptureRawOutput ?? true);
            var completed = 0;
            await foreach (var page in client.ExtractPagesAsync(source, input.FileName, input.MediaType, request, cancellationToken)
                .ConfigureAwait(false))
            {
                builder.AddPage(page);
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new(++completed));
            }
            cancellationToken.ThrowIfCancellationRequested();
            extraction = builder.Build();
        }
        else
            extraction = await client.ExtractAsync(source, input.FileName, input.MediaType, request, cancellationToken)
                .ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = extraction.Document;
            var output = JsonSerializer.Serialize(new
            {
                document.Identifier,
                document.StoredMarkdown,
                Pages = extraction.Pages.Select(page => new
                {
                    page.PageNumber,
                    Observations = page.Observations.Select(item => new { item.ProviderId, item.Order }).ToArray(),
                }).ToArray(),
                SourcePages = document.SourcePages.Select(Frame).ToArray(),
                SourceOutputs = document.SourceOutputs.Select(Source).ToArray(),
                Sections = document.Sections.Select(Section).ToArray(),
            }, s_json);
            cancellationToken.ThrowIfCancellationRequested();
            return new(extraction, output, InspectSourcesAsync);
        }
        catch
        {
            extraction.Dispose();
            throw;
        }
    }

    private static object Source(DocumentSourceOutput output) => new
    {
        output.Identifier, output.ProviderName, output.ModelId,
        NativeStatus = output.NativeStatus.ToString(), ContentStatus = output.ContentStatus.ToString(),
    };
    private static object Frame(DocumentSourcePage frame) => new
    {
        frame.Identifier, frame.PageNumber, frame.Dimensions,
        CoordinateUnit = frame.CoordinateUnit?.ToString(), CoordinateOrigin = frame.CoordinateOrigin?.ToString(),
        SourceOutput = frame.SourceOutput is { } output ? Source(output) : null,
    };
    private static object Annotation(DocumentAnnotation annotation) => new
    {
        annotation.Kind, annotation.Value, annotation.Role, annotation.Confidence,
        Regions = annotation.Regions.Select(region => new { Page = Frame(region.Page), region.Polygon }).ToArray(),
        SourceOutput = annotation.SourceOutput is { } output ? Source(output) : null,
    };
    private static object Section(DocumentSection section) => new
    {
        section.StoredMarkdown, section.Text,
        Annotations = section.Annotations.Select(Annotation).ToArray(),
        Elements = section.Elements.Select(Element).ToArray(),
    };
    private static object Element(DocumentElement element)
    {
        object? content = element switch
        {
            DocumentTextElement text => new { Kind = text.Kind.ToString(), text.HeaderLevel },
            DocumentImageElement image => new
            {
                image.MediaType, ContentLength = image.Content?.Length,
                image.Source, image.AlternativeText,
            },
            DocumentExtensionElement extension => new { extension.Type, extension.Value },
            DocumentSection section => Section(section),
            DocumentTableElement table => new
            {
                table.Cells.RowCount, table.Cells.ColumnCount,
                Cells = Enumerable.Range(0, table.Cells.RowCount).SelectMany(row =>
                    Enumerable.Range(0, table.Cells.ColumnCount).Select(column =>
                    {
                        var cell = table.Cells[row, column];
                        var covered = table.Cells.IsCovered(row, column);
                        return new
                        {
                            Row = row, Column = column, IsCovered = covered, HasCell = cell is not null,
                            State = covered ? "covered" : cell is null ? "missing" : cell.Content is null ? "empty" : "content",
                            cell?.RowSpan, cell?.ColumnSpan, cell?.Kind,
                            Content = cell?.Content is { } child ? Element(child) : null,
                            Annotations = cell?.Annotations.Select(Annotation).ToArray(),
                        };
                    })).ToArray(),
            },
            _ => null,
        };
        return new
        {
            Type = element.GetType().Name, element.Address, element.StoredMarkdown, element.Text,
            Annotations = element.Annotations.Select(Annotation).ToArray(), Content = content,
        };
    }

    private static async Task<string> InspectSourcesAsync(DocumentExtractionResult extraction, CancellationToken token)
    {
        var inspections = new List<DocumentRawInspection>();
        foreach (var source in extraction.Document.SourceOutputs)
        {
            token.ThrowIfCancellationRequested();
            inspections.Add(await InspectAsync(source, token).ConfigureAwait(false));
        }
        return inspections.Count == 0 ? "" : JsonSerializer.Serialize(inspections, s_json);
    }

    private static async Task<DocumentRawInspection> InspectAsync(DocumentSourceOutput output, CancellationToken token)
    {
        string? native = null, body = null;
        var nativeTruncated = false;
        var bodyTruncated = false;
        var errors = new List<string>();
        try
        {
            if (output.GetRawRepresentation() is { } representation)
            {
                using var buffer = new InspectionStream(MaximumInspectionBytes);
                try
                {
                    await JsonSerializer.SerializeAsync(buffer, representation, representation.GetType(), s_json, token)
                        .ConfigureAwait(false);
                }
                catch (InspectionLimitException) { nativeTruncated = true; }
                native = Encoding.UTF8.GetString(buffer.ToArray());
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
        {
            errors.Add($"Native inspection failed ({exception.GetType().Name}).");
        }
        try
        {
            if (output.GetRawContent() is { } artifact)
            {
                var stream = artifact.Content;
                var position = stream.CanSeek ? stream.Position : (long?)null;
                try
                {
                    if (stream.CanSeek)
                        stream.Position = 0;
                    var bytes = new byte[MaximumInspectionBytes + 1];
                    var count = 0;
                    while (count < bytes.Length)
                    {
                        var read = await stream.ReadAsync(bytes.AsMemory(count), token).ConfigureAwait(false);
                        if (read == 0)
                            break;
                        count += read;
                    }
                    bodyTruncated = count > MaximumInspectionBytes;
                    body = Encoding.UTF8.GetString(bytes, 0, Math.Min(count, MaximumInspectionBytes));
                }
                finally
                {
                    if (position is { } original)
                        stream.Position = original;
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
        {
            errors.Add($"Original-content inspection failed ({exception.GetType().Name}).");
        }
        return new(output.Identifier, output.ProviderName, output.NativeStatus.ToString(), output.ContentStatus.ToString(),
            native, body, nativeTruncated, bodyTruncated, errors);
    }

    private sealed class InspectionLimitException : IOException;

    private sealed class InspectionStream(int limit) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            var available = Math.Max(0, limit - (int)Length);
            var bytes = buffer[..Math.Min(buffer.Length, available)].ToArray();
            base.Write(bytes, 0, bytes.Length);
            if (buffer.Length > available)
                throw new InspectionLimitException();
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(buffer.AsSpan(offset, count));
            return Task.CompletedTask;
        }
    }
}

public sealed record DocumentExtractionProgress(int PagesProcessed);
public sealed class DocumentExtractionRunResult : IDisposable
{
    private readonly SemaphoreSlim _inspectionGate = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _disposeSync = new();
    private readonly Func<DocumentExtractionResult, CancellationToken, Task<string>> _inspect;
    private readonly bool _hasRawOutput;
    private DocumentExtractionResult? _extraction;
    private string? _rawOutput;

    internal DocumentExtractionRunResult(DocumentExtractionResult extraction, string output,
        Func<DocumentExtractionResult, CancellationToken, Task<string>> inspect)
    {
        _extraction = extraction;
        _inspect = inspect;
        PageCount = extraction.Pages.Count;
        Output = output;
        _hasRawOutput = extraction.Document.SourceOutputs.Any(source =>
            source.NativeStatus != DocumentRawContentStatus.Disabled || source.ContentStatus != DocumentRawContentStatus.Disabled);
    }

    public int PageCount { get; }
    public string Output { get; }
    public bool IsDisposed => Volatile.Read(ref _extraction) is null;
    public bool HasRawOutput => !IsDisposed && _hasRawOutput;

    public async Task<string> InspectRawAsync(CancellationToken cancellationToken = default)
    {
        await _inspectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_extraction is null, this);
            if (!_hasRawOutput)
                return "";
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            return _rawOutput ??= await _inspect(_extraction!, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _inspectionGate.Release();
        }
    }

    public void Dispose()
    {
        lock (_disposeSync)
        {
            if (IsDisposed)
                return;
            _lifetime.Cancel();
            _inspectionGate.Wait();
            try
            {
                Interlocked.Exchange(ref _extraction, null)?.Dispose();
                _lifetime.Dispose();
            }
            finally
            {
                _inspectionGate.Release();
            }
        }
    }
}
public sealed record DocumentRawInspection(
    string Identifier, string? ProviderName, string NativeStatus, string ContentStatus,
    string? NativeRepresentation, string? OriginalResponseBody, bool NativeTruncated, bool BodyTruncated,
    IReadOnlyList<string> Errors);
