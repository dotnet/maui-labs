using System.Net;
using System.Net.Sockets;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// HTTP client that communicates with the Microsoft.Maui.DevFlow Agent running inside the MAUI app.
/// </summary>
public class AgentClient : IDisposable
{
    private const string ApiV1 = "/api/v1";
    private const string AgentApi = $"{ApiV1}/agent";
    private const string UiApi = $"{ApiV1}/ui";
    private const string WebViewApi = $"{ApiV1}/webview";
    private const string ProfilerApi = $"{ApiV1}/profiler";
    private const string StorageApi = $"{ApiV1}/storage";
    private const string DeviceApi = $"{ApiV1}/device";
    private const string NetworkApi = $"{ApiV1}/network";

    /// <summary>
    /// Per-address connect timeout for the loopback dial (see <see cref="ConnectLoopbackAsync"/>),
    /// which attempts the IPv4 and IPv6 loopback addresses in turn. A loopback refusal returns an
    /// RST almost instantly, so this only bounds the rare case of a silently-dropped connect (e.g. a
    /// broken VPN/tunnel adapter). It is deliberately kept well under <see cref="HttpClient.Timeout"/>
    /// so that even if the first family stalls there is ample budget left to try the other one.
    /// </summary>
    private static readonly TimeSpan LoopbackConnectAttemptTimeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private bool _disposed;

    public string BaseUrl => _baseUrl;

    /// <summary>
    /// Additional attempts for transient transport failures such as a dropped ADB port
    /// forward. Defaults to 0 so normal client calls keep their current fail-fast behavior.
    /// </summary>
    /// <remarks>
    /// GET requests are idempotent and safe to retry. POST/PUT/DELETE requests, however,
    /// can produce duplicate side effects on the agent (e.g. double-tap, double-navigate,
    /// double-invoke of an action) if the transport drops after the agent has received the
    /// request but before the response makes it back to the client. Retries for mutating
    /// HTTP methods are gated by <see cref="RetryMutatingRequests"/>; production callers
    /// that have not accepted that risk should leave <see cref="RetryMutatingRequests"/>
    /// disabled even when this property is non-zero.
    /// </remarks>
    public int TransientFailureRetryCount { get; set; }

    /// <summary>
    /// Whether transient-failure retries (controlled by <see cref="TransientFailureRetryCount"/>)
    /// also apply to mutating HTTP methods (POST, PUT, DELETE). Defaults to <c>true</c> so
    /// existing callers that have opted in to retries continue to retry every request type.
    /// </summary>
    /// <remarks>
    /// Retrying mutating requests can duplicate side effects when a response is lost in flight
    /// (for example, a tap may fire twice, or a Shell navigation may push the same route twice).
    /// GET requests remain safe to retry because they are idempotent. Production agents that
    /// have not explicitly accepted the duplicate-side-effect risk should set this to
    /// <c>false</c>; integration tests that need to ride out an agent process restart can leave
    /// it at the default.
    /// </remarks>
    public bool RetryMutatingRequests { get; set; } = true;

    /// <summary>
    /// Base delay between transient transport retries.
    /// </summary>
    public TimeSpan TransientFailureRetryDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    public AgentClient(string host = "localhost", int port = 9223)
    {
        _baseUrl = $"http://{host}:{port}";
        _http = CreateHttpClient(host);
    }

    /// <summary>
    /// Builds the underlying <see cref="HttpClient"/>. When <paramref name="host"/> is the
    /// <c>localhost</c> alias, a custom connect callback attempts both the IPv4 (<c>127.0.0.1</c>)
    /// and IPv6 (<c>::1</c>) loopback addresses and uses whichever accepts the connection first.
    /// </summary>
    /// <remarks>
    /// The DevFlow agent binds IPv4 loopback only, but .NET's default <see cref="HttpClient"/>
    /// may resolve <c>localhost</c> to IPv6 <c>::1</c> first and fail with "connection refused"
    /// without falling back to IPv4 (see dotnet/maui-labs#341). Rather than forcing a single
    /// address family — which has caused target-specific problems — this honors the OS-preferred
    /// resolution order and falls back to the other loopback family on refusal. Explicit hosts
    /// (a literal IP or a real hostname) are left on the default connect path unchanged.
    /// </remarks>
    private static HttpClient CreateHttpClient(string host)
    {
        if (!IsLoopbackAlias(host))
            return new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var handler = new SocketsHttpHandler
        {
            ConnectCallback = ConnectLoopbackAsync
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    private static bool IsLoopbackAlias(string host)
        => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);

    private static async ValueTask<Stream> ConnectLoopbackAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var port = context.DnsEndPoint.Port;
        var candidates = await ResolveLoopbackCandidatesAsync(context.DnsEndPoint.Host, cancellationToken)
            .ConfigureAwait(false);

        using var raceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var attempts = candidates
            .Select(address => ConnectLoopbackAddressAsync(address, port, raceCts.Token))
            .ToList();
        List<Exception>? failures = null;
        while (attempts.Count > 0)
        {
            var completed = await Task.WhenAny(attempts).ConfigureAwait(false);
            attempts.Remove(completed);
            try
            {
                var socket = await completed.ConfigureAwait(false);
                raceCts.Cancel();
                await DisposeSuccessfulConnectionsAsync(attempts).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                raceCts.Cancel();
                await DisposeSuccessfulConnectionsAsync(attempts).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                (failures ??= new List<Exception>()).Add(ex);
            }
        }

        throw failures is { Count: > 0 }
            ? new SocketException((int)SocketError.ConnectionRefused, BuildLoopbackFailureMessage(failures))
            : new SocketException((int)SocketError.ConnectionRefused);
    }

    private static async Task<Socket> ConnectLoopbackAddressAsync(
        IPAddress address, int port, CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };
        try
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(LoopbackConnectAttemptTimeout);
            await socket.ConnectAsync(address, port, attemptCts.Token).ConfigureAwait(false);
            return socket;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            socket.Dispose();
            throw;
        }
        catch (OperationCanceledException)
        {
            socket.Dispose();
            throw new TimeoutException(
                $"Connect to [{address}]:{port} timed out after {LoopbackConnectAttemptTimeout.TotalSeconds:0}s.");
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task DisposeSuccessfulConnectionsAsync(List<Task<Socket>> attempts)
    {
        foreach (var attempt in attempts)
        {
            try
            {
                (await attempt.ConfigureAwait(false)).Dispose();
            }
            catch
            {
                // Losing connection attempts are expected to fail or be canceled.
            }
        }
    }

    private static async Task<List<IPAddress>> ResolveLoopbackCandidatesAsync(string host, CancellationToken cancellationToken)
    {
        var ordered = new List<IPAddress>();

        try
        {
            // Honor the OS-preferred resolution order (e.g. macOS commonly yields ::1 first).
            foreach (var address in await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false))
            {
                if ((address.AddressFamily == AddressFamily.InterNetwork
                        || address.AddressFamily == AddressFamily.InterNetworkV6)
                    && !ordered.Contains(address))
                    ordered.Add(address);
            }
        }
        catch (Exception ex) when (ex is (SocketException or OperationCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            // DNS lookup failed (unusual for "localhost") — fall through to the explicit loopbacks below.
        }

        // Guarantee both loopback families are attempted, regardless of hosts-file quirks.
        if (!ordered.Contains(IPAddress.Loopback))
            ordered.Add(IPAddress.Loopback);
        if (Socket.OSSupportsIPv6 && !ordered.Contains(IPAddress.IPv6Loopback))
            ordered.Add(IPAddress.IPv6Loopback);

        return ordered;
    }

    private static string BuildLoopbackFailureMessage(List<Exception> failures)
        => "Could not connect to the DevFlow agent on any loopback address. "
            + string.Join("; ", failures.Select(f => f.Message));

    /// <summary>
    /// Check if the agent is reachable.
    /// </summary>
    public async Task<AgentStatus?> GetStatusAsync(int? window = null)
    {
        var url = window != null ? $"{AgentApi}/status?window={window}" : $"{AgentApi}/status";
        var response = await GetAsync<AgentStatus>(url);
        return response;
    }

    public Task<JsonElement> GetCapabilitiesAsync()
        => GetJsonAsync($"{AgentApi}/capabilities");

    public async Task<Dictionary<string, ExtensionDescriptor>> GetExtensionsAsync()
    {
        var capabilities = await GetAsync<AgentCapabilitiesResponse>($"{AgentApi}/capabilities");
        return capabilities?.Extensions ?? new Dictionary<string, ExtensionDescriptor>();
    }

    public async Task<string> CallExtensionToolAsync(string method, string path, JsonElement? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/'))
            throw new ArgumentException("Extension tool path must be an absolute agent path.", nameof(path));

        var httpMethod = new HttpMethod(method);
        using var response = await SendWithTransientRetriesAsync(httpMethod, () => SendExtensionToolRequestAsync(httpMethod, path, parameters));
        var body = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();
        return body;
    }

    private async Task<HttpResponseMessage> SendExtensionToolRequestAsync(HttpMethod method, string path, JsonElement? parameters)
    {
        using var request = new HttpRequestMessage(method, $"{_baseUrl}{path}");
        if (parameters.HasValue && method != HttpMethod.Get)
            request.Content = new StringContent(parameters.Value.GetRawText(), Encoding.UTF8, "application/json");
        else if (parameters.HasValue && parameters.Value.ValueKind == JsonValueKind.Object)
            request.RequestUri = new Uri($"{_baseUrl}{path}{BuildQueryString(parameters.Value)}");

        return await _http.SendAsync(request);
    }

    private static string BuildQueryString(JsonElement parameters)
    {
        var query = parameters.EnumerateObject()
            .Select(property => $"{Uri.EscapeDataString(property.Name)}={Uri.EscapeDataString(FormatQueryValue(property.Value))}")
            .ToArray();
        return query.Length == 0 ? string.Empty : "?" + string.Join("&", query);
    }

    private static string FormatQueryValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            JsonValueKind.Null => string.Empty,
            _ => value.GetRawText()
        };

    /// <summary>
    /// Get the visual tree from the running app.
    /// </summary>
    public Task<List<ElementInfo>> GetTreeAsync(
        int maxDepth = 0,
        int? window = null)
        => GetTreeAsync(maxDepth, window, includeNative: true);

    public async Task<List<ElementInfo>> GetTreeAsync(
        int maxDepth,
        int? window,
        bool includeNative)
    {
        var parts = new List<string>();
        if (maxDepth > 0) parts.Add($"depth={maxDepth}");
        if (window != null) parts.Add($"window={window}");
        if (!includeNative) parts.Add("native=false");
        var url = parts.Count > 0 ? $"{UiApi}/tree?{string.Join("&", parts)}" : $"{UiApi}/tree";
        return await GetAsync<List<ElementInfo>>(url) ?? new();
    }

    public async Task<ElementTreeSnapshot?> GetTreeSnapshotAsync(
        int maxDepth = 0,
        int? window = null,
        bool includeNative = true)
    {
        var parts = new List<string> { "envelope=true" };
        if (maxDepth > 0) parts.Add($"depth={maxDepth}");
        if (window != null) parts.Add($"window={window}");
        if (!includeNative) parts.Add("native=false");
        try
        {
            using var response = await SendWithTransientRetriesAsync(() =>
                _http.GetAsync(
                    $"{_baseUrl}{UiApi}/tree?{string.Join("&", parts)}"));
            if (!response.IsSuccessStatusCode)
                return null;
            var body = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                return new ElementTreeSnapshot
                {
                    Elements = DriverJson.Deserialize<List<ElementInfo>>(body)
                        ?? []
                };
            }

            return DriverJson.Deserialize<ElementTreeSnapshot>(body);
        }
        catch (Exception ex) when (IsExpectedClientException(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// Analyze the current rendered UI for clipping, overflow, text truncation,
    /// overlap, and occlusion findings.
    /// </summary>
    public Task<LayoutInspectionResult?> AnalyzeLayoutAsync(
        LayoutInspectionRequest request)
        => AnalyzeLayoutAsync(request, CancellationToken.None);

    public async Task<LayoutInspectionResult?> AnalyzeLayoutAsync(
        LayoutInspectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            using var response = await SendWithTransientRetriesAsync(
                HttpMethod.Post,
                async () =>
                {
                    using var content = DriverJson.CreateJsonContent(
                        DriverJson.SerializeToNode(request));
                    return await _http.PostAsync(
                        $"{_baseUrl}{UiApi}/diagnostics/layout",
                        content,
                        cancellationToken);
                });
            var responseBody = await response.Content.ReadAsStringAsync(
                cancellationToken);
            if (response.IsSuccessStatusCode)
                return DriverJson.Deserialize<LayoutInspectionResult>(responseBody);
            if ((int)response.StatusCode is 404 or 501)
                return null;

            var message = $"Layout diagnostics request failed with HTTP {(int)response.StatusCode}.";
            string? errorType = null;
            try
            {
                var error = DriverJson.ParseElement(responseBody);
                if (error.TryGetProperty("error", out var errorMessage))
                    message = errorMessage.GetString() ?? message;
                if (error.TryGetProperty("reason", out var reason))
                    errorType = reason.GetString();
                else if (error.TryGetProperty("type", out var type))
                    errorType = type.GetString();
            }
            catch (JsonException)
            {
            }

            throw new LayoutDiagnosticsException(
                (int)response.StatusCode,
                message,
                errorType,
                retryable: (int)response.StatusCode is 429 or 503
                    || (int)response.StatusCode >= 500);
        }
        catch (LayoutDiagnosticsException)
        {
            throw;
        }
        catch (NotSupportedByAgentException)
        {
            // A backend that does not implement layout diagnostics answers the shared 501
            // not_supported envelope, which the transport turns into this exception before the
            // status check above can run. Callers treat "unsupported" as null so the CLI and MCP
            // surfaces can emit their own capability guidance.
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsExpectedClientException(ex))
        {
            throw new LayoutDiagnosticsException(
                0,
                $"Unable to complete the layout diagnostics request: {ex.Message}",
                "layout-diagnostics-unavailable",
                retryable: true,
                innerException: ex);
        }
    }

    /// <summary>
    /// Get the layout diagnostic rules and support levels advertised by the agent.
    /// </summary>
    public Task<LayoutRuleCatalog?> GetLayoutDiagnosticRulesAsync()
        => GetAsync<LayoutRuleCatalog>($"{UiApi}/diagnostics/layout/rules");

    /// <summary>
    /// Get a single element by ID.
    /// </summary>
    public async Task<ElementInfo?> GetElementAsync(string id)
    {
        return await GetAsync<ElementInfo>($"{UiApi}/elements/{id}");
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

        var url = queryParts.Count > 0
            ? $"{UiApi}/elements?{string.Join("&", queryParts)}"
            : $"{UiApi}/elements";
        return await GetAsync<List<ElementInfo>>(url) ?? new();
    }

    /// <summary>
    /// Query elements using a CSS selector string.
    /// </summary>
    public async Task<List<ElementInfo>> QueryCssAsync(string selector)
    {
        var url = $"{_baseUrl}{UiApi}/elements?selector={Uri.EscapeDataString(selector)}";
        var body = await GetStringWithRetryableUiReadAsync(url, returnErrorBody: true);
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
    public Task<bool> TapAsync(string elementId)
        => TapAsync(elementId, captureEpoch: null, registryGeneration: null);

    public async Task<bool> TapAsync(string elementId, long? captureEpoch, long? registryGeneration)
    {
        var payload = new JsonObject
        {
            ["elementId"] = elementId
        };
        AddCaptureMetadata(payload, captureEpoch, registryGeneration);
        return await PostActionAsync($"{UiApi}/actions/tap", payload);
    }

    public Task<ActionResult> TapResultAsync(
        string elementId,
        long? captureEpoch,
        long? registryGeneration)
    {
        var payload = new JsonObject
        {
            ["elementId"] = elementId
        };
        AddCaptureMetadata(payload, captureEpoch, registryGeneration);
        return PostActionResultAsync($"{UiApi}/actions/tap", payload);
    }

    /// <summary>
    /// Fill text into an element.
    /// </summary>
    public Task<bool> FillAsync(string elementId, string text)
        => FillAsync(elementId, text, captureEpoch: null, registryGeneration: null);

    public async Task<bool> FillAsync(
        string elementId,
        string text,
        long? captureEpoch,
        long? registryGeneration)
    {
        var payload = new JsonObject
        {
            ["elementId"] = elementId,
            ["text"] = text
        };
        AddCaptureMetadata(payload, captureEpoch, registryGeneration);
        return await PostActionAsync($"{UiApi}/actions/fill", payload);
    }

    public Task<ActionResult> FillResultAsync(
        string elementId,
        string text,
        long? captureEpoch,
        long? registryGeneration)
    {
        var payload = new JsonObject
        {
            ["elementId"] = elementId,
            ["text"] = text
        };
        AddCaptureMetadata(payload, captureEpoch, registryGeneration);
        return PostActionResultAsync($"{UiApi}/actions/fill", payload);
    }

    /// <summary>
    /// Clear text from an element.
    /// </summary>
    public Task<bool> ClearAsync(string elementId)
        => ClearAsync(elementId, captureEpoch: null, registryGeneration: null);

    public async Task<bool> ClearAsync(string elementId, long? captureEpoch, long? registryGeneration)
    {
        var payload = new JsonObject
        {
            ["elementId"] = elementId
        };
        AddCaptureMetadata(payload, captureEpoch, registryGeneration);
        return await PostActionAsync($"{UiApi}/actions/clear", payload);
    }

    public Task<ActionResult> ClearResultAsync(
        string elementId,
        long? captureEpoch,
        long? registryGeneration)
    {
        var payload = new JsonObject
        {
            ["elementId"] = elementId
        };
        AddCaptureMetadata(payload, captureEpoch, registryGeneration);
        return PostActionResultAsync($"{UiApi}/actions/clear", payload);
    }

    /// <summary>
    /// Focus an element.
    /// </summary>
    public Task<bool> FocusAsync(string elementId)
        => FocusAsync(elementId, captureEpoch: null, registryGeneration: null);

    public async Task<bool> FocusAsync(string elementId, long? captureEpoch, long? registryGeneration)
    {
        var payload = new JsonObject
        {
            ["elementId"] = elementId
        };
        AddCaptureMetadata(payload, captureEpoch, registryGeneration);
        return await PostActionAsync($"{UiApi}/actions/focus", payload);
    }

    public Task<ActionResult> FocusResultAsync(
        string elementId,
        long? captureEpoch,
        long? registryGeneration)
    {
        var payload = new JsonObject
        {
            ["elementId"] = elementId
        };
        AddCaptureMetadata(payload, captureEpoch, registryGeneration);
        return PostActionResultAsync($"{UiApi}/actions/focus", payload);
    }

    /// <summary>
    /// Navigate to a Shell route.
    /// </summary>
    public async Task<bool> NavigateAsync(string route)
    {
        return await PostActionAsync($"{UiApi}/actions/navigate", new JsonObject
        {
            ["route"] = route
        });
    }

    public async Task<bool> BackAsync()
    {
        return await PostActionAsync($"{UiApi}/actions/back", new JsonObject());
    }

    public Task<bool> KeyAsync(string key, string? elementId = null, string? text = null)
        => KeyAsync(
            key,
            elementId,
            text,
            captureEpoch: null,
            registryGeneration: null);

    public async Task<bool> KeyAsync(
        string key,
        string? elementId,
        string? text,
        long? captureEpoch,
        long? registryGeneration)
    {
        var payload = new JsonObject
        {
            ["elementId"] = elementId,
            ["key"] = key,
            ["text"] = text
        };
        AddCaptureMetadata(payload, captureEpoch, registryGeneration);
        return await PostActionAsync($"{UiApi}/actions/key", payload);
    }

    public Task<ActionResult> KeyResultAsync(
        string key,
        string? elementId,
        string? text,
        long? captureEpoch,
        long? registryGeneration)
    {
        var payload = new JsonObject
        {
            ["elementId"] = elementId,
            ["key"] = key,
            ["text"] = text
        };
        AddCaptureMetadata(payload, captureEpoch, registryGeneration);
        return PostActionResultAsync($"{UiApi}/actions/key", payload);
    }

    /// <summary>
    /// Performs a gesture. Supported types: tap, doubletap, longpress, swipe, pan, pinch, rotate.
    /// </summary>
    public async Task<bool> GestureAsync(
        string type,
        string? elementId = null,
        string? direction = null,
        double? distance = null,
        int? durationMs = null)
        => (await GestureDetailedAsync(type, elementId, direction, distance, durationMs)).Success;

    public async Task<bool> GestureAsync(
        string type,
        string? elementId,
        string? direction,
        double? distance,
        int? durationMs,
        long? captureEpoch,
        long? registryGeneration)
        => (await GestureDetailedAsync(
            type,
            elementId,
            direction,
            distance,
            durationMs,
            captureEpoch: captureEpoch,
            registryGeneration: registryGeneration)).Success;

    /// <summary>
    /// Performs a gesture and reports which tier serviced it — a managed MAUI gesture
    /// recognizer, native platform injection, or nothing at all.
    /// </summary>
    public async Task<GestureResult> GestureDetailedAsync(
        string type,
        string? elementId = null,
        string? direction = null,
        double? distance = null,
        int? durationMs = null,
        double? scale = null,
        double? rotation = null,
        double? deltaX = null,
        double? deltaY = null,
        double? originX = null,
        double? originY = null,
        int? steps = null,
        long? captureEpoch = null,
        long? registryGeneration = null)
    {
        var payload = new JsonObject
        {
            ["type"] = type
        };

        if (elementId is not null) payload["elementId"] = elementId;
        if (direction is not null) payload["direction"] = direction;
        if (distance.HasValue) payload["distance"] = distance.Value;
        if (durationMs.HasValue) payload["durationMs"] = durationMs.Value;
        if (scale.HasValue) payload["scale"] = scale.Value;
        if (rotation.HasValue) payload["rotation"] = rotation.Value;
        if (deltaX.HasValue) payload["deltaX"] = deltaX.Value;
        if (deltaY.HasValue) payload["deltaY"] = deltaY.Value;
        if (originX.HasValue) payload["originX"] = originX.Value;
        if (originY.HasValue) payload["originY"] = originY.Value;
        if (steps.HasValue) payload["steps"] = steps.Value;
        AddCaptureMetadata(payload, captureEpoch, registryGeneration);

        try
        {
            using var response = await SendWithTransientRetriesAsync(HttpMethod.Post, async () =>
            {
                using var content = DriverJson.CreateJsonContent(payload);
                return await _http.PostAsync($"{_baseUrl}{UiApi}/actions/gesture", content);
            });
            var responseBody = await response.Content.ReadAsStringAsync();
            var result = DriverJson.Deserialize<GestureResult>(responseBody);

            if (result == null)
                return new GestureResult { Success = false, Type = type, Error = "Agent returned an unreadable response" };

            // A non-2xx response is a failure even if the body says otherwise.
            if (!response.IsSuccessStatusCode)
                result.Success = false;

            return result;
        }
        catch (NotSupportedByAgentException ex)
        {
            return new GestureResult
            {
                Success = false,
                Type = type,
                HandledBy = "none",
                Error = ex.Message
            };
        }
        catch (Exception ex) when (IsExpectedClientException(ex))
        {
            return new GestureResult { Success = false, Type = type, Error = ex.Message };
        }
    }

    public Task<ActionResult> GestureResultAsync(
        string type,
        string? elementId,
        string? direction,
        double? distance,
        int? durationMs,
        long? captureEpoch,
        long? registryGeneration)
    {
        var payload = new JsonObject
        {
            ["type"] = type
        };

        if (elementId is not null) payload["elementId"] = elementId;
        if (direction is not null) payload["direction"] = direction;
        if (distance.HasValue) payload["distance"] = distance.Value;
        if (durationMs.HasValue) payload["durationMs"] = durationMs.Value;
        AddCaptureMetadata(payload, captureEpoch, registryGeneration);

        return PostActionResultAsync($"{UiApi}/actions/gesture", payload);
    }

    /// <summary>Pinch to zoom. <paramref name="scale"/> of 2.0 zooms in 2x, 0.5 zooms out.</summary>
    public Task<GestureResult> PinchAsync(
        string? elementId,
        double scale,
        double? originX = null,
        double? originY = null,
        int? durationMs = null,
        int? steps = null,
        long? captureEpoch = null,
        long? registryGeneration = null)
        => GestureDetailedAsync(
            "pinch", elementId, scale: scale, originX: originX, originY: originY,
            durationMs: durationMs, steps: steps,
            captureEpoch: captureEpoch, registryGeneration: registryGeneration);

    /// <summary>Rotate by <paramref name="degrees"/>, positive = clockwise.</summary>
    public Task<GestureResult> RotateAsync(
        string? elementId,
        double degrees,
        int? durationMs = null,
        int? steps = null,
        long? captureEpoch = null,
        long? registryGeneration = null)
        => GestureDetailedAsync(
            "rotate", elementId, rotation: degrees, durationMs: durationMs, steps: steps,
            captureEpoch: captureEpoch, registryGeneration: registryGeneration);

    /// <summary>Drag by a vector in device-independent pixels.</summary>
    public Task<GestureResult> PanAsync(
        string? elementId,
        double deltaX,
        double deltaY,
        int? durationMs = null,
        int? steps = null,
        long? captureEpoch = null,
        long? registryGeneration = null)
        => GestureDetailedAsync(
            "pan", elementId, deltaX: deltaX, deltaY: deltaY,
            durationMs: durationMs, steps: steps,
            captureEpoch: captureEpoch, registryGeneration: registryGeneration);

    public Task<JsonElement> BatchAsync(
        IEnumerable<JsonObject> actions,
        bool continueOnError = false)
        => BatchAsync(
            actions,
            continueOnError,
            captureEpoch: null,
            registryGeneration: null);

    public async Task<JsonElement> BatchAsync(
        IEnumerable<JsonObject> actions,
        bool continueOnError,
        long? captureEpoch,
        long? registryGeneration)
    {
        var items = new JsonArray();
        foreach (var action in actions)
            items.Add((JsonNode?)action.DeepClone());

        var body = new JsonObject
        {
            ["continueOnError"] = continueOnError,
            ["actions"] = items
        };
        AddCaptureMetadata(body, captureEpoch, registryGeneration);

        using var response = await SendWithTransientRetriesAsync(HttpMethod.Post, async () =>
        {
            using var content = DriverJson.CreateJsonContent(body);
            return await _http.PostAsync($"{_baseUrl}{UiApi}/actions/batch", content);
        });
        var responseBody = await response.Content.ReadAsStringAsync();
        return DriverJson.ParseElement(responseBody);
    }

    /// <summary>
    /// Scroll by delta, item index, or scroll element into view.
    /// </summary>
    public Task<bool> ScrollAsync(
        string? elementId = null,
        double deltaX = 0,
        double deltaY = 0,
        bool animated = true,
        int? window = null,
        int? itemIndex = null,
        int? groupIndex = null,
        string? scrollToPosition = null)
        => ScrollAsync(
            elementId,
            deltaX,
            deltaY,
            animated,
            window,
            itemIndex,
            groupIndex,
            scrollToPosition,
            captureEpoch: null,
            registryGeneration: null);

    public async Task<bool> ScrollAsync(
        string? elementId,
        double deltaX,
        double deltaY,
        bool animated,
        int? window,
        int? itemIndex,
        int? groupIndex,
        string? scrollToPosition,
        long? captureEpoch,
        long? registryGeneration)
    {
        var url = $"{UiApi}/actions/scroll";
        if (window != null) url += $"?window={window}";

        var payload = new JsonObject
        {
            ["deltaX"] = deltaX,
            ["deltaY"] = deltaY,
            ["animated"] = animated
        };

        if (elementId is not null) payload["elementId"] = elementId;
        if (itemIndex.HasValue) payload["itemIndex"] = itemIndex.Value;
        if (groupIndex.HasValue) payload["groupIndex"] = groupIndex.Value;
        if (scrollToPosition is not null) payload["scrollToPosition"] = scrollToPosition;
        AddCaptureMetadata(payload, captureEpoch, registryGeneration);

        return await PostActionAsync(url, payload);
    }

    public Task<ActionResult> ScrollResultAsync(
        string? elementId,
        double deltaX,
        double deltaY,
        bool animated,
        int? window,
        int? itemIndex,
        int? groupIndex,
        string? scrollToPosition,
        long? captureEpoch,
        long? registryGeneration)
    {
        var url = $"{UiApi}/actions/scroll";
        if (window != null) url += $"?window={window}";
        var payload = new JsonObject
        {
            ["deltaX"] = deltaX,
            ["deltaY"] = deltaY,
            ["animated"] = animated
        };
        if (elementId is not null) payload["elementId"] = elementId;
        if (itemIndex.HasValue) payload["itemIndex"] = itemIndex.Value;
        if (groupIndex.HasValue) payload["groupIndex"] = groupIndex.Value;
        if (scrollToPosition is not null) payload["scrollToPosition"] = scrollToPosition;
        AddCaptureMetadata(payload, captureEpoch, registryGeneration);
        return PostActionResultAsync(url, payload);
    }

    /// <summary>
    /// Resize the app window.
    /// </summary>
    public async Task<bool> ResizeAsync(int width, int height, int? window = null)
    {
        var url = $"{UiApi}/actions/resize";
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
    public async Task<byte[]?> ScreenshotAsync(
        int? window = null,
        string? elementId = null,
        string? selector = null,
        int? maxWidth = null,
        string? scale = null)
    {
        var result = await ScreenshotResultAsync(window, elementId, selector, maxWidth, scale);
        return result.Success ? result.Data : null;
    }

    public async Task<byte[]?> ScreenshotAsync(
        int? window,
        string? elementId,
        string? selector,
        int? maxWidth,
        string? scale,
        long? captureEpoch,
        long? registryGeneration)
    {
        var result = await ScreenshotResultAsync(
            window,
            elementId,
            selector,
            maxWidth,
            scale,
            captureEpoch,
            registryGeneration);
        return result.Success ? result.Data : null;
    }

    /// <summary>
    /// Captures a screenshot and returns a structured <see cref="ScreenshotResult"/>. On failure,
    /// the result carries the agent-provided error message, machine-readable reason, retryable
    /// flag, and any actionable suggestions (e.g. the macOS app window not being frontmost),
    /// instead of collapsing every failure into <c>null</c> as <see cref="ScreenshotAsync"/> does.
    /// This never throws <see cref="NotSupportedByAgentException"/>: a backend that does not
    /// implement screenshots is reported as a failure result with <c>Reason</c> set to
    /// <c>"not_supported"</c>, same as any other structured failure.
    /// </summary>
    public Task<ScreenshotResult> ScreenshotResultAsync(
        int? window = null,
        string? elementId = null,
        string? selector = null,
        int? maxWidth = null,
        string? scale = null)
        => ScreenshotResultAsync(
            window,
            elementId,
            selector,
            maxWidth,
            scale,
            captureEpoch: null,
            registryGeneration: null);

    public Task<ScreenshotResult> ScreenshotResultAsync(
        int? window,
        string? elementId,
        string? selector,
        int? maxWidth,
        string? scale,
        long? captureEpoch,
        long? registryGeneration)
        => ScreenshotResultCoreAsync(
            window,
            elementId,
            selector,
            maxWidth,
            scale,
            captureEpoch,
            registryGeneration,
            fullscreen: false);

    public async Task<byte[]?> FullscreenScreenshotAsync(
        int? window = null,
        int? maxWidth = null,
        string? scale = null,
        long? captureEpoch = null,
        long? registryGeneration = null)
    {
        var result = await ScreenshotResultCoreAsync(
            window,
            elementId: null,
            selector: null,
            maxWidth,
            scale,
            captureEpoch,
            registryGeneration,
            fullscreen: true);
        return result.Success ? result.Data : null;
    }

    private async Task<ScreenshotResult> ScreenshotResultCoreAsync(
        int? window,
        string? elementId,
        string? selector,
        int? maxWidth,
        string? scale,
        long? captureEpoch,
        long? registryGeneration,
        bool fullscreen)
    {
        try
        {
            var queryParams = new List<string>();
            if (fullscreen) queryParams.Add("fullscreen=true");
            if (window != null) queryParams.Add($"window={window}");
            if (elementId != null) queryParams.Add($"elementId={Uri.EscapeDataString(elementId)}");
            if (selector != null) queryParams.Add($"selector={Uri.EscapeDataString(selector)}");
            if (maxWidth != null) queryParams.Add($"maxWidth={maxWidth}");
            if (scale != null) queryParams.Add($"scale={Uri.EscapeDataString(scale)}");
            if (captureEpoch != null) queryParams.Add($"captureEpoch={captureEpoch}");
            if (registryGeneration != null) queryParams.Add($"registryGeneration={registryGeneration}");

            var url = queryParams.Count > 0
                ? $"{_baseUrl}{UiApi}/screenshot?{string.Join("&", queryParams)}"
                : $"{_baseUrl}{UiApi}/screenshot";

            const int captureRetryCount = 2;
            for (var attempt = 0; ; attempt++)
            {
                using var response = await SendWithTransientRetriesAsync(() => _http.GetAsync(url));
                if (response.IsSuccessStatusCode)
                {
                    return ScreenshotResult.Ok(
                        await response.Content.ReadAsByteArrayAsync(),
                        GetHeaderInt64(response, "X-DevFlow-Capture-Epoch"),
                        GetHeaderInt64(response, "X-DevFlow-Registry-Generation"),
                        GetHeaderInt32(response, "X-DevFlow-Window-Id"));
                }

                var body = await response.Content.ReadAsStringAsync();
                if (response.StatusCode == HttpStatusCode.Conflict
                    && IsCaptureChangedDuringRead(body)
                    && attempt < captureRetryCount)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(10 * (attempt + 1)));
                    continue;
                }

                return ParseScreenshotError(body);
            }
        }
        // The shared retry path turns a uniform 501 not_supported envelope into this typed
        // exception. ScreenshotResultAsync's documented contract is to report failures through
        // the returned ScreenshotResult rather than throwing, so translate it back into one here.
        catch (NotSupportedByAgentException ex)
        {
            return ScreenshotResult.Failure(ex.Message, "not_supported");
        }
        catch (Exception ex) when (IsExpectedClientException(ex)) { return ScreenshotResult.Failure(null); }
    }

    private static ScreenshotResult ParseScreenshotError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return ScreenshotResult.Failure(null);

        try
        {
            var json = DriverJson.ParseElement(body);
            if (json.ValueKind != JsonValueKind.Object)
                return ScreenshotResult.Failure(null);

            var error = json.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString() : null;
            var reason = json.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString() : null;

            var retryable = false;
            IReadOnlyList<string>? suggestions = null;

            if (json.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object)
            {
                if (details.TryGetProperty("retryable", out var ret) &&
                    (ret.ValueKind == JsonValueKind.True || ret.ValueKind == JsonValueKind.False))
                    retryable = ret.GetBoolean();

                if (details.TryGetProperty("suggestions", out var sugg) && sugg.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<string>();
                    foreach (var item in sugg.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                            list.Add(s);
                    }
                    if (list.Count > 0) suggestions = list;
                }
            }

            return ScreenshotResult.Failure(error, reason, retryable, suggestions);
        }
        catch
        {
            return ScreenshotResult.Failure(null);
        }
    }

    /// <summary>
    /// Get a specific property value from an element.
    /// </summary>
    /// <remarks>
    /// Surfaces explicit server rejections (a response body with <c>"success": false</c>, such
    /// as <c>native-property-not-supported</c> for native elements or "Agent not bound to app")
    /// as a thrown exception instead of silently returning <c>null</c>, so callers see the
    /// server's real error/reason rather than a success-shaped null. Transport failures and
    /// unparsable responses are also surfaced rather than collapsed to a success-shaped
    /// <c>null</c>. The one exception is a genuine "property not found", which the server
    /// reports via HTTP 404 with no other structured reason: that case still returns
    /// <c>null</c>, preserving the documented not-found contract relied on by MCP tools like
    /// <c>maui_get_property</c> and <c>maui_assert</c>.
    /// </remarks>
    public async Task<string?> GetPropertyAsync(string elementId, string propertyName)
    {
        var (result, statusCode) = await GetJsonWithStatusAsync($"{UiApi}/elements/{elementId}/properties/{propertyName}");

        if (result.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException(
                $"Failed to get property '{propertyName}' on element '{elementId}': the DevFlow agent could not be reached or returned an unreadable response.");
        }

        if (result.ValueKind == JsonValueKind.Object)
        {
            if (result.TryGetProperty("success", out var successProperty)
                && successProperty.ValueKind == JsonValueKind.False)
            {
                var hasReason = result.TryGetProperty("reason", out var reasonProperty)
                    && reasonProperty.ValueKind == JsonValueKind.String;

                // A plain "property not found" is reported via HTTP 404 without any other
                // structured reason. Keep that as a null return rather than an exception.
                if (statusCode == 404 && !hasReason)
                    return null;

                var error = result.TryGetProperty("error", out var errorProperty)
                    && errorProperty.ValueKind == JsonValueKind.String
                        ? errorProperty.GetString()
                        : $"Failed to get property '{propertyName}' on element '{elementId}'.";

                if (hasReason)
                    error = $"{error} (reason: {reasonProperty.GetString()})";

                throw new InvalidOperationException(error);
            }

            if (result.TryGetProperty("value", out var val))
                return val.GetString();
        }

        return null;
    }

    /// <summary>
    /// Set a property value on an element.
    /// </summary>
    public Task<bool> SetPropertyAsync(string elementId, string propertyName, string value)
        => SetPropertyAsync(
            elementId,
            propertyName,
            value,
            captureEpoch: null,
            registryGeneration: null);

    public async Task<bool> SetPropertyAsync(
        string elementId,
        string propertyName,
        string value,
        long? captureEpoch,
        long? registryGeneration)
    {
        try
        {
            using var response = await SendWithTransientRetriesAsync(HttpMethod.Put, async () =>
            {
                var payload = new JsonObject
                {
                    ["value"] = value
                };
                AddCaptureMetadata(payload, captureEpoch, registryGeneration);
                using var content = DriverJson.CreateJsonContent(payload);
                return await _http.PutAsync($"{_baseUrl}{UiApi}/elements/{elementId}/properties/{propertyName}", content);
            });
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (IsExpectedClientException(ex)) { return false; }
    }

    public Task<ActionResult> SetPropertyResultAsync(
        string elementId,
        string propertyName,
        string value,
        long? captureEpoch,
        long? registryGeneration)
    {
        var payload = new JsonObject
        {
            ["value"] = value
        };
        AddCaptureMetadata(payload, captureEpoch, registryGeneration);
        return PutActionResultAsync(
            $"{UiApi}/elements/{elementId}/properties/{propertyName}",
            payload);
    }

    /// <summary>
    /// Get the app-scoped theme currently reported by the agent.
    /// </summary>
    public async Task<ThemeResult?> GetThemeAsync()
    {
        var result = await GetAsync<ThemeResult>($"{DeviceApi}/app/theme");
        return result == null ? null : WithSuccessfulThemeResult(result);
    }

    /// <summary>
    /// Set the app-scoped theme inside the running MAUI app.
    /// </summary>
    public async Task<ThemeResult> SetThemeAsync(DevFlowTheme theme)
    {
        var body = new JsonObject
        {
            ["theme"] = theme.ToProtocolString(),
        };

        var result = await PutJsonAsync<ThemeResult>($"{DeviceApi}/app/theme", body);
        return result != null ? WithSuccessfulThemeResult(result) : new ThemeResult
        {
            Theme = theme,
            RequestedTheme = theme,
            UserAppTheme = theme,
            Source = "app",
            Success = false,
            Message = "Failed to set app theme.",
        };
    }

    private static ThemeResult WithSuccessfulThemeResult(ThemeResult result)
        => new()
        {
            Theme = result.Theme,
            RequestedTheme = result.RequestedTheme,
            UserAppTheme = result.UserAppTheme,
            EffectiveTheme = result.EffectiveTheme,
            SupportedThemes = result.SupportedThemes,
            Source = result.Source,
            Success = true,
            Message = result.Message,
        };

    /// <summary>
    /// Retrieve application logs from the agent.
    /// </summary>
    public async Task<string> GetLogsAsync(int limit = 100, int skip = 0, string? source = null)
    {
        var path = $"{ApiV1}/logs?limit={limit}&skip={skip}";
        if (!string.IsNullOrEmpty(source) && source != "all")
            path += $"&source={Uri.EscapeDataString(source)}";
        return await GetStringWithTransientRetriesAsync($"{_baseUrl}{path}");
    }

    /// <summary>
    /// Send a CDP command to a Blazor WebView.
    /// </summary>
    public async Task<JsonElement> SendCdpCommandAsync(string method, JsonNode? @params = null, string? webviewId = null)
    {
        var path = $"{WebViewApi}/evaluate";
        if (!string.IsNullOrEmpty(webviewId))
            path += $"?webview={Uri.EscapeDataString(webviewId)}";

        var body = new JsonObject
        {
            ["method"] = method
        };
        if (@params != null)
            body["params"] = @params.DeepClone();

        using var response = await SendWithTransientRetriesAsync(HttpMethod.Post, async () =>
        {
            using var content = DriverJson.CreateJsonContent(body);
            return await _http.PostAsync($"{_baseUrl}{path}", content);
        });
        var responseBody = await response.Content.ReadAsStringAsync();
        return DriverJson.ParseElement(responseBody);
    }

    /// <summary>
    /// Gets the list of CDP WebViews registered with the agent.
    /// </summary>
    public async Task<JsonElement> GetCdpWebViewsAsync()
    {
        return await GetJsonAsync($"{WebViewApi}/contexts");
    }

    public async Task<string> GetCdpSourceAsync(string? webviewId = null)
    {
        var path = $"{WebViewApi}/source";
        if (!string.IsNullOrEmpty(webviewId))
            path += $"?webview={Uri.EscapeDataString(webviewId)}";
        return await GetStringWithTransientRetriesAsync($"{_baseUrl}{path}");
    }

    public async Task<bool> NavigateWebViewAsync(string url, string? contextId = null)
    {
        var payload = new JsonObject
        {
            ["url"] = url
        };

        if (!string.IsNullOrWhiteSpace(contextId))
            payload["contextId"] = contextId;

        return await PostActionAsync($"{WebViewApi}/navigate", payload);
    }

    public async Task<bool> ClickWebViewAsync(string selector, string? contextId = null)
    {
        var payload = new JsonObject
        {
            ["selector"] = selector
        };

        if (!string.IsNullOrWhiteSpace(contextId))
            payload["contextId"] = contextId;

        return await PostActionAsync($"{WebViewApi}/input/click", payload);
    }

    public async Task<bool> FillWebViewAsync(string selector, string text, string? contextId = null)
    {
        var payload = new JsonObject
        {
            ["selector"] = selector,
            ["text"] = text
        };

        if (!string.IsNullOrWhiteSpace(contextId))
            payload["contextId"] = contextId;

        return await PostActionAsync($"{WebViewApi}/input/fill", payload);
    }

    public async Task<bool> InsertWebViewTextAsync(string text, string? contextId = null)
    {
        var payload = new JsonObject
        {
            ["text"] = text
        };

        if (!string.IsNullOrWhiteSpace(contextId))
            payload["contextId"] = contextId;

        return await PostActionAsync($"{WebViewApi}/input/text", payload);
    }

    public async Task<string> HitTestAsync(double x, double y, int? window = null)
    {
        var result = await HitTestResultAsync(x, y, window);
        if (result.Success)
            return result.Body;

        throw new HttpRequestException(
            result.Reason ?? "DevFlow hit testing failed.",
            inner: null,
            statusCode: result.StatusCode.HasValue
                ? (HttpStatusCode)result.StatusCode.Value
                : null);
    }

    /// <summary>
    /// Hit-tests a point and returns a structured <see cref="UiReadResult"/> rather than
    /// throwing. This never throws <see cref="NotSupportedByAgentException"/>: a backend that
    /// does not implement hit testing is reported as a failure result with <c>Reason</c> set to
    /// <c>"not_supported"</c>, same as any other structured failure.
    /// </summary>
    public async Task<UiReadResult> HitTestResultAsync(
        double x,
        double y,
        int? window = null)
    {
        // Format with invariant culture so the outgoing request always uses '.' as the
        // decimal separator, regardless of the CLI/driver process's current culture.
        var path = $"{UiApi}/hit-test?x={x.ToString(CultureInfo.InvariantCulture)}&y={y.ToString(CultureInfo.InvariantCulture)}";
        if (window.HasValue)
            path += $"&window={window.Value}";

        try
        {
            var retryWindow = TimeSpan.FromSeconds(2);
            var startedAt = System.Diagnostics.Stopwatch.StartNew();
            for (var attempt = 0; ; attempt++)
            {
                using var response = await SendWithTransientRetriesAsync(
                    () => _http.GetAsync($"{_baseUrl}{path}"));
                var body = await response.Content.ReadAsStringAsync();
                string? reason = null;
                try
                {
                    var json = DriverJson.ParseElement(body);
                    if (json.ValueKind == JsonValueKind.Object
                        && json.TryGetProperty("reason", out var reasonProperty))
                    {
                        reason = reasonProperty.GetString();
                    }
                }
                catch (JsonException)
                {
                }

                var statusCode = (int)response.StatusCode;
                var retryable = response.StatusCode == HttpStatusCode.Conflict
                    && reason is "capture-changed-during-read" or "native-probe-busy";
                if (retryable)
                {
                    var delay = TimeSpan.FromMilliseconds(
                        Math.Min(25 * (1 << Math.Min(attempt, 4)), 250));
                    if (startedAt.Elapsed + delay < retryWindow)
                    {
                        await Task.Delay(delay);
                        continue;
                    }
                }

                return new UiReadResult(
                    response.IsSuccessStatusCode,
                    statusCode,
                    body,
                    reason,
                    retryable,
                    TransportFailure: false);
            }
        }
        // The shared retry path turns a uniform 501 not_supported envelope into this typed
        // exception. HitTestResultAsync's documented contract is to report failures through the
        // returned UiReadResult rather than throwing, so translate it back into one here.
        catch (NotSupportedByAgentException ex)
        {
            return new UiReadResult(
                Success: false,
                StatusCode: (int)HttpStatusCode.NotImplemented,
                Body: ex.Message,
                Reason: "not_supported",
                Retryable: false,
                TransportFailure: false);
        }
        catch (Exception ex) when (IsExpectedClientException(ex))
        {
            return new UiReadResult(
                Success: false,
                StatusCode: null,
                Body: string.Empty,
                Reason: null,
                Retryable: false,
                TransportFailure: true);
        }
    }

    public async Task<ProfilerCapabilities?> GetProfilerCapabilitiesAsync()
    {
        return await GetAsync<ProfilerCapabilities>($"{ProfilerApi}/capabilities");
    }

    public async Task<ProfilerSessionInfo?> StartProfilerAsync(int? sampleIntervalMs = null)
    {
        var payload = new JsonObject();
        if (sampleIntervalMs.HasValue)
            payload["sampleIntervalMs"] = sampleIntervalMs.Value;

        var response = await PostJsonAsync<ProfilerSessionEnvelope>($"{ProfilerApi}/sessions", payload);
        return response?.Session;
    }

    public async Task<ProfilerSessionInfo?> StopProfilerAsync(string? sessionId = null)
    {
        var response = await DeleteJsonAsync<ProfilerSessionEnvelope>($"{ProfilerApi}/sessions/{Uri.EscapeDataString(sessionId ?? "current")}");
        return response?.Session;
    }

    public async Task<ProfilerBatch?> GetProfilerSamplesAsync(
        long sampleCursor = 0,
        long markerCursor = 0,
        long spanCursor = 0,
        int limit = 500)
        => await GetProfilerSamplesAsync(null, sampleCursor, markerCursor, spanCursor, limit);

    public async Task<ProfilerBatch?> GetProfilerSamplesAsync(
        string? sessionId,
        long sampleCursor = 0,
        long markerCursor = 0,
        long spanCursor = 0,
        int limit = 500)
    {
        var resolvedSessionId = Uri.EscapeDataString(sessionId ?? "current");
        var url = $"{ProfilerApi}/sessions/{resolvedSessionId}/samples?sampleCursor={sampleCursor}&markerCursor={markerCursor}&spanCursor={spanCursor}&limit={limit}";
        return await GetAsync<ProfilerBatch>(url);
    }

    public async Task<bool> PublishProfilerMarkerAsync(
        string name,
        string type = "user.action",
        string? payloadJson = null)
    {
        return await PostActionAsync($"{ProfilerApi}/markers", new JsonObject
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

        var path = $"{ProfilerApi}/hotspots?limit={limit}&minDurationMs={minDurationMs}";
        if (!string.IsNullOrWhiteSpace(kind))
            path += $"&kind={Uri.EscapeDataString(kind)}";
        return await GetAsync<List<ProfilerHotspot>>(path) ?? new();
    }

    private async Task<T?> GetAsync<T>(string path) where T : class
    {
        try
        {
            var response = await GetStringWithRetryableUiReadAsync($"{_baseUrl}{path}");
            return DriverJson.Deserialize<T>(response);
        }
        catch (Exception ex) when (IsExpectedClientException(ex)) { return null; }
    }

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        try
        {
            var body = await GetStringWithRetryableUiReadAsync(
                $"{_baseUrl}{path}",
                returnErrorBody: true);
            if (string.IsNullOrWhiteSpace(body))
                return default;

            return DriverJson.ParseElement(body);
        }
        catch (Exception ex) when (IsExpectedClientException(ex)) { return default; }
    }

    /// <summary>
    /// Like <see cref="GetJsonAsync(string)"/>, but also retains the HTTP status code so
    /// callers can distinguish a genuine "not found" (404) from other structured failures
    /// without relying on message text matching.
    /// </summary>
    private async Task<(JsonElement Json, int? StatusCode)> GetJsonWithStatusAsync(string path)
    {
        int? statusCode = null;
        try
        {
            var body = await GetStringWithRetryableUiReadAsync(
                $"{_baseUrl}{path}",
                returnErrorBody: true,
                onStatusCode: code => statusCode = code);
            if (string.IsNullOrWhiteSpace(body))
                return (default, statusCode);

            return (DriverJson.ParseElement(body), statusCode);
        }
        catch (Exception ex) when (IsExpectedClientException(ex)) { return (default, statusCode); }
    }

    private static void AddCaptureMetadata(
        JsonObject payload,
        long? captureEpoch,
        long? registryGeneration)
    {
        if (captureEpoch.HasValue)
            payload["captureEpoch"] = captureEpoch.Value;
        if (registryGeneration.HasValue)
            payload["registryGeneration"] = registryGeneration.Value;
    }

    private static long? GetHeaderInt64(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values)
            && long.TryParse(values.FirstOrDefault(), out var value)
                ? value
                : null;

    private static int? GetHeaderInt32(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values)
            && int.TryParse(values.FirstOrDefault(), out var value)
                ? value
                : null;

    private async Task<bool> PostActionAsync(string path, JsonNode body)
    {
        try
        {
            using var response = await SendWithTransientRetriesAsync(HttpMethod.Post, async () =>
            {
                using var content = DriverJson.CreateJsonContent(body);
                return await _http.PostAsync($"{_baseUrl}{path}", content);
            });
            if (!response.IsSuccessStatusCode) return false;

            var responseBody = await response.Content.ReadAsStringAsync();
            var result = DriverJson.Deserialize<ActionResponse>(responseBody);
            return result?.Success == true;
        }
        catch (Exception ex) when (IsExpectedClientException(ex)) { return false; }
    }

    private Task<ActionResult> PostActionResultAsync(string path, JsonNode body)
        => SendActionResultAsync(HttpMethod.Post, path, body);

    private Task<ActionResult> PutActionResultAsync(string path, JsonNode body)
        => SendActionResultAsync(HttpMethod.Put, path, body);

    private async Task<ActionResult> SendActionResultAsync(
        HttpMethod method,
        string path,
        JsonNode body)
    {
        try
        {
            using var response = await SendWithTransientRetriesAsync(method, async () =>
            {
                using var content = DriverJson.CreateJsonContent(body);
                using var request = new HttpRequestMessage(method, $"{_baseUrl}{path}")
                {
                    Content = content
                };
                return await _http.SendAsync(request);
            });
            var responseBody = await response.Content.ReadAsStringAsync();
            string? reason = null;
            string? error = null;
            var actionSucceeded = response.IsSuccessStatusCode;
            var explicitlyRetryable = false;
            try
            {
                var result = DriverJson.ParseElement(responseBody);
                if (result.ValueKind == JsonValueKind.Object)
                {
                    if (result.TryGetProperty("success", out var successProperty)
                        && successProperty.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        actionSucceeded &= successProperty.GetBoolean();
                    }
                    if (result.TryGetProperty("reason", out var reasonProperty))
                        reason = reasonProperty.GetString();
                    if (result.TryGetProperty("error", out var errorProperty)
                        && errorProperty.ValueKind == JsonValueKind.String)
                    {
                        error = errorProperty.GetString();
                    }
                    if (result.TryGetProperty("details", out var details)
                        && details.ValueKind == JsonValueKind.Object
                        && details.TryGetProperty("retryable", out var retryableProperty)
                        && retryableProperty.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        explicitlyRetryable = retryableProperty.GetBoolean();
                    }
                }
            }
            catch (JsonException)
            {
            }

            var statusCode = (int)response.StatusCode;
            return new ActionResult(
                actionSucceeded,
                statusCode,
                reason,
                Retryable: explicitlyRetryable
                    || reason is "stale-capture-epoch" or "ui-mutation-busy",
                TransportFailure: false)
            {
                Error = error
            };
        }
        // The shared retry path turns a uniform 501 not_supported envelope into this typed
        // exception. The *ResultAsync action APIs (Tap, Fill, Clear, Focus, Key, Gesture, Scroll,
        // SetProperty) document reporting failures through the returned ActionResult rather than
        // throwing, so translate it back into one here.
        catch (NotSupportedByAgentException ex)
        {
            return new ActionResult(
                Success: false,
                StatusCode: (int)HttpStatusCode.NotImplemented,
                Reason: "not_supported",
                Retryable: false,
                TransportFailure: false)
            {
                Error = ex.Message
            };
        }
        catch (Exception ex) when (IsExpectedClientException(ex))
        {
            return new ActionResult(
                Success: false,
                StatusCode: null,
                Reason: null,
                Retryable: true,
                TransportFailure: true);
        }
    }

    private async Task<T?> PostJsonAsync<T>(string path, JsonNode body) where T : class
    {
        try
        {
            using var response = await SendWithTransientRetriesAsync(HttpMethod.Post, async () =>
            {
                using var content = DriverJson.CreateJsonContent(body);
                return await _http.PostAsync($"{_baseUrl}{path}", content);
            });
            var responseBody = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;
            return DriverJson.Deserialize<T>(responseBody);
        }
        catch (Exception ex) when (IsExpectedClientException(ex))
        {
            return null;
        }
    }

    private async Task<T?> PutJsonAsync<T>(string path, JsonNode body) where T : class
    {
        try
        {
            using var content = DriverJson.CreateJsonContent(body);
            var response = await _http.PutAsync($"{_baseUrl}{path}", content);
            if (!response.IsSuccessStatusCode)
                return null;

            var responseBody = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;
            return DriverJson.Deserialize<T>(responseBody);
        }
        catch
        {
            return null;
        }
    }

    private async Task<T?> DeleteJsonAsync<T>(string path) where T : class
    {
        try
        {
            using var response = await SendWithTransientRetriesAsync(HttpMethod.Delete, () => _http.DeleteAsync($"{_baseUrl}{path}"));
            if (!response.IsSuccessStatusCode)
                return null;
            var responseBody = await response.Content.ReadAsStringAsync();
            return DriverJson.Deserialize<T>(responseBody);
        }
        catch (Exception ex) when (IsExpectedClientException(ex))
        {
            return null;
        }
    }

    private async Task<bool> DeleteActionAsync(string path)
    {
        try
        {
            using var response = await SendWithTransientRetriesAsync(HttpMethod.Delete, () => _http.DeleteAsync($"{_baseUrl}{path}"));
            if (!response.IsSuccessStatusCode)
                return false;

            var responseBody = await response.Content.ReadAsStringAsync();
            var result = DriverJson.Deserialize<ActionResponse>(responseBody);
            return result?.Success == true;
        }
        catch (Exception ex) when (IsExpectedClientException(ex))
        {
            return false;
        }
    }

    private async Task<string> GetStringWithTransientRetriesAsync(string url)
    {
        using var response = await SendWithTransientRetriesAsync(() => _http.GetAsync(url));
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GET {url} failed with {(int)response.StatusCode}.", null, response.StatusCode);

        return body;
    }

    private async Task<string> GetStringWithRetryableUiReadAsync(
        string url,
        bool returnErrorBody = false,
        Action<int>? onStatusCode = null)
    {
        var retryWindow = TimeSpan.FromSeconds(1);
        var startedAt = System.Diagnostics.Stopwatch.StartNew();
        for (var attempt = 0; ; attempt++)
        {
            using var response = await SendWithTransientRetriesAsync(() => _http.GetAsync(url));
            var body = await response.Content.ReadAsStringAsync();
            onStatusCode?.Invoke((int)response.StatusCode);
            if (response.IsSuccessStatusCode)
                return body;

            if (response.StatusCode == HttpStatusCode.Conflict
                && IsCaptureChangedDuringRead(body))
            {
                var delay = TimeSpan.FromMilliseconds(Math.Min(25 * (1 << Math.Min(attempt, 4)), 250));
                if (startedAt.Elapsed + delay < retryWindow)
                {
                    await Task.Delay(delay);
                    continue;
                }

                throw new InvalidOperationException(
                    "The UI kept changing while DevFlow was reading it. Retry the operation after the current UI update completes.");
            }

            if (returnErrorBody)
                return body;

            response.EnsureSuccessStatusCode();
        }
    }

    private static bool IsCaptureChangedDuringRead(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;

        try
        {
            var json = DriverJson.ParseElement(body);
            return json.ValueKind == JsonValueKind.Object
                && json.TryGetProperty("reason", out var reason)
                && reason.GetString() == "capture-changed-during-read";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private Task<T> SendWithTransientRetriesAsync<T>(Func<Task<T>> send)
        => SendWithTransientRetriesAsync(HttpMethod.Get, send);

    private async Task<T> SendWithTransientRetriesAsync<T>(HttpMethod method, Func<Task<T>> send)
    {
        var retryCount = Math.Max(0, TransientFailureRetryCount);
        var isMutating = method != HttpMethod.Get;
        if (isMutating && !RetryMutatingRequests)
            retryCount = 0;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var result = await send();

                // A backend that cannot serve an endpoint answers 501 with a not_supported
                // envelope. Surface that as a typed exception instead of letting it decay into a
                // null/false that looks identical to a genuine failure.
                if (result is HttpResponseMessage message)
                    await ThrowIfNotSupportedAsync(message);

                return result;
            }
            catch (Exception ex) when (IsTransientTransportException(ex) && attempt < retryCount)
            {
                var delay = GetTransientFailureRetryDelay(attempt);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay);
            }
        }
    }

    private static async Task ThrowIfNotSupportedAsync(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.NotImplemented)
            return;

        var body = await response.Content.ReadAsStringAsync();
        string capability;
        string? reason = null;

        try
        {
            var json = DriverJson.ParseElement(body);

            // Only the uniform not_supported envelope maps to the typed exception. Endpoints that
            // answer 501 with their own success/error shape keep their existing contract.
            if (json.ValueKind != JsonValueKind.Object ||
                !json.TryGetProperty("error", out var kind) ||
                kind.ValueKind != JsonValueKind.String ||
                kind.GetString() != "not_supported")
            {
                return;
            }

            capability = json.TryGetProperty("capability", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString() ?? "unknown"
                : "unknown";

            if (json.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String)
                reason = r.GetString();
        }
        catch (JsonException)
        {
            // Non-JSON 501 — leave it to the caller's existing error handling.
            return;
        }

        // The caller never receives the message, so release it here.
        response.Dispose();
        throw new NotSupportedByAgentException(capability, reason);
    }

    private TimeSpan GetTransientFailureRetryDelay(int attempt)
    {
        if (TransientFailureRetryDelay <= TimeSpan.Zero)
            return TimeSpan.Zero;

        var multiplier = Math.Min(attempt + 1, 5);
        return TimeSpan.FromMilliseconds(TransientFailureRetryDelay.TotalMilliseconds * multiplier);
    }

    private static bool IsExpectedClientException(Exception ex)
        => ex is HttpRequestException or TaskCanceledException or IOException or JsonException
            || (ex.InnerException is not null && IsExpectedClientException(ex.InnerException));

    private static bool IsTransientTransportException(Exception ex)
    {
        switch (ex)
        {
            case HttpRequestException httpEx when httpEx.InnerException is SocketException:
                return true;
            case IOException:
                return true;
            // Only retry a TaskCanceledException when it represents a real
            // transport failure (i.e. wraps another exception that is not the
            // HttpClient timeout marker). A bare TCE with no inner is almost
            // always a caller-initiated CancellationToken cancellation, which
            // must not be retried.
            case TaskCanceledException tcEx when tcEx.InnerException is not null and not TimeoutException:
                return true;
        }
        return ex.InnerException is not null && IsTransientTransportException(ex.InnerException);
    }

    // ── DevFlow Actions ──

    private const string InvokeApi = $"{ApiV1}/invoke";

    /// <summary>
    /// List all registered DevFlow Actions (methods annotated with [DevFlowAction]).
    /// </summary>
    public async Task<JsonElement> ListActionsAsync()
        => await GetJsonAsync($"{InvokeApi}/actions");

    /// <summary>
    /// Invoke a registered DevFlow Action by name.
    /// </summary>
    public async Task<InvokeResult?> InvokeActionAsync(string actionName, JsonArray? args = null)
    {
        var body = new JsonObject();
        if (args != null)
            body["args"] = args;
        return await PostJsonAsync<InvokeResult>($"{InvokeApi}/actions/{Uri.EscapeDataString(actionName)}", body);
    }

    // ── Preferences ──

    public async Task<JsonElement> GetPreferencesAsync(string? sharedName = null)
    {
        var path = $"{StorageApi}/preferences";
        if (!string.IsNullOrEmpty(sharedName))
            path += $"?sharedName={Uri.EscapeDataString(sharedName)}";
        return await GetJsonAsync(path);
    }

    public async Task<JsonElement> GetPreferenceAsync(string key, string? type = null, string? sharedName = null)
    {
        var path = $"{StorageApi}/preferences/{Uri.EscapeDataString(key)}";
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

        using var response = await SendWithTransientRetriesAsync(HttpMethod.Put, async () =>
        {
            using var content = DriverJson.CreateJsonContent(body);
            return await _http.PutAsync($"{_baseUrl}{StorageApi}/preferences/{Uri.EscapeDataString(key)}", content);
        });
        var responseBody = await response.Content.ReadAsStringAsync();
        return DriverJson.ParseElement(responseBody);
    }

    public async Task<JsonElement> DeletePreferenceAsync(string key, string? sharedName = null)
    {
        var path = $"{StorageApi}/preferences/{Uri.EscapeDataString(key)}";
        if (!string.IsNullOrEmpty(sharedName))
            path += $"?sharedName={Uri.EscapeDataString(sharedName)}";
        using var response = await SendWithTransientRetriesAsync(HttpMethod.Delete, () => _http.DeleteAsync($"{_baseUrl}{path}"));
        var responseBody = await response.Content.ReadAsStringAsync();
        return DriverJson.ParseElement(responseBody);
    }

    public async Task<bool> ClearPreferencesAsync(string? sharedName = null)
    {
        var path = $"{StorageApi}/preferences";
        if (!string.IsNullOrEmpty(sharedName))
            path += $"?sharedName={Uri.EscapeDataString(sharedName)}";
        return await DeleteActionAsync(path);
    }

    // ── Secure Storage ──

    public async Task<JsonElement> GetSecureStorageAsync(string key)
    {
        return await GetJsonAsync($"{StorageApi}/secure/{Uri.EscapeDataString(key)}");
    }

    public async Task<JsonElement> SetSecureStorageAsync(string key, string value)
    {
        using var response = await SendWithTransientRetriesAsync(HttpMethod.Put, async () =>
        {
            using var content = DriverJson.CreateJsonContent(new JsonObject
            {
                ["value"] = value
            });
            return await _http.PutAsync($"{_baseUrl}{StorageApi}/secure/{Uri.EscapeDataString(key)}", content);
        });
        var responseBody = await response.Content.ReadAsStringAsync();
        return DriverJson.ParseElement(responseBody);
    }

    public async Task<JsonElement> DeleteSecureStorageAsync(string key)
    {
        using var response = await SendWithTransientRetriesAsync(HttpMethod.Delete, () => _http.DeleteAsync($"{_baseUrl}{StorageApi}/secure/{Uri.EscapeDataString(key)}"));
        var responseBody = await response.Content.ReadAsStringAsync();
        return DriverJson.ParseElement(responseBody);
    }

    public async Task<bool> ClearSecureStorageAsync()
    {
        return await DeleteActionAsync($"{StorageApi}/secure");
    }

    // ── Platform info ──

    public async Task<JsonElement> GetPlatformInfoAsync(string endpoint)
    {
        var normalizedEndpoint = endpoint switch
        {
            "app-info" => "app",
            "device-info" => "info",
            "device-display" => "display",
            _ => endpoint
        };
        return await GetJsonAsync($"{DeviceApi}/{normalizedEndpoint}");
    }

    public async Task<JsonElement> GetGeolocationAsync(string? accuracy = null, int? timeoutSeconds = null)
    {
        var path = $"{DeviceApi}/geolocation";
        var qs = new List<string>();
        if (!string.IsNullOrEmpty(accuracy)) qs.Add($"accuracy={Uri.EscapeDataString(accuracy)}");
        if (timeoutSeconds.HasValue) qs.Add($"timeout={timeoutSeconds.Value}");
        if (qs.Count > 0) path += "?" + string.Join("&", qs);
        return await GetJsonAsync(path);
    }

    // ── Sensors ──

    public async Task<JsonElement> GetSensorsAsync()
    {
        return await GetJsonAsync($"{DeviceApi}/sensors");
    }

    public async Task<bool> StartSensorAsync(string sensor, string? speed = null)
    {
        var path = $"{DeviceApi}/sensors/{Uri.EscapeDataString(sensor)}/start";
        if (!string.IsNullOrEmpty(speed))
            path += $"?speed={Uri.EscapeDataString(speed)}";
        return await PostActionAsync(path, new JsonObject());
    }

    public async Task<bool> StopSensorAsync(string sensor)
    {
        return await PostActionAsync($"{DeviceApi}/sensors/{Uri.EscapeDataString(sensor)}/stop", new JsonObject());
    }

    // ── Jobs ──

    public async Task<JsonElement> GetJobsAsync()
    {
        return await GetJsonAsync($"{DeviceApi}/jobs");
    }

    public async Task<JsonElement> RunJobAsync(string identifier, string? type = null)
    {
        try
        {
            var payload = new JsonObject();
            if (!string.IsNullOrWhiteSpace(type))
                payload["type"] = type;

            using var response = await SendWithTransientRetriesAsync(HttpMethod.Post, async () =>
            {
                using var content = DriverJson.CreateJsonContent(payload);
                return await _http.PostAsync($"{_baseUrl}{DeviceApi}/jobs/{Uri.EscapeDataString(identifier)}/run", content);
            });
            var responseBody = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseBody))
                return default;
            return DriverJson.ParseElement(responseBody);
        }
        catch (Exception ex) when (IsExpectedClientException(ex)) { return default; }
    }

    // ── Files ──

    public async Task<JsonElement> ListStorageRootsAsync()
    {
        return await GetJsonAsync($"{StorageApi}/roots");
    }

    public async Task<JsonElement> ListFilesAsync(string? path = null, string? root = null)
    {
        var url = $"{StorageApi}/files";
        var query = BuildStorageFilesQuery(path, root);
        if (!string.IsNullOrEmpty(query))
            url += query;

        return await GetJsonAsync(url);
    }

    public async Task<JsonElement> DownloadFileAsync(string path, string? root = null)
    {
        return await GetJsonAsync($"{StorageApi}/files/{Uri.EscapeDataString(path)}{BuildRootQuery(root)}");
    }

    public async Task<JsonElement> UploadFileAsync(string path, string contentBase64, string? root = null)
    {
        var body = new JsonObject { ["contentBase64"] = contentBase64 };
        using var response = await SendWithTransientRetriesAsync(HttpMethod.Put, async () =>
        {
            using var content = DriverJson.CreateJsonContent(body);
            return await _http.PutAsync($"{_baseUrl}{StorageApi}/files/{Uri.EscapeDataString(path)}{BuildRootQuery(root)}", content);
        });
        var responseBody = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(responseBody))
            return default;

        return DriverJson.ParseElement(responseBody);
    }

    public async Task<bool> DeleteFileAsync(string path, string? root = null)
    {
        return await DeleteActionAsync($"{StorageApi}/files/{Uri.EscapeDataString(path)}{BuildRootQuery(root)}");
    }

    private static string BuildStorageFilesQuery(string? path, string? root)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(path))
            query.Add($"path={Uri.EscapeDataString(path)}");
        if (!string.IsNullOrEmpty(root))
            query.Add($"root={Uri.EscapeDataString(root)}");

        return query.Count == 0 ? string.Empty : "?" + string.Join("&", query);
    }

    private static string BuildRootQuery(string? root)
        => string.IsNullOrEmpty(root) ? string.Empty : $"?root={Uri.EscapeDataString(root)}";

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
            var url = $"{_baseUrl}{NetworkApi}/requests?limit={limit}";
            if (!string.IsNullOrEmpty(host)) url += $"&host={Uri.EscapeDataString(host)}";
            if (!string.IsNullOrEmpty(method)) url += $"&method={Uri.EscapeDataString(method)}";

            var response = await GetStringWithTransientRetriesAsync(url);
            return DriverJson.Deserialize<List<NetworkRequest>>(response) ?? new();
        }
        catch (Exception ex) when (IsExpectedClientException(ex)) { return new(); }
    }

    public async Task<NetworkRequest?> GetNetworkRequestDetailAsync(string id)
    {
        try
        {
            var response = await GetStringWithTransientRetriesAsync($"{_baseUrl}{NetworkApi}/requests/{Uri.EscapeDataString(id)}");
            return DriverJson.Deserialize<NetworkRequest>(response);
        }
        catch (Exception ex) when (IsExpectedClientException(ex)) { return null; }
    }

    public async Task<bool> ClearNetworkRequestsAsync()
    {
        return await DeleteActionAsync($"{NetworkApi}/requests");
    }

    /// <summary>
    /// Returns the WebSocket URL for live network monitoring.
    /// </summary>
    public string GetNetworkWebSocketUrl()
    {
        var wsBase = _baseUrl.Replace("http://", "ws://").Replace("https://", "wss://");
        return $"{wsBase}/ws/v1/network";
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

/// <summary>
/// Result of an action request made through one of the <c>*ResultAsync</c> action APIs
/// (e.g. <see cref="AgentClient.TapResultAsync"/>, <see cref="AgentClient.FillResultAsync"/>).
/// Reports failures — including a backend not supporting the action's capability, surfaced with
/// <see cref="Reason"/> set to <c>"not_supported"</c> — through this structure instead of
/// throwing <see cref="NotSupportedByAgentException"/>.
/// </summary>
public readonly record struct ActionResult(
    bool Success,
    int? StatusCode,
    string? Reason,
    bool Retryable,
    bool TransportFailure)
{
    /// <summary>Human-readable server error message (e.g. from the response's "error" field), when available.</summary>
    public string? Error { get; init; }
}

/// <summary>
/// Result of a UI read request made through <see cref="AgentClient.HitTestResultAsync"/>.
/// Reports failures — including a backend not supporting the capability, surfaced with
/// <see cref="Reason"/> set to <c>"not_supported"</c> — through this structure instead of
/// throwing <see cref="NotSupportedByAgentException"/>.
/// </summary>
public readonly record struct UiReadResult(
    bool Success,
    int? StatusCode,
    string Body,
    string? Reason,
    bool Retryable,
    bool TransportFailure);

/// <summary>
/// Outcome of a gesture, including which tier serviced it. Gestures can be satisfied by a
/// managed MAUI gesture recognizer or by native platform injection, and the two behave
/// differently enough that it is worth knowing which one ran.
/// </summary>
public class GestureResult
{
    [System.Text.Json.Serialization.JsonPropertyName("success")]
    public bool Success { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string? Type { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("elementId")]
    public string? ElementId { get; set; }

    /// <summary>"recognizer", "native", "action", "scroll" (legacy swipe fallback), or "none".</summary>
    [System.Text.Json.Serialization.JsonPropertyName("handledBy")]
    public string? HandledBy { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("platform")]
    public string? Platform { get; set; }

    /// <summary>What actually serviced the gesture, e.g. "MKMapView.Camera.CenterCoordinateDistance /2".</summary>
    [System.Text.Json.Serialization.JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class AgentStatus
{
    [System.Text.Json.Serialization.JsonPropertyName("agent")]
    public AgentDescriptor? Agent { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("device")]
    public DeviceDescriptor? Device { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("app")]
    public AppDescriptor? App { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("capabilities")]
    public AgentCapabilities? Capabilities { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("extensions")]
    public ExtensionsMarker? Extensions { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("running")]
    public bool Running { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string? Version => Agent?.Version;
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Platform => Device?.Platform;
    [System.Text.Json.Serialization.JsonIgnore]
    public string? DeviceType => Device?.DeviceType;
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Idiom => Device?.Idiom;
    [System.Text.Json.Serialization.JsonIgnore]
    public string? AppName => App?.Name;
}

public class AgentDescriptor
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string? Name { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("version")]
    public string? Version { get; set; }
    /// <summary>
    /// Human-readable app framework, e.g. <c>".NET MAUI"</c> or <c>".NET"</c>.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("framework")]
    public string? Framework { get; set; }

    /// <summary>
    /// Machine-readable app framework: <c>"maui"</c> or <c>"native"</c>.
    /// Null when talking to an agent built before the field was introduced.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("frameworkId")]
    public string? FrameworkId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("frameworkVersion")]
    public string? FrameworkVersion { get; set; }

    /// <summary>
    /// The UI framework the agent walks: <c>"maui-controls"</c>, <c>"android-views"</c>,
    /// <c>"uikit"</c>, <c>"appkit"</c>, <c>"gtk"</c> or <c>"wpf"</c>.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("uiFramework")]
    public string? UiFramework { get; set; }
}

public class DeviceDescriptor
{
    [System.Text.Json.Serialization.JsonPropertyName("platform")]
    public string? Platform { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("deviceType")]
    public string? DeviceType { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("idiom")]
    public string? Idiom { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("displayDensity")]
    public double? DisplayDensity { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("windowWidth")]
    public double? WindowWidth { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("windowHeight")]
    public double? WindowHeight { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("windowCount")]
    public int? WindowCount { get; set; }
}

public class AppDescriptor
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string? Name { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("packageId")]
    public string? PackageId { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("version")]
    public string? Version { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("build")]
    public string? Build { get; set; }
}

public class AgentCapabilities
{
    [System.Text.Json.Serialization.JsonPropertyName("ui")]
    public bool Ui { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("screenshots")]
    public bool Screenshots { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("webview")]
    public bool WebView { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("network")]
    public bool Network { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("logs")]
    public bool Logs { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("sensors")]
    public bool Sensors { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("storage")]
    public bool Storage { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("profiler")]
    public bool Profiler { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("jobs")]
    public bool Jobs { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("theme")]
    public bool Theme { get; set; }
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

/// <summary>
/// Result of a DevFlow Action invocation.
/// </summary>
public class InvokeResult
{
    [System.Text.Json.Serialization.JsonPropertyName("success")]
    public bool Success { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("returnValue")]
    public string? ReturnValue { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("returnType")]
    public string? ReturnType { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("error")]
    public string? Error { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("action")]
    public string? Action { get; set; }
}