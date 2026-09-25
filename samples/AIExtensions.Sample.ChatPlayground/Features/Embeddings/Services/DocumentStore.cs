using System.Text;
using System.Text.Json;
using AIExtensions.Sample.ChatPlayground.Features.Embeddings.Models;
using AIExtensions.Sample.ChatPlayground.Shared.Storage;

namespace AIExtensions.Sample.ChatPlayground.Features.Embeddings.Services;

/// <summary>Keeps imported Markdown and plain text apart from chat recordings and search indexes.</summary>
public sealed class DocumentStore : IDisposable
{
    private const int MaxDocumentBytes = 256 * 1024;
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, StoredDocument> _documents = new(StringComparer.Ordinal);
    private bool _loaded;

    public DocumentStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _directory = Path.Combine(dataDirectory, "embedding-playground", "documents");
    }

    public async Task<ImportedDocument> ImportAsync(
        string fileName, Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(source);
        var name = Path.GetFileName(fileName);
        var extension = Path.GetExtension(name);
        if (!string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".markdown", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Import a Markdown (.md, .markdown) or plain text (.txt) file.");

        using var buffer = new MemoryStream();
        var block = new byte[8192];
        int count;
        while ((count = await source.ReadAsync(block, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + count > MaxDocumentBytes)
                throw new InvalidDataException("Documents must be 256 KB or smaller.");
            buffer.Write(block, 0, count);
        }
        var text = new UTF8Encoding(false, true).GetString(buffer.GetBuffer(), 0, (int)buffer.Length)
            .TrimStart('\uFEFF');
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException("The selected document has no searchable text.");

        var document = new StoredDocument(Guid.NewGuid().ToString("N"), name, text, DateTimeOffset.UtcNow);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            await Task.Run(() => AtomicFile.WriteAllText(
                DocumentPath(document.Id), JsonSerializer.Serialize(document)), cancellationToken).ConfigureAwait(false);
            _documents.Add(document.Id, document);
            return document.Summary;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ImportedDocument>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _documents.Values.OrderByDescending(document => document.ImportedAtUtc)
                .Select(document => document.Summary).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<StoredDocument> ReadAsync(string id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _documents.TryGetValue(id, out var document) ? document
                : throw new FileNotFoundException("The imported document was not found.", id);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(_directory))
                await Task.Run(() => Directory.Delete(_directory, recursive: true), cancellationToken)
                    .ConfigureAwait(false);
            _documents.Clear();
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
            return;

        var loaded = new Dictionary<string, StoredDocument>(StringComparer.Ordinal);
        if (Directory.Exists(_directory))
        {
            foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var document = JsonSerializer.Deserialize<StoredDocument>(
                    await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false))
                    ?? throw new InvalidDataException($"The imported document at {path} is empty.");
                if (document.Id != Path.GetFileNameWithoutExtension(path) ||
                    !Guid.TryParseExact(document.Id, "N", out _) ||
                    string.IsNullOrWhiteSpace(document.Name) ||
                    !string.Equals(document.Name, Path.GetFileName(document.Name), StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(document.Text) ||
                    document.ImportedAtUtc == default ||
                    !loaded.TryAdd(document.Id, document))
                    throw new InvalidDataException($"The imported document at {path} is invalid.");
            }
        }

        foreach (var (id, document) in loaded)
            _documents.Add(id, document);
        _loaded = true;
    }

    private string DocumentPath(string id) => Path.Combine(_directory, id + ".json");

    public void Dispose() => _gate.Dispose();
}
