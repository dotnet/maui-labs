using System.Net.Http.Headers;
using System.Text;

namespace Microsoft.Maui.DevFlow.Agent.Core.Network;

/// <summary>
/// DelegatingHandler that intercepts HTTP requests/responses and records them
/// in the NetworkRequestStore. Wraps any inner handler (preserving platform-specific
/// handlers like AndroidMessageHandler, NSUrlSessionHandler, etc.).
/// </summary>
public class DevFlowHttpHandler : DelegatingHandler
{
    private readonly NetworkRequestStore _store;
    private readonly int _maxBodySize;

    private static readonly HashSet<string> TextContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/json", "application/xml", "application/soap+xml",
        "application/graphql", "application/javascript",
        "application/x-www-form-urlencoded", "application/ld+json",
        "application/vnd.api+json"
    };

    public DevFlowHttpHandler(NetworkRequestStore store, int maxBodySize = 256 * 1024)
        : base()
    {
        _store = store;
        _maxBodySize = maxBodySize;
    }

    public DevFlowHttpHandler(NetworkRequestStore store, HttpMessageHandler innerHandler, int maxBodySize = 256 * 1024)
        : base(innerHandler)
    {
        _store = store;
        _maxBodySize = maxBodySize;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var entry = NetworkRequestEntry.BeginCapture(request);

        // Capture request body
        if (request.Content != null)
        {
            try
            {
                var (body, encoding, size, truncated) = await CaptureBodyAsync(request.Content);
                entry.RequestBody = body;
                entry.RequestBodyEncoding = encoding;
                entry.RequestSize = size;
                entry.RequestBodyTruncated = truncated;
            }
            catch { /* Don't fail the request if body capture fails */ }
        }

        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            entry.CompleteCapture(response);

            // Capture response body
            if (response.Content != null)
            {
                try
                {
                    var (body, encoding, size, truncated) = await CaptureResponseBodyAsync(response);
                    entry.ResponseBody = body;
                    entry.ResponseBodyEncoding = encoding;
                    entry.ResponseSize = size;
                    entry.ResponseBodyTruncated = truncated;
                }
                catch { /* Don't fail the response if body capture fails */ }
            }

            _store.Add(entry);
            return response;
        }
        catch (Exception ex)
        {
            entry.CompleteWithError(ex);
            _store.Add(entry);
            throw;
        }
    }

    private async Task<(string? body, string? encoding, long size, bool truncated)> CaptureBodyAsync(
        HttpContent content)
    {
        var bytes = await content.ReadAsByteArrayAsync();
        var size = (long)bytes.Length;
        var isText = IsTextContent(content.Headers.ContentType);

        if (bytes.Length == 0)
            return (null, null, 0, false);

        var truncated = bytes.Length > _maxBodySize;
        var captureBytes = truncated ? bytes.AsSpan(0, _maxBodySize).ToArray() : bytes;

        if (isText)
        {
            return (Encoding.UTF8.GetString(captureBytes), "text", size, truncated);
        }
        else
        {
            return (Convert.ToBase64String(captureBytes), "base64", size, truncated);
        }
    }

    private async Task<(string? body, string? encoding, long size, bool truncated)> CaptureResponseBodyAsync(
        HttpResponseMessage response)
    {
        // For responses, we need to buffer the content so the app can still read it.
        // ReadAsByteArrayAsync buffers the content, making it re-readable.
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var size = (long)bytes.Length;
        var isText = IsTextContent(response.Content.Headers.ContentType);

        if (bytes.Length == 0)
            return (null, null, 0, false);

        var truncated = bytes.Length > _maxBodySize;
        var captureBytes = truncated ? bytes.AsSpan(0, _maxBodySize).ToArray() : bytes;

        if (isText)
        {
            return (Encoding.UTF8.GetString(captureBytes), "text", size, truncated);
        }
        else
        {
            return (Convert.ToBase64String(captureBytes), "base64", size, truncated);
        }
    }

    private static bool IsTextContent(MediaTypeHeaderValue? contentType)
    {
        if (contentType == null) return false;
        var mediaType = contentType.MediaType;
        if (mediaType == null) return false;

        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return true;

        return TextContentTypes.Contains(mediaType);
    }
}
