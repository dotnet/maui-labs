using System.Net.Http;
using System.Text.Json;

namespace BespokeCollections.Core;

public sealed class PhotoRepository
{
    readonly HttpClient _http;
    readonly Uri _baseUri;

    public PhotoRepository(HttpClient http, Uri baseUri, int pageSize = 50)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme is not ("http" or "https") || pageSize is < 1 or > 100)
            throw new ArgumentException("A HTTP fixture URL and a bounded page size are required.");
        _http = http;
        _baseUri = baseUri;
        PageSize = pageSize;
    }

    public int PageSize { get; }

    public async Task<PhotoPage> LoadPageAsync(string query, string? cursor, CancellationToken cancellationToken)
    {
        var url = new Uri(_baseUri, $"photos?query={Uri.EscapeDataString(query)}&limit={PageSize}&cursor={Uri.EscapeDataString(cursor ?? "")}");
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 128 * 1024)
            throw new InvalidDataException("The metadata page exceeds the fixture response limit.");
        var bytes = await ReadBoundedAsync(response.Content, 128 * 1024, cancellationToken).ConfigureAwait(false);
        var page = JsonSerializer.Deserialize(bytes, PhotoJsonContext.Default.PhotoPage)
            ?? throw new InvalidDataException("The metadata response was null.");
        if (page.Items is null || page.Items.Length > PageSize ||
            page.Items.Any(item => item is null || item.Id <= 0 || item.PixelWidth is <= 0 or > 16384 ||
                item.PixelHeight is <= 0 or > 16384 || string.IsNullOrWhiteSpace(item.Caption) ||
                string.IsNullOrWhiteSpace(item.ThumbnailPath) || !item.ThumbnailPath.StartsWith('/')) ||
            page.Items.Select(item => item.Id).Distinct().Count() != page.Items.Length ||
            page.NextCursor is { Length: > 1024 } ||
            page.NextCursor is not null && (page.NextCursor == cursor || page.Items.Length == 0))
            throw new InvalidDataException("The metadata page has invalid keys, dimensions, or cursor progress.");
        return page;
    }

    public async Task<byte[]> LoadThumbnailAsync(PhotoItem item, int pixelWidth, CancellationToken cancellationToken)
    {
        if (pixelWidth is < 32 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        var url = new Uri(_baseUri, item.ThumbnailPath + $"?width={pixelWidth}&revision={item.Revision}");
        if (url.Authority != _baseUri.Authority || url.Scheme != _baseUri.Scheme)
            throw new InvalidDataException("Thumbnail URLs must remain on the configured fixture.");
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await ReadBoundedAsync(response.Content, 512 * 1024, cancellationToken).ConfigureAwait(false);
    }

    static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maximum, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0)
                return result.ToArray();
            if (result.Length + count > maximum)
                throw new InvalidDataException("The HTTP response exceeds the configured byte limit.");
            result.Write(buffer, 0, count);
        }
    }
}
