using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Agent.Core.Network;

/// <summary>
/// Captured HTTP request/response entry.
/// Summary fields are always populated; detail fields (headers, body) are populated
/// based on configuration and may be null in summary-only contexts.
/// </summary>
public class NetworkRequestEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("host")]
    public string? Host { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("statusCode")]
    public int? StatusCode { get; set; }

    [JsonPropertyName("statusText")]
    public string? StatusText { get; set; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("requestSize")]
    public long? RequestSize { get; set; }

    [JsonPropertyName("responseSize")]
    public long? ResponseSize { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("requestContentType")]
    public string? RequestContentType { get; set; }

    [JsonPropertyName("responseContentType")]
    public string? ResponseContentType { get; set; }

    // Detail fields — populated when full details are requested

    [JsonPropertyName("requestHeaders")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string[]>? RequestHeaders { get; set; }

    [JsonPropertyName("responseHeaders")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string[]>? ResponseHeaders { get; set; }

    [JsonPropertyName("requestBody")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestBody { get; set; }

    [JsonPropertyName("responseBody")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResponseBody { get; set; }

    [JsonPropertyName("requestBodyEncoding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestBodyEncoding { get; set; }

    [JsonPropertyName("responseBodyEncoding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResponseBodyEncoding { get; set; }

    [JsonPropertyName("requestBodyTruncated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RequestBodyTruncated { get; set; }

    [JsonPropertyName("responseBodyTruncated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ResponseBodyTruncated { get; set; }

    // Non-serialized timing helper
    [JsonIgnore]
    internal Stopwatch? Stopwatch { get; set; }

    public static NetworkRequestEntry BeginCapture(HttpRequestMessage request)
    {
        var uri = request.RequestUri;
        var entry = new NetworkRequestEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Method = request.Method.Method,
            Url = uri?.ToString() ?? "",
            Host = uri?.Host,
            Path = uri?.AbsolutePath,
            RequestContentType = request.Content?.Headers.ContentType?.MediaType,
            RequestHeaders = CaptureHeaders(request.Headers, request.Content?.Headers),
            Stopwatch = Stopwatch.StartNew()
        };
        return entry;
    }

    public void CompleteCapture(HttpResponseMessage response)
    {
        Stopwatch?.Stop();
        DurationMs = Stopwatch?.ElapsedMilliseconds ?? 0;
        StatusCode = (int)response.StatusCode;
        StatusText = response.ReasonPhrase;
        ResponseContentType = response.Content?.Headers.ContentType?.MediaType;
        ResponseHeaders = CaptureHeaders(response.Headers, response.Content?.Headers);
    }

    public void CompleteWithError(Exception ex)
    {
        Stopwatch?.Stop();
        DurationMs = Stopwatch?.ElapsedMilliseconds ?? 0;
        Error = ex.Message;
    }

    /// <summary>
    /// Returns a summary copy without detail fields (headers, body).
    /// </summary>
    public NetworkRequestEntry ToSummary() => new()
    {
        Id = Id,
        Timestamp = Timestamp,
        Method = Method,
        Url = Url,
        Host = Host,
        Path = Path,
        StatusCode = StatusCode,
        StatusText = StatusText,
        DurationMs = DurationMs,
        RequestSize = RequestSize,
        ResponseSize = ResponseSize,
        Error = Error,
        RequestContentType = RequestContentType,
        ResponseContentType = ResponseContentType,
        RequestBodyTruncated = RequestBodyTruncated,
        ResponseBodyTruncated = ResponseBodyTruncated
    };

    private static Dictionary<string, string[]> CaptureHeaders(
        System.Net.Http.Headers.HttpHeaders headers,
        System.Net.Http.Headers.HttpContentHeaders? contentHeaders)
    {
        var dict = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in headers)
            dict[h.Key] = h.Value.ToArray();
        if (contentHeaders != null)
        {
            foreach (var h in contentHeaders)
            {
                if (!dict.ContainsKey(h.Key))
                    dict[h.Key] = h.Value.ToArray();
            }
        }
        return dict;
    }
}
