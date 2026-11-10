using System.Text.Json;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Applies the playground's upload limit and deadline without changing provider mappings.</summary>
internal class CloudDocumentExtractionPolicy(DocumentExtractionClient innerClient, TimeSpan timeout)
    : DelegatingDocumentExtractionClient(innerClient)
{
    private const int MaximumDocumentBytes = 20 * 1024 * 1024;
    private bool _disposed;

    public override Task<ExtractionDocument> ExtractAsync(
        Stream document, string identifier, string mediaType, DocumentExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        if (!document.CanRead)
            throw new ArgumentException("The document stream must be readable.", nameof(document));
        if (mediaType is not ("application/pdf" or "image/png" or "image/jpeg" or "image/heic" or "image/tiff"))
            throw new NotSupportedException($"Unsupported document media type: {mediaType}.");
        return ExtractCoreAsync(document, identifier, mediaType, options?.Clone(), cancellationToken);
    }

    private async Task<ExtractionDocument> ExtractCoreAsync(
        Stream document, string identifier, string mediaType, DocumentExtractionOptions? options,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var bounded = new UploadStream(document);
        try
        {
            var result = await InnerClient.ExtractAsync(bounded, identifier, mediaType, options, deadline.Token)
                .ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (HttpRequestException error)
        {
            // Provider/transport error messages may include credentials, service text or document content.
            throw new HttpRequestException(
                error.StatusCode is { } status ? $"Document extraction returned HTTP {(int)status}." :
                    "Document extraction request failed.", null, error.StatusCode);
        }
        catch (JsonException)
        {
            throw new InvalidDataException("Document extraction returned invalid JSON.");
        }
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return base.GetService(serviceType, serviceKey);
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;
        _disposed = true;
        base.Dispose(disposing);
    }

    private sealed class UploadStream(Stream source) : Stream
    {
        private long _read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Count(source.Read(buffer, offset, count));
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
            Count(await source.ReadAsync(buffer.AsMemory(offset, count), token).ConfigureAwait(false));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));
        private int Count(int count)
        {
            _read += count;
            if (_read > MaximumDocumentBytes)
                throw new InvalidOperationException("Documents must be 20 MB or smaller.");
            return count;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
