using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Inspector;

/// <summary>
/// Lightweight HTTP server that serves the DevFlow Web Inspector.
/// Generates an interactive HTML page representing the native app's visual tree
/// and proxies interaction commands to the DevFlow agent.
/// </summary>
public sealed class InspectorServer : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private readonly int _port;
    private readonly string _agentHost;
    private readonly int _agentPort;
    private readonly AgentClient _client;
    private readonly object _cacheLock = new();
    // Lifetime cancellation source; cancelled in Dispose() so broker-mode WS proxies
    // (which never call Start() to create _cts) still see shutdown.
    private readonly CancellationTokenSource _lifetimeCts = new();
    private byte[]? _cachedScreenshot;
    private string? _cachedScreenshotElementId;
    private long? _cachedScreenshotCaptureEpoch;
    private bool _cachedScreenshotFullscreen;
    private readonly Dictionary<long, byte[]> _screenshotSnapshots = [];
    private readonly Queue<long> _screenshotSnapshotOrder = [];
    private long _nextScreenshotSnapshotId;
    private DateTime _screenshotCacheTime;
    private string? _rootPageId;
    // The window-absolute offset of the screenshotted root page element.
    // Used to translate between viewport coordinates (relative to the screenshot)
    // and window coordinates (used by the agent's hit-test/tap/scroll APIs).
    private double _rootOffsetX;
    private double _rootOffsetY;
    private long? _captureEpoch;
    private long? _registryGeneration;
    private int? _windowId;
    private bool _fullscreenScreenshot;
    private static readonly TimeSpan ScreenshotCacheDuration = TimeSpan.FromMilliseconds(200);
    private const int MaxScreenshotSnapshots = 16;

    // Cap request bodies to avoid local DoS via huge POST payloads.
    private const long MaxRequestBodyBytes = 1_048_576; // 1 MB

    public int Port => _port;

    /// <summary>
    /// Port of the underlying DevFlow agent this inspector is proxying to. Used by
    /// the broker to detect when an agent has reconnected on a different port and
    /// the cached InspectorServer's AgentClient is now pointing at a dead port.
    /// </summary>
    public int AgentPort => _agentPort;

    public InspectorServer(int port, string agentHost, int agentPort)
    {
        _port = port;
        _agentHost = agentHost;
        _agentPort = agentPort;
        _client = new AgentClient(agentHost, agentPort);
    }

    private void InvalidateScreenshotCache()
    {
        lock (_cacheLock)
        {
            _cachedScreenshot = null;
            _cachedScreenshotCaptureEpoch = null;
        }
    }

    private long? RememberScreenshot(byte[]? screenshot)
    {
        if (screenshot is not { Length: > 0 })
            return null;

        var snapshotId = Interlocked.Increment(ref _nextScreenshotSnapshotId);
        lock (_cacheLock)
        {
            _screenshotSnapshots[snapshotId] = screenshot;
            _screenshotSnapshotOrder.Enqueue(snapshotId);
            while (_screenshotSnapshotOrder.Count > MaxScreenshotSnapshots)
            {
                var expiredId = _screenshotSnapshotOrder.Dequeue();
                _screenshotSnapshots.Remove(expiredId);
            }
        }

        return snapshotId;
    }

    private byte[]? GetRememberedScreenshot(string queryString)
    {
        foreach (var pair in queryString.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2
                && parts[0].Equals("t", StringComparison.Ordinal)
                && long.TryParse(Uri.UnescapeDataString(parts[1]), out var parsedId))
            {
                lock (_cacheLock)
                    return _screenshotSnapshots.GetValueOrDefault(parsedId);
            }
        }

        return null;
    }

    /// <summary>
    /// Safely extract the "elements" array from a hit-test response. Returns false if
    /// the agent returned malformed JSON, missing the property, or wrong shape — in which
    /// case the inspector should fall back to a "no element here" path instead of crashing
    /// and leaking the exception text to the browser.
    /// </summary>
    private static bool TryParseHitTestElements(
        string? hitResult,
        out JsonDocument? doc,
        out JsonElement elements,
        out long? captureEpoch,
        out long? registryGeneration)
    {
        doc = null;
        elements = default;
        captureEpoch = null;
        registryGeneration = null;
        if (string.IsNullOrEmpty(hitResult)) return false;
        try
        {
            doc = JsonDocument.Parse(hitResult);
            if (!doc.RootElement.TryGetProperty("elements", out elements) || elements.ValueKind != JsonValueKind.Array)
            {
                doc.Dispose();
                doc = null;
                return false;
            }

            if (doc.RootElement.TryGetProperty("captureEpoch", out var epochProperty)
                && epochProperty.TryGetInt64(out var parsedEpoch))
            {
                captureEpoch = parsedEpoch;
            }
            if (doc.RootElement.TryGetProperty("registryGeneration", out var generationProperty)
                && generationProperty.TryGetInt64(out var parsedGeneration))
            {
                registryGeneration = parsedGeneration;
            }
            return true;
        }
        catch (JsonException)
        {
            doc?.Dispose();
            doc = null;
            return false;
        }
    }

    private static bool TryGetNextHitTestCandidate(
        string? hitResult,
        HashSet<string> attemptedIds,
        bool preferScrollable,
        out string? elementId,
        out long? captureEpoch,
        out long? registryGeneration)
    {
        elementId = null;
        if (!TryParseHitTestElements(
            hitResult,
            out var document,
            out var elements,
            out captureEpoch,
            out registryGeneration))
        {
            return false;
        }

        using (document)
        {
            JsonElement? bestCandidate = null;
            var bestScore = int.MinValue;
            foreach (var element in elements.EnumerateArray())
            {
                if (!element.TryGetProperty("id", out var idProperty))
                    continue;

                var candidateId = idProperty.GetString();
                if (string.IsNullOrEmpty(candidateId) || attemptedIds.Contains(candidateId))
                    continue;

                if (!preferScrollable)
                {
                    attemptedIds.Add(candidateId);
                    elementId = candidateId;
                    return true;
                }

                var score = GetScrollableCandidateScore(element);
                if (score > bestScore)
                {
                    bestCandidate = element;
                    bestScore = score;
                }
            }

            if (bestCandidate is { } candidate
                && candidate.TryGetProperty("id", out var bestIdProperty))
            {
                var candidateId = bestIdProperty.GetString();
                if (!string.IsNullOrEmpty(candidateId))
                {
                    attemptedIds.Add(candidateId);
                    elementId = candidateId;
                    return true;
                }
            }
        }

        return false;
    }

    private static int GetScrollableCandidateScore(JsonElement element)
    {
        if (element.TryGetProperty("type", out var typeProperty))
        {
            var type = typeProperty.GetString();
            if (type is "ScrollView" or "CollectionView" or "ListView"
                or "RecyclerView" or "ItemsView" or "ScrollViewer" or "UIScrollView")
            {
                return 2;
            }
        }

        if (element.TryGetProperty("capabilities", out var capabilities)
            && capabilities.ValueKind == JsonValueKind.Array
            && capabilities.EnumerateArray().Any(capability =>
                capability.ValueKind == JsonValueKind.String
                && capability.GetString() == "scroll"))
        {
            return 1;
        }

        return 0;
    }

    private static bool TryGetActionCaptureMetadata(
        JsonElement root,
        bool required,
        out long? captureEpoch,
        out long? registryGeneration,
        out string? error)
    {
        captureEpoch = null;
        registryGeneration = null;
        error = null;

        if (root.TryGetProperty("captureEpoch", out var epochProperty))
        {
            if (!epochProperty.TryGetInt64(out var parsedEpoch) || parsedEpoch <= 0)
            {
                error = "captureEpoch must be a positive integer";
                return false;
            }

            captureEpoch = parsedEpoch;
        }

        if (root.TryGetProperty("registryGeneration", out var generationProperty))
        {
            if (!generationProperty.TryGetInt64(out var parsedGeneration) || parsedGeneration < 0)
            {
                error = "registryGeneration must be a non-negative integer";
                return false;
            }

            registryGeneration = parsedGeneration;
        }

        if (required && captureEpoch is null)
        {
            error = "captureEpoch is required for elementId actions";
            return false;
        }

        if (registryGeneration is not null && captureEpoch is null)
        {
            error = "captureEpoch is required when registryGeneration is supplied";
            return false;
        }

        return true;
    }

    private static (int, string, byte[]) CaptureMetadataError(string error)
        => (400, "application/json", JsonSerializer.SerializeToUtf8Bytes(new { error }));

    private static (int, string, byte[]) ActionOutcomeResponse(ActionResult outcome)
    {
        if (outcome.Success)
            return (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"));

        var statusCode = outcome.StatusCode is >= 400 and <= 599
            ? outcome.StatusCode.Value
            : 502;
        return (
            statusCode,
            "application/json",
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                ok = false,
                reason = outcome.Reason,
                retryable = outcome.Retryable
            }));
    }

    private static (int, string, byte[]) UiReadOutcomeResponse(UiReadResult outcome)
    {
        var statusCode = outcome.StatusCode is >= 400 and <= 599
            ? outcome.StatusCode.Value
            : 502;
        return (
            statusCode,
            "application/json",
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                ok = false,
                reason = outcome.Reason,
                retryable = outcome.Retryable
            }));
    }

    private Task<bool> RequiresCaptureEpochAsync()
        => DetectCaptureEpochRequirementAsync();

    private async Task<bool> DetectCaptureEpochRequirementAsync()
    {
        try
        {
            var response = await _client.GetCapabilitiesAsync();
            if (!response.TryGetProperty("capabilities", out var capabilities)
                || !capabilities.TryGetProperty("ui.actions", out var uiActions)
                || !uiActions.TryGetProperty("features", out var features)
                || features.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            return features.EnumerateArray().Any(feature =>
                feature.ValueKind == JsonValueKind.String
                && feature.GetString() == "stale-capture-rejection");
        }
        catch
        {
            lock (_cacheLock)
                return _captureEpoch.HasValue;
        }
    }

    /// <summary>
    /// Handles an HTTP request from the broker, routing it through the inspector logic.
    /// This allows the broker to serve inspector pages without a separate listener.
    /// </summary>
    public async Task HandleBrokerRequestAsync(HttpListenerContext context, string path)
    {
        try
        {
            // Origin port check uses the broker's listening port so that a page on
            // any other loopback port (e.g. a separate dev server on :3000) is
            // rejected — see LocalOriginValidator for the RFC 6454 rationale.
            var brokerPort = context.Request.Url?.Port ?? 0;

            // Handle WebSocket upgrade for /ws/events
            if (context.Request.IsWebSocketRequest && path.TrimEnd('/') == "/ws/events")
            {
                // Reject cross-origin WebSocket subscriptions (any web page can open a
                // WebSocket regardless of same-origin policy — the server must enforce).
                var origin = context.Request.Headers["Origin"];
                if (!LocalOriginValidator.IsAllowed(origin, brokerPort))
                {
                    context.Response.StatusCode = 403;
                    context.Response.Close();
                    return;
                }
                await HandleBrokerWebSocketProxy(context);
                return;
            }

            var method = context.Request.HttpMethod;

            // Mitigate CSRF on state-mutating endpoints: a browser can dispatch a "simple"
            // cross-origin POST (text/plain or form-encoded) without a preflight, even
            // though it cannot read the response. Reject non-loopback Origins on POST.
            if (method == "POST")
            {
                var origin = context.Request.Headers["Origin"];
                if (!LocalOriginValidator.IsAllowed(origin, brokerPort))
                {
                    context.Response.StatusCode = 403;
                    context.Response.Close();
                    return;
                }
            }

            string? body = null;
            if (method == "POST" && context.Request.HasEntityBody)
            {
                // Reject oversize bodies to prevent local DoS.
                var contentLength = context.Request.ContentLength64;
                if (contentLength > MaxRequestBodyBytes)
                {
                    context.Response.StatusCode = 413;
                    context.Response.Close();
                    return;
                }

                body = await ReadBoundedBodyAsync(
                    context.Request.InputStream,
                    contentLength >= 0 ? contentLength : MaxRequestBodyBytes,
                    _lifetimeCts.Token);

                if (body == null)
                {
                    context.Response.StatusCode = 413;
                    context.Response.Close();
                    return;
                }
            }

            var request = new HttpRequestInfo
            {
                Method = method,
                Path = path,
                QueryString = context.Request.Url?.Query ?? string.Empty,
                Body = body
            };
            var (statusCode, contentType, responseBody) = await RouteAsync(request);

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = contentType;
            // No CORS headers: the inspector UI is served same-origin from the broker.
            // Allowing cross-origin would let any web page drive the locally connected app.
            // Anti-framing headers (defense-in-depth against clickjacking): even though
            // the Origin validator already blocks cross-origin API calls, these headers
            // prevent a malicious page from rendering the inspector in an iframe.
            context.Response.Headers.Set("X-Frame-Options", "DENY");
            context.Response.Headers.Set("Content-Security-Policy", "frame-ancestors 'none'");
            context.Response.ContentLength64 = responseBody.Length;
            await context.Response.OutputStream.WriteAsync(responseBody);
            context.Response.Close();
        }
        catch (Exception ex)
        {
            try
            {
                // Log the full exception server-side but return a generic body
                // to avoid leaking internal state (paths, ports, socket error codes)
                // to the browser. RouteAsync's inner catch already does the same.
                Console.Error.WriteLine($"[inspector] broker request failed: {ex}");
                context.Response.StatusCode = 500;
                var msg = Encoding.UTF8.GetBytes("Internal Server Error");
                await context.Response.OutputStream.WriteAsync(msg);
                context.Response.Close();
            }
            catch { }
        }
    }

    /// <summary>
    /// Proxies a WebSocket connection from the broker to the agent's /ws/v1/ui/events endpoint.
    /// </summary>
    private async Task HandleBrokerWebSocketProxy(HttpListenerContext context)
    {
        var wsContext = await context.AcceptWebSocketAsync(null);
        var clientWs = wsContext.WebSocket;

        using var agentWs = new System.Net.WebSockets.ClientWebSocket();
        // The agent's WebSocket route is /ws/v1/ui/events (see DevFlowAgentService route map).
        var agentUri = new Uri($"ws://{_agentHost}:{_agentPort}/ws/v1/ui/events");

        // Tie the proxy lifetime to the inspector so Dispose() unblocks ReceiveAsync.
        // _lifetimeCts is always non-null (broker mode never calls Start()), and is
        // optionally linked to the listener's _cts when running in standalone mode.
        using var linkedCts = _cts != null
            ? CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token, _cts.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var ct = linkedCts.Token;

        try
        {
            await agentWs.ConnectAsync(agentUri, ct);
        }
        catch
        {
            try
            {
                await clientWs.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.EndpointUnavailable,
                    "Agent not reachable", CancellationToken.None);
            }
            catch { }
            return;
        }

        // Send the same subscribe handshake the standalone proxy uses
        // (HandleWebSocketProxy below). The agent only emits events after
        // it has seen a subscribe frame, so without this the broker-hosted
        // relay would silently deliver no events to the browser.
        try
        {
            var subscribe = Encoding.UTF8.GetBytes("{\"type\":\"subscribe\",\"data\":{\"events\":[\"all\"]}}");
            await agentWs.SendAsync(subscribe, System.Net.WebSockets.WebSocketMessageType.Text, true, ct);
        }
        catch
        {
            try { await clientWs.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.InternalServerError, "Subscribe failed", CancellationToken.None); } catch { }
            return;
        }

        // Bidirectional relay. The agent→browser direction is what matters
        // (events flow that way); the browser→agent direction exists purely
        // to observe browser-side close frames so a closed tab unblocks this
        // loop instead of leaking a task until the agent next sends data (or
        // _lifetimeCts is cancelled). Without the monitor task, every closed
        // inspector tab leaves a hanging relay task on the broker.
        try
        {
            var agentToClient = RelayLoopAsync(agentWs, clientWs, ct);
            var clientToAgent = RelayLoopAsync(clientWs, agentWs, ct);
            await Task.WhenAny(agentToClient, clientToAgent);
            // Cancel the linked CTS so the surviving relay task unblocks via
            // cooperative cancellation (OperationCanceledException) before the
            // finally block disposes the sockets out from under it. Without
            // this, the abandoned ReceiveAsync only wakes up with
            // ObjectDisposedException when CloseAsync below tears the socket
            // down — slower, noisier, and the catch {} below would have to
            // swallow that distinct exception type.
            try { linkedCts.Cancel(); } catch { }
        }
        catch { }
        finally
        {
            if (clientWs.State == System.Net.WebSockets.WebSocketState.Open)
                try { await clientWs.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
            if (agentWs.State == System.Net.WebSockets.WebSocketState.Open)
                try { await agentWs.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
        }
    }

    private static async Task RelayLoopAsync(
        System.Net.WebSockets.WebSocket source,
        System.Net.WebSockets.WebSocket destination,
        CancellationToken ct)
    {
        var buffer = new byte[4096];
        try
        {
            while (!ct.IsCancellationRequested &&
                   source.State == System.Net.WebSockets.WebSocketState.Open &&
                   destination.State == System.Net.WebSockets.WebSocketState.Open)
            {
                var result = await source.ReceiveAsync(buffer, ct);
                if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;

                await destination.SendAsync(
                    new ArraySegment<byte>(buffer, 0, result.Count),
                    result.MessageType, result.EndOfMessage, ct);
            }
        }
        catch { }
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        _listenTask = AcceptLoop(_cts.Token);
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();
        if (_listenTask != null)
            await _listenTask.ConfigureAwait(false);
    }

    private int _disposed;

    public void Dispose()
    {
        // Make Dispose idempotent. CancellationTokenSource.Dispose() throws
        // ObjectDisposedException on a second call, and InspectorServer can
        // be disposed from multiple places: the broker eviction path and a
        // direct CLI shutdown. A guard is cheaper than try/catching every
        // member.
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        try { _lifetimeCts.Cancel(); } catch { }
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _cts?.Dispose(); } catch { }
        try { _lifetimeCts.Dispose(); } catch { }
        try { _client.Dispose(); } catch { }
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                var (request, oversized) = await ReadRequestAsync(stream, ct);

                if (oversized)
                {
                    await WriteResponseAsync(stream, 413, "text/plain",
                        Encoding.UTF8.GetBytes("Payload Too Large"), ct);
                    return;
                }

                if (request == null) return;

                // Origin enforcement for standalone-listener mode. Broker mode applies the
                // same check in HandleBrokerRequestAsync; without this, a cross-origin web
                // page could POST to /api/tap, /api/scroll, etc. (CSRF) or open /ws/events
                // (WebSocket hijack) when the inspector runs outside the broker.
                var requestOrigin = request.Headers.TryGetValue("origin", out var o) ? o : null;
                var isWebSocketUpgrade = request.Path == "/ws/events" &&
                    request.Headers.TryGetValue("upgrade", out var upgradeHdr) &&
                    upgradeHdr.Equals("websocket", StringComparison.OrdinalIgnoreCase);
                if ((request.Method == "POST" || isWebSocketUpgrade) &&
                    !LocalOriginValidator.IsAllowed(requestOrigin, _port))
                {
                    await WriteResponseAsync(stream, 403, "text/plain",
                        Encoding.UTF8.GetBytes("Forbidden"), ct);
                    return;
                }

                // Check for WebSocket upgrade on /ws/events
                if (isWebSocketUpgrade)
                {
                    await HandleWebSocketProxy(client, stream, request, ct);
                    return;
                }

                var (statusCode, contentType, body) = await RouteAsync(request);
                await WriteResponseAsync(stream, statusCode, contentType, body, ct);
            }
        }
        catch { }
    }

    private async Task<(int statusCode, string contentType, byte[] body)> RouteAsync(HttpRequestInfo request)
    {
        try
        {
            return request.Method switch
            {
                "GET" => request.Path switch
                {
                    "/" or "" => await HandleRootAsync(),
                    "/api/state" => await HandleStateAsync(),
                    "/screenshot.png" => await HandleScreenshotAsync(request),
                    "/devflow.js" => HandleEmbeddedFile("devflow.js", "application/javascript"),
                    "/devflow.css" => HandleEmbeddedFile("devflow.css", "text/css"),
                    _ => (404, "text/plain", Encoding.UTF8.GetBytes("Not Found"))
                },
                "POST" => request.Path switch
                {
                    "/api/tap" => await HandleProxyTapAsync(request.Body),
                    "/api/scroll" => await HandleProxyScrollAsync(request.Body),
                    "/api/gesture" => await HandleProxyGestureAsync(request.Body),
                    "/api/back" => await HandleProxyBackAsync(),
                    "/api/fill" => await HandleProxyFillAsync(request.Body),
                    "/api/key" => await HandleProxyKeyAsync(request.Body),
                    _ => (404, "text/plain", Encoding.UTF8.GetBytes("Not Found"))
                },
                _ => (405, "text/plain", Encoding.UTF8.GetBytes("Method Not Allowed"))
            };
        }
        catch (Exception ex)
        {
            // Don't leak exception detail (which can include host/port info,
            // file paths, or full stack traces if the message was built by
            // an inner library) to the inspector browser. Log to stderr so
            // an operator can still see what went wrong locally.
            Console.Error.WriteLine($"[inspector] route '{request.Path}' failed: {ex}");
            return (500, "text/plain", Encoding.UTF8.GetBytes("Internal Server Error"));
        }
    }

    private async Task<(int, string, byte[])> HandleRootAsync()
    {
        var tree = await GetTreeWithRetriesAsync();
        if (tree.Count == 0)
        {
            return (
                503,
                "text/plain",
                Encoding.UTF8.GetBytes("Unable to capture the current app tree"));
        }

        // Find the root page element (first child of Window with content).
        // On Mac Catalyst, the default screenshot captures the full screen but element
        // bounds are relative to the page content. By screenshotting the page element
        // directly we get a 1:1 match between pixel coordinates and element bounds.
        var rootPageId = FindRootPageId(tree);
        var fullscreen = HasDetachedNativeRoots(tree);
        var (rootOffsetX, rootOffsetY) = fullscreen
            ? (0d, 0d)
            : GetRootPageOffset(tree, rootPageId);
        var (captureEpoch, registryGeneration, windowId) = GetCaptureMetadata(tree);
        lock (_cacheLock)
        {
            _rootPageId = fullscreen ? null : rootPageId;
            _rootOffsetX = rootOffsetX;
            _rootOffsetY = rootOffsetY;
            _captureEpoch = captureEpoch;
            _registryGeneration = registryGeneration;
            _windowId = windowId;
            _fullscreenScreenshot = fullscreen;
        }
        var screenshot = await GetCachedScreenshotAsync(
            fullscreen ? null : rootPageId,
            captureEpoch,
            registryGeneration,
            fullscreen);
        var screenshotSnapshotId = RememberScreenshot(screenshot);
        var hasScreenshot = screenshot?.Length > 0;

        double viewportWidth = 800, viewportHeight = 600;
        if (hasScreenshot)
        {
            var (pw, ph) = GetPngDimensions(screenshot!);
            viewportWidth = pw;
            viewportHeight = ph;
        }

        var html = HtmlRenderer.Render(
            tree,
            hasScreenshot,
            (int)viewportWidth,
            (int)viewportHeight,
            1,
            1,
            rootOffsetX,
            rootOffsetY,
            screenshotSnapshotId.HasValue
                ? $"screenshot.png?t={screenshotSnapshotId.Value}"
                : null);
        return (200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
    }

    /// <summary>
    /// Returns JSON state for AJAX polling: screenshot (as timestamped URL) + element divs HTML.
    /// This avoids full page reload flash.
    /// </summary>
    private async Task<(int, string, byte[])> HandleStateAsync()
    {
        var tree = await GetTreeWithRetriesAsync();
        if (tree.Count == 0)
        {
            return (
                503,
                "application/json",
                JsonSerializer.SerializeToUtf8Bytes(new
                {
                    error = "Unable to capture the current app tree.",
                    retryable = true
                }));
        }

        var rootPageId = FindRootPageId(tree);
        var fullscreen = HasDetachedNativeRoots(tree);
        var (rootOffsetX, rootOffsetY) = fullscreen
            ? (0d, 0d)
            : GetRootPageOffset(tree, rootPageId);
        var (captureEpoch, registryGeneration, windowId) = GetCaptureMetadata(tree);
        var screenshot = await GetCachedScreenshotAsync(
            fullscreen ? null : rootPageId,
            captureEpoch,
            registryGeneration,
            fullscreen);
        var hasScreenshot = screenshot?.Length > 0;
        if (!hasScreenshot)
        {
            return (
                503,
                "application/json",
                JsonSerializer.SerializeToUtf8Bytes(new
                {
                    error = "Unable to capture the current app state.",
                    retryable = true
                }));
        }

        lock (_cacheLock)
        {
            _rootPageId = fullscreen ? null : rootPageId;
            _rootOffsetX = rootOffsetX;
            _rootOffsetY = rootOffsetY;
            _captureEpoch = captureEpoch;
            _registryGeneration = registryGeneration;
            _windowId = windowId;
            _fullscreenScreenshot = fullscreen;
        }

        var screenshotSnapshotId = RememberScreenshot(screenshot);

        double viewportWidth = 800, viewportHeight = 600;
        if (hasScreenshot)
        {
            var (pw, ph) = GetPngDimensions(screenshot!);
            viewportWidth = pw;
            viewportHeight = ph;
        }

        var elementsHtml = HtmlRenderer.RenderElements(tree, 1, rootOffsetX, rootOffsetY);
        var json = JsonSerializer.Serialize(new
        {
            screenshotUrl = screenshotSnapshotId.HasValue
                ? $"screenshot.png?t={screenshotSnapshotId.Value}"
                : null,
            elements = elementsHtml,
            viewportWidth,
            viewportHeight,
            captureEpoch,
            registryGeneration,
            windowId
        });

        return (200, "application/json", Encoding.UTF8.GetBytes(json));
    }

    private async Task<List<ElementInfo>> GetTreeWithRetriesAsync()
    {
        const int maxAttempts = 3;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var tree = await _client.GetTreeAsync();
            if (tree.Count > 0)
                return tree;
            if (attempt + 1 < maxAttempts)
                await Task.Delay(TimeSpan.FromMilliseconds(25 * (attempt + 1)));
        }

        return [];
    }

    /// <summary>
    /// Finds the ID of the topmost page element in the tree.
    /// When a modal page is showing, it appears as a later child of the Window,
    /// so we take the last child which is the topmost visible page.
    /// </summary>
    private static string? FindRootPageId(List<ElementInfo> tree)
    {
        if (tree.Count == 0) return null;
        var window = tree[0];
        if (window.Children is not { Count: > 0 }) return null;
        // Last child is the topmost (modal pages are added after the shell)
        return window.Children[^1].Id;
    }

    private static bool HasDetachedNativeRoots(List<ElementInfo> tree)
        => tree.Skip(1).Any(HasVisibleNativeBounds);

    private static bool HasVisibleNativeBounds(ElementInfo element)
    {
        var bounds = element.WindowBounds ?? element.Bounds;
        if (element.Origin == "native"
            && element.IsVisible
            && bounds is { Width: > 0, Height: > 0 })
        {
            return true;
        }

        return element.Children?.Any(HasVisibleNativeBounds) == true;
    }

    /// <summary>
    /// Returns the window-absolute offset of the root page element that is being
    /// screenshotted. When the screenshot targets a modal or a page with a safe-area
    /// offset, its WindowBounds.X/Y are non-zero. Overlay positions and hit-test
    /// coordinates must be adjusted by this offset to stay in sync.
    /// </summary>
    private static (double x, double y) GetRootPageOffset(List<ElementInfo> tree, string? rootPageId)
    {
        if (rootPageId == null || tree.Count == 0) return (0, 0);
        var window = tree[0];
        if (window.Children == null) return (0, 0);
        var rootPage = window.Children.FirstOrDefault(c => c.Id == rootPageId);
        if (rootPage == null) return (0, 0);
        var bounds = rootPage.WindowBounds ?? rootPage.Bounds;
        return (bounds?.X ?? 0, bounds?.Y ?? 0);
    }

    private static (long? captureEpoch, long? registryGeneration, int? windowId) GetCaptureMetadata(
        List<ElementInfo> tree)
    {
        var root = tree.FirstOrDefault();
        if (root == null || root.CaptureEpoch <= 0)
            return (null, null, null);

        return (
            root.CaptureEpoch,
            root.RegistryGeneration,
            root.WindowId);
    }

    /// <summary>Reads width/height from PNG IHDR chunk (bytes 16-23) after validating PNG signature.</summary>
    private static (int width, int height) GetPngDimensions(byte[] png)
    {
        // PNG magic: 137 80 78 71 13 10 26 10
        ReadOnlySpan<byte> pngSig = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png.Length < 24 || !png.AsSpan(0, 8).SequenceEqual(pngSig))
            return (0, 0);
        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        // Reject negative dimensions (PNG IHDR width/height are 4-byte big-endian
        // unsigned, so a negative int here means bit 31 was set — invalid per spec)
        // and absurdly large positive dimensions. The inspector feeds these values
        // into CSS sizing, so an attacker-controlled or corrupt PNG could otherwise
        // produce a multi-million-pixel viewport. 32768 is well above any real
        // device resolution and matches common platform texture-size limits.
        const int MaxDimension = 32768;
        if (w <= 0 || h <= 0 || w > MaxDimension || h > MaxDimension) return (0, 0);
        return (w, h);
    }

    private async Task<(int, string, byte[])> HandleScreenshotAsync(HttpRequestInfo request)
    {
        if (!string.IsNullOrEmpty(request.QueryString))
        {
            var remembered = GetRememberedScreenshot(request.QueryString);
            return remembered is { Length: > 0 }
                ? (200, "image/png", remembered)
                : (404, "text/plain", Encoding.UTF8.GetBytes("Screenshot snapshot expired"));
        }

        string? rootPageId;
        long? captureEpoch;
        long? registryGeneration;
        bool fullscreen;
        lock (_cacheLock)
        {
            rootPageId = _rootPageId;
            captureEpoch = _captureEpoch;
            registryGeneration = _registryGeneration;
            fullscreen = _fullscreenScreenshot;
        }
        var png = await GetCachedScreenshotAsync(
            rootPageId,
            captureEpoch,
            registryGeneration,
            fullscreen);
        if (png == null || png.Length == 0)
            return (404, "text/plain", Encoding.UTF8.GetBytes("No screenshot available"));
        return (200, "image/png", png);
    }

    private (int, string, byte[]) HandleEmbeddedFile(string fileName, string contentType)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"Microsoft.Maui.Cli.DevFlow.Inspector.Web.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return (404, "text/plain", Encoding.UTF8.GetBytes($"Resource not found: {resourceName}"));

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return (200, contentType, ms.ToArray());
    }

    private async Task<byte[]?> GetCachedScreenshotAsync(
        string? elementId = null,
        long? captureEpoch = null,
        long? registryGeneration = null,
        bool fullscreen = false)
    {
        lock (_cacheLock)
        {
            // Cache key must include elementId — a cached full-page screenshot
            // is not a valid response for a per-element request and vice versa.
            // Without this check, callers that vary elementId would receive
            // whichever shot happened to be cached first within the 200ms window.
            if (_cachedScreenshot != null
                && string.Equals(_cachedScreenshotElementId, elementId, StringComparison.Ordinal)
                && _cachedScreenshotCaptureEpoch == captureEpoch
                && _cachedScreenshotFullscreen == fullscreen
                && DateTime.UtcNow - _screenshotCacheTime < ScreenshotCacheDuration)
                return _cachedScreenshot;
        }

        var fresh = fullscreen
            ? await _client.FullscreenScreenshotAsync(
                window: null,
                maxWidth: null,
                scale: null,
                captureEpoch: captureEpoch,
                registryGeneration: registryGeneration)
            : await _client.ScreenshotAsync(
                window: null,
                elementId: elementId,
                selector: null,
                maxWidth: null,
                scale: null,
                captureEpoch: captureEpoch,
                registryGeneration: registryGeneration);
        lock (_cacheLock)
        {
            _cachedScreenshot = fresh;
            _cachedScreenshotElementId = elementId;
            _cachedScreenshotCaptureEpoch = captureEpoch;
            _cachedScreenshotFullscreen = fullscreen;
            _screenshotCacheTime = DateTime.UtcNow;
        }
        return fresh;
    }

    // ── Proxy handlers ──

    private async Task<(int, string, byte[])> HandleProxyTapAsync(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Body required\"}"));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Support coordinate-based tap: translate viewport coords to window coords
        // (add root offset back) then hit-test and tap the element
        if (root.TryGetProperty("x", out var xProp) && root.TryGetProperty("y", out var yProp))
        {
            double offsetX, offsetY;
            lock (_cacheLock) { offsetX = _rootOffsetX; offsetY = _rootOffsetY; }
            var x = xProp.GetDouble() + offsetX;
            var y = yProp.GetDouble() + offsetY;

            var attemptedIds = new HashSet<string>(StringComparer.Ordinal);
            var retryCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            ActionResult? terminalOutcome = null;
            var hitOutcome = await _client.HitTestResultAsync(x, y);
            if (!hitOutcome.Success)
                return UiReadOutcomeResponse(hitOutcome);
            var hitResult = hitOutcome.Body;
            for (var attempt = 0; attempt < 32; attempt++)
            {
                if (!TryGetNextHitTestCandidate(
                    hitResult,
                    attemptedIds,
                    preferScrollable: false,
                    out var elementId,
                    out var captureEpoch,
                    out var registryGeneration))
                    break;

                var outcome = await _client.TapResultAsync(
                    elementId!,
                    captureEpoch,
                    registryGeneration);
                if (outcome.Success)
                {
                    InvalidateScreenshotCache();
                    return (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"));
                }

                if (outcome.TransportFailure)
                {
                    terminalOutcome = outcome;
                    break;
                }

                if (outcome.Retryable)
                {
                    var retryCount = retryCounts.GetValueOrDefault(elementId!);
                    if (retryCount >= 1)
                    {
                        terminalOutcome = outcome;
                        break;
                    }

                    retryCounts[elementId!] = retryCount + 1;
                    attemptedIds.Remove(elementId!);
                    await Task.Delay(25);
                }
                else if (outcome.StatusCode is 408 or 409 or 429
                    || outcome.StatusCode >= 500)
                {
                    terminalOutcome = outcome;
                    break;
                }

                hitOutcome = await _client.HitTestResultAsync(x, y);
                if (!hitOutcome.Success)
                    return UiReadOutcomeResponse(hitOutcome);
                hitResult = hitOutcome.Body;
            }
            if (terminalOutcome.HasValue)
                return ActionOutcomeResponse(terminalOutcome.Value);
            return (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"reason\":\"No tappable element at coordinates\"}"));
        }

        // Support elementId-based tap
        if (root.TryGetProperty("elementId", out var elIdProp))
        {
            var elementId = elIdProp.GetString();
            if (!string.IsNullOrEmpty(elementId))
            {
                if (!TryGetActionCaptureMetadata(
                    root,
                    required: await RequiresCaptureEpochAsync(),
                    out var captureEpoch,
                    out var registryGeneration,
                    out var metadataError))
                {
                    return CaptureMetadataError(metadataError!);
                }

                var outcome = await _client.TapResultAsync(
                    elementId,
                    captureEpoch,
                    registryGeneration);
                InvalidateScreenshotCache();
                return ActionOutcomeResponse(outcome);
            }
        }

        return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"x/y or elementId required\"}"));
    }

    private async Task<(int, string, byte[])> HandleProxyScrollAsync(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Body required\"}"));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var deltaX = root.TryGetProperty("deltaX", out var dxProp) ? dxProp.GetDouble() : 0;
        var deltaY = root.TryGetProperty("deltaY", out var dyProp) ? dyProp.GetDouble() : 0;

        // If coordinates provided, translate viewport coords to window coords
        // (add root offset back) then hit-test and try each element for scroll
        if (root.TryGetProperty("x", out var xProp) && root.TryGetProperty("y", out var yProp))
        {
            double offsetX, offsetY;
            lock (_cacheLock) { offsetX = _rootOffsetX; offsetY = _rootOffsetY; }
            var x = xProp.GetDouble() + offsetX;
            var y = yProp.GetDouble() + offsetY;
            var attemptedIds = new HashSet<string>(StringComparer.Ordinal);
            var retryCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            ActionResult? terminalOutcome = null;
            var hitOutcome = await _client.HitTestResultAsync(x, y);
            if (!hitOutcome.Success)
                return UiReadOutcomeResponse(hitOutcome);
            var hitResult = hitOutcome.Body;
            for (var attempt = 0; attempt < 32; attempt++)
            {
                if (!TryGetNextHitTestCandidate(
                    hitResult,
                    attemptedIds,
                    preferScrollable: true,
                    out var elementId,
                    out var captureEpoch,
                    out var registryGeneration))
                    break;

                var outcome = await _client.ScrollResultAsync(
                    elementId: elementId,
                    deltaX: deltaX,
                    deltaY: deltaY,
                    animated: true,
                    window: null,
                    itemIndex: null,
                    groupIndex: null,
                    scrollToPosition: null,
                    captureEpoch: captureEpoch,
                    registryGeneration: registryGeneration);
                if (outcome.Success)
                {
                    InvalidateScreenshotCache();
                    return (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"));
                }

                if (outcome.TransportFailure)
                {
                    terminalOutcome = outcome;
                    break;
                }

                if (outcome.Retryable)
                {
                    var retryCount = retryCounts.GetValueOrDefault(elementId!);
                    if (retryCount >= 1)
                    {
                        terminalOutcome = outcome;
                        break;
                    }

                    retryCounts[elementId!] = retryCount + 1;
                    attemptedIds.Remove(elementId!);
                    await Task.Delay(25);
                }
                else if (outcome.StatusCode is 408 or 409 or 429
                    || outcome.StatusCode >= 500)
                {
                    terminalOutcome = outcome;
                    break;
                }

                hitOutcome = await _client.HitTestResultAsync(x, y);
                if (!hitOutcome.Success)
                    return UiReadOutcomeResponse(hitOutcome);
                hitResult = hitOutcome.Body;
            }
            if (terminalOutcome.HasValue)
                return ActionOutcomeResponse(terminalOutcome.Value);
        }

        // Fallback: scroll without element target
        {
            var outcome = await _client.ScrollResultAsync(
                elementId: null,
                deltaX: deltaX,
                deltaY: deltaY,
                animated: true,
                window: null,
                itemIndex: null,
                groupIndex: null,
                scrollToPosition: null,
                captureEpoch: null,
                registryGeneration: null);
            InvalidateScreenshotCache();
            return ActionOutcomeResponse(outcome);
        }
    }

    private async Task<(int, string, byte[])> HandleProxyGestureAsync(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Body required\"}"));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Determine swipe direction from gesture points
        if (root.TryGetProperty("points", out var pointsArr) && pointsArr.GetArrayLength() >= 2)
        {
            var first = pointsArr[0];
            var last = pointsArr[pointsArr.GetArrayLength() - 1];
            // Guard against malformed input (e.g., {points: [{}, {}]}) so a
            // client error returns 400 rather than bubbling as 500.
            if (!first.TryGetProperty("x", out var fx) || !first.TryGetProperty("y", out var fy) ||
                !last.TryGetProperty("x", out var lx) || !last.TryGetProperty("y", out var ly))
            {
                return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"missing x/y in gesture points\"}"));
            }

            var dx = lx.GetDouble() - fx.GetDouble();
            var dy = ly.GetDouble() - fy.GetDouble();

            var direction = Math.Abs(dx) > Math.Abs(dy)
                ? (dx > 0 ? "right" : "left")
                : (dy > 0 ? "down" : "up");

            var distance = Math.Sqrt(dx * dx + dy * dy);

            var success = await _client.GestureAsync("swipe", direction: direction, distance: distance);
            InvalidateScreenshotCache();
            return success
                ? (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"))
                : (500, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false}"));
        }

        return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"points array required\"}"));
    }

    private async Task<(int, string, byte[])> HandleProxyBackAsync()
    {
        var success = await _client.BackAsync();
        InvalidateScreenshotCache();
        return success
            ? (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"))
            : (500, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false}"));
    }

    private async Task<(int, string, byte[])> HandleProxyFillAsync(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Body required\"}"));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var elementId = root.TryGetProperty("elementId", out var idProp) ? idProp.GetString() : null;
        var text = root.TryGetProperty("text", out var textProp) ? textProp.GetString() : null;

        if (string.IsNullOrEmpty(elementId) || text == null)
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"elementId and text required\"}"));

        if (!TryGetActionCaptureMetadata(
            root,
            required: await RequiresCaptureEpochAsync(),
            out var captureEpoch,
            out var registryGeneration,
            out var metadataError))
        {
            return CaptureMetadataError(metadataError!);
        }

        var outcome = await _client.FillResultAsync(
            elementId,
            text,
            captureEpoch,
            registryGeneration);
        InvalidateScreenshotCache();
        return ActionOutcomeResponse(outcome);
    }

    private async Task<(int, string, byte[])> HandleProxyKeyAsync(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Body required\"}"));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var key = root.TryGetProperty("key", out var keyProp) ? keyProp.GetString() : null;
        var elementId = root.TryGetProperty("elementId", out var idProp) ? idProp.GetString() : null;

        if (string.IsNullOrEmpty(key))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"key required\"}"));

        if (!TryGetActionCaptureMetadata(
            root,
            required: !string.IsNullOrEmpty(elementId) && await RequiresCaptureEpochAsync(),
            out var captureEpoch,
            out var registryGeneration,
            out var metadataError))
        {
            return CaptureMetadataError(metadataError!);
        }

        var outcome = await _client.KeyResultAsync(
            key,
            elementId,
            text: null,
            captureEpoch: captureEpoch,
            registryGeneration: registryGeneration);
        InvalidateScreenshotCache();
        return ActionOutcomeResponse(outcome);
    }

    // ── WebSocket proxy (pass-through to agent /ws/v1/ui/events) ──

    private async Task HandleWebSocketProxy(TcpClient tcpClient, NetworkStream clientStream, HttpRequestInfo request, CancellationToken ct)
    {
        // Complete WebSocket handshake with browser
        if (!request.Headers.TryGetValue("sec-websocket-key", out var wsKey))
            return;

        var acceptKey = Convert.ToBase64String(
            System.Security.Cryptography.SHA1.HashData(
                Encoding.UTF8.GetBytes(wsKey + "258EAFA5-E914-47DA-95CA-5AB5DC4B46D6")));

        var handshake = $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {acceptKey}\r\n\r\n";
        await clientStream.WriteAsync(Encoding.UTF8.GetBytes(handshake), ct);
        await clientStream.FlushAsync(ct);

        // Connect to agent WebSocket and relay messages
        using var agentWs = new System.Net.WebSockets.ClientWebSocket();
        // Per-call CTS used to short-circuit the agent→browser relay when the
        // browser-side TCP stream closes. Without it, a closed browser tab would
        // hang the relay until the agent next sent data (or _cts cancelled).
        using var browserClosedCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, browserClosedCts.Token);
        var linkedCt = linkedCts.Token;
        try
        {
            await agentWs.ConnectAsync(new Uri($"ws://{_agentHost}:{_agentPort}/ws/v1/ui/events"), linkedCt);

            // Subscribe to all events
            var subscribe = Encoding.UTF8.GetBytes("{\"type\":\"subscribe\",\"data\":{\"events\":[\"all\"]}}");
            await agentWs.SendAsync(subscribe, System.Net.WebSockets.WebSocketMessageType.Text, true, linkedCt);

            // Browser→agent monitor: this proxy doesn't forward browser payloads to
            // the agent (the inspector only subscribes), but we still need to know
            // when the browser closes the tab so we can stop draining agent events
            // for nothing. Any read from clientStream — including a Close frame or
            // a 0-byte EOF from a closed TCP socket — signals "browser is gone".
            var monitorTask = Task.Run(async () =>
            {
                var monitorBuf = new byte[256];
                try
                {
                    while (!linkedCt.IsCancellationRequested)
                    {
                        var n = await clientStream.ReadAsync(monitorBuf, linkedCt);
                        if (n <= 0) break;
                        // Any inbound frame here is either a Close or a Ping; the
                        // standalone proxy doesn't process either — fall through to
                        // cancel so the relay tears down cleanly.
                        break;
                    }
                }
                catch { }
                finally
                {
                    try { browserClosedCts.Cancel(); } catch { }
                }
            }, linkedCt);

            // Relay agent messages to browser. Accumulate fragments into a
            // single payload before forwarding so that one logical agent
            // message becomes one WebSocket frame on the wire — otherwise
            // SendWebSocketFrameAsync (which always sets FIN) would split
            // long messages into multiple FIN-bit frames and the browser
            // would see partial JSON. Cap the assembled size so a
            // misbehaving agent (or a huge visual tree) cannot OOM the broker.
            const int MaxAssembledMessageBytes = 4 * 1024 * 1024; // 4 MB
            var buffer = new byte[8192];
            using var assembled = new MemoryStream();
            while (!linkedCt.IsCancellationRequested && agentWs.State == System.Net.WebSockets.WebSocketState.Open)
            {
                System.Net.WebSockets.WebSocketReceiveResult result;
                try { result = await agentWs.ReceiveAsync(buffer, linkedCt); }
                catch { break; }
                if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    break;

                if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Text)
                {
                    assembled.Write(buffer, 0, result.Count);
                    if (assembled.Length > MaxAssembledMessageBytes)
                    {
                        Console.Error.WriteLine($"[inspector] WS message exceeded {MaxAssembledMessageBytes} bytes, closing relay");
                        break;
                    }
                    if (result.EndOfMessage)
                    {
                        var payload = assembled.ToArray();
                        assembled.SetLength(0);
                        await SendWebSocketFrameAsync(clientStream, payload, linkedCt);
                    }
                }
            }
        }
        catch { }
        finally
        {
            try { browserClosedCts.Cancel(); } catch { }
        }
    }

    private static async Task SendWebSocketFrameAsync(NetworkStream stream, byte[] payload, CancellationToken ct)
    {
        // Build a text frame (FIN + opcode 0x1)
        var frame = new List<byte> { 0x81 }; // FIN + text
        if (payload.Length < 126)
            frame.Add((byte)payload.Length);
        else if (payload.Length <= 65535)
        {
            frame.Add(126);
            frame.Add((byte)(payload.Length >> 8));
            frame.Add((byte)(payload.Length & 0xFF));
        }
        else
        {
            frame.Add(127);
            var len = (long)payload.Length;
            for (int i = 7; i >= 0; i--)
                frame.Add((byte)((len >> (i * 8)) & 0xFF));
        }
        frame.AddRange(payload);
        await stream.WriteAsync(frame.ToArray(), ct);
        await stream.FlushAsync(ct);
    }

    // ── HTTP parsing helpers ──

    /// <summary>
    /// Reads a request body from a stream up to <paramref name="maxBytes"/>, decoding as UTF-8.
    /// Returns null if the body exceeds the cap. Decoding once at the end avoids splitting
    /// multi-byte UTF-8 sequences across chunk reads. A per-read timeout prevents slow-drip
    /// clients from holding the handler open.
    /// </summary>
    private static async Task<string?> ReadBoundedBodyAsync(Stream input, long maxBytes, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        long total = 0;
        while (true)
        {
            using var perReadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            perReadCts.CancelAfter(TimeSpan.FromSeconds(10));
            int read;
            try { read = await input.ReadAsync(buffer.AsMemory(), perReadCts.Token); }
            catch { return null; }
            if (read <= 0) break;
            total += read;
            if (total > maxBytes)
                return null;
            ms.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    private static async Task<(HttpRequestInfo? Request, bool Oversized)> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        // Accumulate reads until we find the end-of-headers sentinel (\r\n\r\n)
        // or hit MaxHeaderBytes. A single ReadAsync is not guaranteed to deliver
        // the full headers — TCP can fragment the stream, and a request with many
        // cookies, long Authorization headers, or a slow client / proxy can split
        // headers across multiple segments. Dropping such requests silently would
        // make legitimate browsers intermittently fail with no diagnostic.
        const int MaxHeaderBytes = 64 * 1024;
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

        int headerEnd = -1;
        while (headerEnd < 0)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer, timeoutCts.Token);
            }
            catch { return (null, false); }
            if (read == 0) return (null, false);

            ms.Write(buffer, 0, read);
            if (ms.Length > MaxHeaderBytes)
                return (null, true);

            // Re-scan the accumulated bytes (ASCII portion only) for the end of headers.
            // Headers are ASCII per RFC 7230 §3.2.4, so ASCII decoding is correct and
            // avoids splitting multi-byte UTF-8 sequences that may appear in the body.
            var soFar = Encoding.ASCII.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            headerEnd = soFar.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        }

        var raw = Encoding.ASCII.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        var headerSection = raw[..headerEnd];
        int read_total = (int)ms.Length;

        var lines = headerSection.Split("\r\n");
        if (lines.Length == 0) return (null, false);

        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2) return (null, false);

        var method = requestLine[0].ToUpperInvariant();
        var requestTarget = requestLine[1];
        var queryIndex = requestTarget.IndexOf('?');
        var queryString = queryIndex >= 0 ? requestTarget[queryIndex..] : string.Empty;
        var path = (queryIndex >= 0 ? requestTarget[..queryIndex] : requestTarget).TrimEnd('/');
        if (string.IsNullOrEmpty(path)) path = "/";

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            var colonIdx = lines[i].IndexOf(':');
            if (colonIdx > 0)
            {
                var key = lines[i][..colonIdx].Trim();
                var value = lines[i][(colonIdx + 1)..].Trim();
                headers[key] = value;
            }
        }

        // Read body as raw bytes, then decode as UTF-8 once.
        string? body = null;
        if (headers.TryGetValue("content-length", out var clStr) && int.TryParse(clStr, out var contentLength) && contentLength > 0)
        {
            if (contentLength > MaxRequestBodyBytes)
                return (null, true);

            var bodyStart = headerEnd + 4;
            var bytesAlreadyRead = read_total - bodyStart;
            var bodyBytes = new byte[contentLength];

            if (bytesAlreadyRead > 0)
            {
                var copy = Math.Min(bytesAlreadyRead, contentLength);
                Buffer.BlockCopy(ms.GetBuffer(), bodyStart, bodyBytes, 0, copy);
            }

            int totalBodyRead = Math.Min(Math.Max(0, bytesAlreadyRead), contentLength);
            while (totalBodyRead < contentLength)
            {
                using var perReadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                perReadCts.CancelAfter(TimeSpan.FromSeconds(10));
                int extra;
                try { extra = await stream.ReadAsync(bodyBytes.AsMemory(totalBodyRead, contentLength - totalBodyRead), perReadCts.Token); }
                catch { return (null, false); }
                if (extra == 0) break;
                totalBodyRead += extra;
            }
            body = Encoding.UTF8.GetString(bodyBytes, 0, totalBodyRead);
        }

        return (new HttpRequestInfo
        {
            Method = method,
            Path = path,
            QueryString = queryString,
            Headers = headers,
            Body = body
        }, false);
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int statusCode, string contentType, byte[] body, CancellationToken ct)
    {
        var statusText = statusCode switch
        {
            200 => "OK",
            400 => "Bad Request",
            404 => "Not Found",
            405 => "Method Not Allowed",
            409 => "Conflict",
            413 => "Payload Too Large",
            500 => "Internal Server Error",
            502 => "Bad Gateway",
            503 => "Service Unavailable",
            _ => "Unknown"
        };

        // No CORS headers: the inspector UI is served same-origin; allowing
        // cross-origin would let any web page drive the locally connected app.
        var header = $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                     $"Content-Type: {contentType}\r\n" +
                     $"Content-Length: {body.Length}\r\n" +
                     "Connection: close\r\n\r\n";

        await stream.WriteAsync(Encoding.UTF8.GetBytes(header), ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    internal sealed class HttpRequestInfo
    {
        public string Method { get; init; } = "";
        public string Path { get; init; } = "";
        public string QueryString { get; init; } = "";
        public Dictionary<string, string> Headers { get; init; } = new();
        public string? Body { get; init; }
    }
}
