using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// HTTP client that communicates with the Microsoft.Maui.DevFlow Agent running inside the MAUI app.
/// </summary>
public class AgentClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private bool _disposed;

    public string BaseUrl => _baseUrl;

    public AgentClient(string host = "localhost", int port = 9223)
    {
        _baseUrl = $"http://{host}:{port}";
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>
    /// Check if the agent is reachable.
    /// </summary>
    public async Task<AgentStatus?> GetStatusAsync(int? window = null)
    {
        var url = window != null ? $"/api/status?window={window}" : "/api/status";
        var response = await GetAsync<AgentStatus>(url);
        return response;
    }

    /// <summary>
    /// Get the visual tree from the running app.
    /// </summary>
    public async Task<List<ElementInfo>> GetTreeAsync(int maxDepth = 0, int? window = null)
    {
        var parts = new List<string>();
        if (maxDepth > 0) parts.Add($"depth={maxDepth}");
        if (window != null) parts.Add($"window={window}");
        var url = parts.Count > 0 ? $"/api/tree?{string.Join("&", parts)}" : "/api/tree";
        return await GetAsync<List<ElementInfo>>(url) ?? new();
    }

    /// <summary>
    /// Get a single element by ID.
    /// </summary>
    public async Task<ElementInfo?> GetElementAsync(string id)
    {
        return await GetAsync<ElementInfo>($"/api/element/{id}");
    }

    /// <summary>
    /// Query elements by type, automationId, and/or text.
    /// </summary>
    public async Task<List<ElementInfo>> QueryAsync(string? type = null, string? automationId = null, string? text = null)
    {
        var queryParts = new List<string>();
        if (type != null) queryParts.Add($"type={Uri.EscapeDataString(type)}");
        if (automationId != null) queryParts.Add($"automationId={Uri.EscapeDataString(automationId)}");
        if (text != null) queryParts.Add($"text={Uri.EscapeDataString(text)}");

        var url = $"/api/query?{string.Join("&", queryParts)}";
        return await GetAsync<List<ElementInfo>>(url) ?? new();
    }

    /// <summary>
    /// Query elements using a CSS selector string.
    /// </summary>
    public async Task<List<ElementInfo>> QueryCssAsync(string selector)
    {
        var url = $"{_baseUrl}/api/query?selector={Uri.EscapeDataString(selector)}";
        var response = await _http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        var json = DriverJson.ParseElement(body);
        if (json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty("success", out var s) && !s.GetBoolean())
        {
            var msg = json.TryGetProperty("error", out var e) ? e.GetString() : "Query failed";
            throw new InvalidOperationException(msg);
        }
        return DriverJson.Deserialize<List<ElementInfo>>(json.GetRawText()) ?? new();
    }

    /// <summary>
    /// Tap an element.
    /// </summary>
    public async Task<bool> TapAsync(string elementId)
    {
        return await PostActionAsync("/api/action/tap", new JsonObject
        {
            ["elementId"] = elementId
        });
    }

    /// <summary>
    /// Fill text into an element.
    /// </summary>
    public async Task<bool> FillAsync(string elementId, string text)
    {
        return await PostActionAsync("/api/action/fill", new JsonObject
        {
            ["elementId"] = elementId,
            ["text"] = text
        });
    }

    /// <summary>
    /// Clear text from an element.
    /// </summary>
    public async Task<bool> ClearAsync(string elementId)
    {
        return await PostActionAsync("/api/action/clear", new JsonObject
        {
            ["elementId"] = elementId
        });
    }

    /// <summary>
    /// Focus an element.
    /// </summary>
    public async Task<bool> FocusAsync(string elementId)
    {
        return await PostActionAsync("/api/action/focus", new JsonObject
        {
            ["elementId"] = elementId
        });
    }

    /// <summary>
    /// Navigate to a Shell route.
    /// </summary>
    public async Task<bool> NavigateAsync(string route)
    {
        return await PostActionAsync("/api/action/navigate", new JsonObject
        {
            ["route"] = route
        });
    }

    /// <summary>
    /// Scroll by delta, item index, or scroll element into view.
    /// </summary>
    public async Task<bool> ScrollAsync(string? elementId = null, double deltaX = 0, double deltaY = 0, bool animated = true, int? window = null, int? itemIndex = null, int? groupIndex = null, string? scrollToPosition = null)
    {
        var url = "/api/action/scroll";
        if (window != null) url += $"?window={window}";
        return await PostActionAsync(url, new JsonObject
        {
            ["elementId"] = elementId,
            ["deltaX"] = deltaX,
            ["deltaY"] = deltaY,
            ["animated"] = animated,
            ["itemIndex"] = itemIndex,
            ["groupIndex"] = groupIndex,
            ["scrollToPosition"] = scrollToPosition
        });
    }

    /// <summary>
    /// Resize the app window.
    /// </summary>
    public async Task<bool> ResizeAsync(int width, int height, int? window = null)
    {
        var url = "/api/action/resize";
        if (window != null) url += $"?window={window}";
        return await PostActionAsync(url, new JsonObject
        {
            ["width"] = width,
            ["height"] = height
        });
    }

    /// <summary>
    /// Take a screenshot (returns PNG bytes).
    /// Optionally target a specific element by ID or CSS selector.
    /// </summary>
    public async Task<byte[]?> ScreenshotAsync(int? window = null, string? elementId = null, string? selector = null, int? maxWidth = null, string? scale = null)
    {
        try
        {
            var queryParams = new List<string>();
            if (window != null) queryParams.Add($"window={window}");
            if (elementId != null) queryParams.Add($"id={Uri.EscapeDataString(elementId)}");
            if (selector != null) queryParams.Add($"selector={Uri.EscapeDataString(selector)}");
            if (maxWidth != null) queryParams.Add($"maxWidth={maxWidth}");
            if (scale != null) queryParams.Add($"scale={Uri.EscapeDataString(scale)}");

            var url = queryParams.Count > 0
                ? $"{_baseUrl}/api/screenshot?{string.Join("&", queryParams)}"
                : $"{_baseUrl}/api/screenshot";

            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch { return null; }
    }

    /// <summary>
    /// Get a specific property value from an element.
    /// </summary>
    public async Task<string?> GetPropertyAsync(string elementId, string propertyName)
    {
        var result = await GetJsonAsync($"/api/property/{elementId}/{propertyName}");
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("value", out var val))
            return val.GetString();
        return null;
    }

    /// <summary>
    /// Set a property value on an element.
    /// </summary>
    public async Task<bool> SetPropertyAsync(string elementId, string propertyName, string value)
    {
        try
        {
            using var content = DriverJson.CreateJsonContent(new JsonObject
            {
                ["value"] = value
            });
            var response = await _http.PostAsync($"{_baseUrl}/api/property/{elementId}/{propertyName}", content);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>
    /// Retrieve application logs from the agent.
    /// </summary>
    public async Task<string> GetLogsAsync(int limit = 100, int skip = 0, string? source = null)
    {
        var path = $"/api/logs?limit={limit}&skip={skip}";
        if (!string.IsNullOrEmpty(source) && source != "all")
            path += $"&source={Uri.EscapeDataString(source)}";
        return await _http.GetStringAsync($"{_baseUrl}{path}");
    }

    /// <summary>
    /// Send a CDP command to a Blazor WebView.
    /// </summary>
    public async Task<JsonElement> SendCdpCommandAsync(string method, JsonNode? @params = null, string? webviewId = null)
    {
        var path = "/api/cdp";
        if (!string.IsNullOrEmpty(webviewId))
            path += $"?webview={Uri.EscapeDataString(webviewId)}";

        var body = new JsonObject
        {
            ["method"] = method
        };
        if (@params != null)
            body["params"] = @params;

        using var content = DriverJson.CreateJsonContent(body);
        var response = await _http.PostAsync($"{_baseUrl}{path}", content);
        var responseBody = await response.Content.ReadAsStringAsync();
        return DriverJson.ParseElement(responseBody);
    }

    /// <summary>
    /// Gets the list of CDP WebViews registered with the agent.
    /// </summary>
    public async Task<JsonElement> GetCdpWebViewsAsync()
    {
        return await GetJsonAsync("/api/cdp/webviews");
    }

    public async Task<string> GetCdpSourceAsync(string? webviewId = null)
    {
        var path = "/api/cdp/source";
        if (!string.IsNullOrEmpty(webviewId))
            path += $"?webview={Uri.EscapeDataString(webviewId)}";
        return await _http.GetStringAsync($"{_baseUrl}{path}");
    }

    public async Task<string> HitTestAsync(double x, double y, int? window = null)
    {
        var path = $"/api/hittest?x={x}&y={y}";
        if (window.HasValue)
            path += $"&window={window.Value}";
        return await _http.GetStringAsync($"{_baseUrl}{path}");
    }

    public async Task<ProfilerCapabilities?> GetProfilerCapabilitiesAsync()
    {
        return await GetAsync<ProfilerCapabilities>("/api/profiler/capabilities");
    }

    public async Task<ProfilerSessionInfo?> StartProfilerAsync(int? sampleIntervalMs = null)
    {
        var payload = new JsonObject();
        if (sampleIntervalMs.HasValue)
            payload["sampleIntervalMs"] = sampleIntervalMs.Value;

        var response = await PostJsonAsync<ProfilerSessionEnvelope>("/api/profiler/start", payload);
        return response?.Session;
    }

    public async Task<ProfilerSessionInfo?> StopProfilerAsync()
    {
        var response = await PostJsonAsync<ProfilerSessionEnvelope>("/api/profiler/stop", new JsonObject());
        return response?.Session;
    }

    public async Task<ProfilerBatch?> GetProfilerSamplesAsync(
        long sampleCursor = 0,
        long markerCursor = 0,
        long spanCursor = 0,
        int limit = 500)
    {
        var url = $"/api/profiler/samples?sampleCursor={sampleCursor}&markerCursor={markerCursor}&spanCursor={spanCursor}&limit={limit}";
        return await GetAsync<ProfilerBatch>(url);
    }

    public async Task<bool> PublishProfilerMarkerAsync(
        string name,
        string type = "user.action",
        string? payloadJson = null)
    {
        return await PostActionAsync("/api/profiler/marker", new JsonObject
        {
            ["name"] = name,
            ["type"] = type,
            ["payloadJson"] = payloadJson
        });
    }

    public async Task<List<ProfilerHotspot>> GetProfilerHotspotsAsync(
        int limit = 20,
        int minDurationMs = 16,
        string? kind = null)
    {
        limit = Math.Clamp(limit, 1, 200);
        minDurationMs = Math.Clamp(minDurationMs, 0, 60_000);

        var path = $"/api/profiler/hotspots?limit={limit}&minDurationMs={minDurationMs}";
        if (!string.IsNullOrWhiteSpace(kind))
            path += $"&kind={Uri.EscapeDataString(kind)}";
        return await GetAsync<List<ProfilerHotspot>>(path) ?? new();
    }

    private async Task<T?> GetAsync<T>(string path) where T : class
    {
        try
        {
            var response = await _http.GetStringAsync($"{_baseUrl}{path}");
            return DriverJson.Deserialize<T>(response);
        }
        catch { return null; }
    }

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        try
        {
            using var response = await _http.GetAsync($"{_baseUrl}{path}");
            var body = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body))
                return default;

            return DriverJson.ParseElement(body);
        }
        catch { return default; }
    }

    private async Task<bool> PostActionAsync(string path, JsonNode body)
    {
        try
        {
            using var content = DriverJson.CreateJsonContent(body);
            var response = await _http.PostAsync($"{_baseUrl}{path}", content);
            if (!response.IsSuccessStatusCode) return false;

            var responseBody = await response.Content.ReadAsStringAsync();
            var result = DriverJson.Deserialize<ActionResponse>(responseBody);
            return result?.Success == true;
        }
        catch { return false; }
    }

    private async Task<T?> PostJsonAsync<T>(string path, JsonNode body) where T : class
    {
        try
        {
            using var content = DriverJson.CreateJsonContent(body);
            var response = await _http.PostAsync($"{_baseUrl}{path}", content);
            if (!response.IsSuccessStatusCode)
                return null;
            var responseBody = await response.Content.ReadAsStringAsync();
            return DriverJson.Deserialize<T>(responseBody);
        }
        catch
        {
            return null;
        }
    }

    // ── Preferences ──

    public async Task<JsonElement> GetPreferencesAsync(string? sharedName = null)
    {
        var path = "/api/preferences";
        if (!string.IsNullOrEmpty(sharedName))
            path += $"?sharedName={Uri.EscapeDataString(sharedName)}";
        return await GetJsonAsync(path);
    }

    public async Task<JsonElement> GetPreferenceAsync(string key, string? type = null, string? sharedName = null)
    {
        var path = $"/api/preferences/{Uri.EscapeDataString(key)}";
        var qs = new List<string>();
        if (!string.IsNullOrEmpty(type)) qs.Add($"type={Uri.EscapeDataString(type)}");
        if (!string.IsNullOrEmpty(sharedName)) qs.Add($"sharedName={Uri.EscapeDataString(sharedName)}");
        if (qs.Count > 0) path += "?" + string.Join("&", qs);
        return await GetJsonAsync(path);
    }

    public async Task<JsonElement> SetPreferenceAsync(string key, string value, string? type = null, string? sharedName = null)
    {
        var body = new JsonObject
        {
            ["value"] = value
        };
        if (!string.IsNullOrEmpty(type)) body["type"] = type;
        if (!string.IsNullOrEmpty(sharedName)) body["sharedName"] = sharedName;

        using var content = DriverJson.CreateJsonContent(body);
        var response = await _http.PostAsync($"{_baseUrl}/api/preferences/{Uri.EscapeDataString(key)}", content);
        var responseBody = await response.Content.ReadAsStringAsync();
        return DriverJson.ParseElement(responseBody);
    }

    public async Task<JsonElement> DeletePreferenceAsync(string key, string? sharedName = null)
    {
        var path = $"/api/preferences/{Uri.EscapeDataString(key)}";
        if (!string.IsNullOrEmpty(sharedName))
            path += $"?sharedName={Uri.EscapeDataString(sharedName)}";
        var response = await _http.DeleteAsync($"{_baseUrl}{path}");
        var responseBody = await response.Content.ReadAsStringAsync();
        return DriverJson.ParseElement(responseBody);
    }

    public async Task<bool> ClearPreferencesAsync(string? sharedName = null)
    {
        var path = "/api/preferences/clear";
        if (!string.IsNullOrEmpty(sharedName))
            path += $"?sharedName={Uri.EscapeDataString(sharedName)}";
        return await PostActionAsync(path, new JsonObject());
    }

    // ── Secure Storage ──

    public async Task<JsonElement> GetSecureStorageAsync(string key)
    {
        return await GetJsonAsync($"/api/secure-storage/{Uri.EscapeDataString(key)}");
    }

    public async Task<JsonElement> SetSecureStorageAsync(string key, string value)
    {
        using var content = DriverJson.CreateJsonContent(new JsonObject
        {
            ["value"] = value
        });
        var response = await _http.PostAsync($"{_baseUrl}/api/secure-storage/{Uri.EscapeDataString(key)}", content);
        var responseBody = await response.Content.ReadAsStringAsync();
        return DriverJson.ParseElement(responseBody);
    }

    public async Task<JsonElement> DeleteSecureStorageAsync(string key)
    {
        var response = await _http.DeleteAsync($"{_baseUrl}/api/secure-storage/{Uri.EscapeDataString(key)}");
        var responseBody = await response.Content.ReadAsStringAsync();
        return DriverJson.ParseElement(responseBody);
    }

    public async Task<bool> ClearSecureStorageAsync()
    {
        return await PostActionAsync("/api/secure-storage/clear", new JsonObject());
    }

    // ── Platform info ──

    public async Task<JsonElement> GetPlatformInfoAsync(string endpoint)
    {
        return await GetJsonAsync($"/api/platform/{endpoint}");
    }

    public async Task<JsonElement> GetGeolocationAsync(string? accuracy = null, int? timeoutSeconds = null)
    {
        var path = "/api/platform/geolocation";
        var qs = new List<string>();
        if (!string.IsNullOrEmpty(accuracy)) qs.Add($"accuracy={Uri.EscapeDataString(accuracy)}");
        if (timeoutSeconds.HasValue) qs.Add($"timeout={timeoutSeconds.Value}");
        if (qs.Count > 0) path += "?" + string.Join("&", qs);
        return await GetJsonAsync(path);
    }

    // ── Sensors ──

    public async Task<JsonElement> GetSensorsAsync()
    {
        return await GetJsonAsync("/api/sensors");
    }

    public async Task<bool> StartSensorAsync(string sensor, string? speed = null)
    {
        var path = $"/api/sensors/{Uri.EscapeDataString(sensor)}/start";
        if (!string.IsNullOrEmpty(speed))
            path += $"?speed={Uri.EscapeDataString(speed)}";
        return await PostActionAsync(path, new JsonObject());
    }

    public async Task<bool> StopSensorAsync(string sensor)
    {
        return await PostActionAsync($"/api/sensors/{Uri.EscapeDataString(sensor)}/stop", new JsonObject());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }

    // ── Network monitoring ──

    public async Task<List<NetworkRequest>> GetNetworkRequestsAsync(
        int limit = 100, string? host = null, string? method = null)
    {
        try
        {
            var url = $"{_baseUrl}/api/network?limit={limit}";
            if (!string.IsNullOrEmpty(host)) url += $"&host={Uri.EscapeDataString(host)}";
            if (!string.IsNullOrEmpty(method)) url += $"&method={Uri.EscapeDataString(method)}";

            var response = await _http.GetStringAsync(url);
            return DriverJson.Deserialize<List<NetworkRequest>>(response) ?? new();
        }
        catch { return new(); }
    }

    public async Task<NetworkRequest?> GetNetworkRequestDetailAsync(string id)
    {
        try
        {
            var response = await _http.GetStringAsync($"{_baseUrl}/api/network/{Uri.EscapeDataString(id)}");
            return DriverJson.Deserialize<NetworkRequest>(response);
        }
        catch { return null; }
    }

    public async Task<bool> ClearNetworkRequestsAsync()
    {
        return await PostActionAsync("/api/network/clear", new JsonObject());
    }

    /// <summary>
    /// Returns the WebSocket URL for live network monitoring.
    /// </summary>
    public string GetNetworkWebSocketUrl()
    {
        var wsBase = _baseUrl.Replace("http://", "ws://").Replace("https://", "wss://");
        return $"{wsBase}/ws/network";
    }

    internal sealed class ProfilerSessionEnvelope
    {
        [System.Text.Json.Serialization.JsonPropertyName("session")]
        public ProfilerSessionInfo? Session { get; set; }
    }

    internal sealed class ActionResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("success")]
        public bool Success { get; set; }
    }
}

public class AgentStatus
{
    [System.Text.Json.Serialization.JsonPropertyName("agent")]
    public string? Agent { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("version")]
    public string? Version { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("platform")]
    public string? Platform { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("deviceType")]
    public string? DeviceType { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("idiom")]
    public string? Idiom { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("appName")]
    public string? AppName { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("running")]
    public bool Running { get; set; }
}

public class NetworkRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("method")]
    public string Method { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("url")]
    public string Url { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("host")]
    public string? Host { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("path")]
    public string? Path { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("statusCode")]
    public int? StatusCode { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("statusText")]
    public string? StatusText { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("requestSize")]
    public long? RequestSize { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("responseSize")]
    public long? ResponseSize { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("error")]
    public string? Error { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("requestContentType")]
    public string? RequestContentType { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("responseContentType")]
    public string? ResponseContentType { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("requestHeaders")]
    public Dictionary<string, string[]>? RequestHeaders { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("responseHeaders")]
    public Dictionary<string, string[]>? ResponseHeaders { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("requestBody")]
    public string? RequestBody { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("responseBody")]
    public string? ResponseBody { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("requestBodyEncoding")]
    public string? RequestBodyEncoding { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("responseBodyEncoding")]
    public string? ResponseBodyEncoding { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("requestBodyTruncated")]
    public bool RequestBodyTruncated { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("responseBodyTruncated")]
    public bool ResponseBodyTruncated { get; set; }
}

public class ProfilerSessionInfo
{
    [System.Text.Json.Serialization.JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("startedAtUtc")]
    public DateTime StartedAtUtc { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("sampleIntervalMs")]
    public int SampleIntervalMs { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("isActive")]
    public bool IsActive { get; set; }
}

public class ProfilerSample
{
    [System.Text.Json.Serialization.JsonPropertyName("tsUtc")]
    public DateTime TsUtc { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("fps")]
    public double? Fps { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("frameTimeMsP50")]
    public double? FrameTimeMsP50 { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("frameTimeMsP95")]
    public double? FrameTimeMsP95 { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("worstFrameTimeMs")]
    public double? WorstFrameTimeMs { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("managedBytes")]
    public long ManagedBytes { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("gc0")]
    public int Gc0 { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("gc1")]
    public int Gc1 { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("gc2")]
    public int Gc2 { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("nativeMemoryBytes")]
    public long? NativeMemoryBytes { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("nativeMemoryKind")]
    public string? NativeMemoryKind { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("cpuPercent")]
    public double? CpuPercent { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("threadCount")]
    public int? ThreadCount { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("jankFrameCount")]
    public int JankFrameCount { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("uiThreadStallCount")]
    public int UiThreadStallCount { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("frameSource")]
    public string FrameSource { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("frameQuality")]
    public string FrameQuality { get; set; } = "";
}

public class ProfilerMarker
{
    [System.Text.Json.Serialization.JsonPropertyName("tsUtc")]
    public DateTime TsUtc { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("payloadJson")]
    public string? PayloadJson { get; set; }
}

public class ProfilerBatch
{
    [System.Text.Json.Serialization.JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("samples")]
    public List<ProfilerSample> Samples { get; set; } = new();
    [System.Text.Json.Serialization.JsonPropertyName("markers")]
    public List<ProfilerMarker> Markers { get; set; } = new();
    [System.Text.Json.Serialization.JsonPropertyName("spans")]
    public List<ProfilerSpan> Spans { get; set; } = new();
    [System.Text.Json.Serialization.JsonPropertyName("sampleCursor")]
    public long SampleCursor { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("markerCursor")]
    public long MarkerCursor { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("spanCursor")]
    public long SpanCursor { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("isActive")]
    public bool IsActive { get; set; }
}

public class ProfilerSpan
{
    [System.Text.Json.Serialization.JsonPropertyName("spanId")]
    public string SpanId { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("parentSpanId")]
    public string? ParentSpanId { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("traceId")]
    public string? TraceId { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("startTsUtc")]
    public DateTime StartTsUtc { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("endTsUtc")]
    public DateTime EndTsUtc { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("durationMs")]
    public double DurationMs { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("kind")]
    public string Kind { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("status")]
    public string Status { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("threadId")]
    public int? ThreadId { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("screen")]
    public string? Screen { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("elementPath")]
    public string? ElementPath { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("tagsJson")]
    public string? TagsJson { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class ProfilerHotspot
{
    [System.Text.Json.Serialization.JsonPropertyName("kind")]
    public string Kind { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("screen")]
    public string? Screen { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("count")]
    public int Count { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("avgDurationMs")]
    public double AvgDurationMs { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("p95DurationMs")]
    public double P95DurationMs { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("maxDurationMs")]
    public double MaxDurationMs { get; set; }
}

public class ProfilerCapabilities
{
    [System.Text.Json.Serialization.JsonPropertyName("available")]
    public bool Available { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("supportedInBuild")]
    public bool SupportedInBuild { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("featureEnabled")]
    public bool FeatureEnabled { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("platform")]
    public string Platform { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("managedMemorySupported")]
    public bool ManagedMemorySupported { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("nativeMemorySupported")]
    public bool NativeMemorySupported { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("gcSupported")]
    public bool GcSupported { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("cpuPercentSupported")]
    public bool CpuPercentSupported { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("fpsSupported")]
    public bool FpsSupported { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("frameTimingsEstimated")]
    public bool FrameTimingsEstimated { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("nativeFrameTimingsSupported")]
    public bool NativeFrameTimingsSupported { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("jankEventsSupported")]
    public bool JankEventsSupported { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("uiThreadStallSupported")]
    public bool UiThreadStallSupported { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("threadCountSupported")]
    public bool ThreadCountSupported { get; set; }
}
