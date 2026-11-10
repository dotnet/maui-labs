namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Bounds each OCR response while borrowing the application's transport.</summary>
internal sealed class BoundedDocumentResponseHandler(HttpClient client) : HttpMessageHandler
{
    private const int MaximumResponseBytes = 32 * 1024 * 1024;
    private readonly HttpClient _client = client ?? throw new ArgumentNullException(nameof(client));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // A request cannot be sent by two HttpClients; forward an independent request to the borrowed client.
        using var forwarded = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };
        foreach (var header in request.Headers)
            forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (request.Content is { } content)
        {
            forwarded.Content = new ByteArrayContent(await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
            foreach (var header in content.Headers)
                forwarded.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        var response = await _client.SendAsync(forwarded, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var original = response.Content;
            if (original.Headers.ContentLength > MaximumResponseBytes)
                throw new InvalidDataException("Mistral OCR response exceeds 32 MB.");
            using var input = await original.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int count;
            while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                if (output.Length + count > MaximumResponseBytes)
                    throw new InvalidDataException("Mistral OCR response exceeds 32 MB.");
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var replacement = new ByteArrayContent(output.ToArray());
            foreach (var header in original.Headers)
                replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content = replacement;
            original.Dispose();
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
}
