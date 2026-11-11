namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Bounds OCR responses in the application's HTTP pipeline.</summary>
internal sealed class BoundedDocumentResponseHandler : DelegatingHandler
{
    private const int MaximumResponseBytes = 32 * 1024 * 1024;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
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
