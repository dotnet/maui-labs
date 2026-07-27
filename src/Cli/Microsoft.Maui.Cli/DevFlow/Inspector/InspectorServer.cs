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
    private readonly string? _policyStartPath;
    private LayoutDiagnosticsPolicy _layoutDiagnosticsPolicy;
    private LayoutDiagnosticsPolicy _projectLayoutDiagnosticsPolicy;
    private LayoutInspectionResult? _latestLayoutDiagnostics;
    private DateTime _latestLayoutDiagnosticsAt;
    private readonly SemaphoreSlim _diagnosticsDeltaGate = new(1, 1);
    private readonly object _cacheLock = new();
    // Lifetime cancellation source; cancelled in Dispose() so broker-mode WS proxies
    // (which never call Start() to create _cts) still see shutdown.
    private readonly CancellationTokenSource _lifetimeCts = new();
    private byte[]? _cachedScreenshot;
    private string? _cachedScreenshotElementId;
    private DateTime _screenshotCacheTime;
    private readonly Dictionary<string, (byte[] Bytes, DateTime CreatedAt)>
        _screenshotFrames = new(StringComparer.Ordinal);
    private string? _rootPageId;
    // The window-absolute offset of the screenshotted root page element.
    // Used to translate between viewport coordinates (relative to the screenshot)
    // and window coordinates (used by the agent's hit-test/tap/scroll APIs).
    private double _rootOffsetX;
    private double _rootOffsetY;
    private static readonly TimeSpan ScreenshotCacheDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan LayoutDiagnosticsCacheDuration =
        TimeSpan.FromMilliseconds(250);

    // Cap request bodies to avoid local DoS via huge POST payloads.
    private const long MaxRequestBodyBytes = 1_048_576; // 1 MB

    public int Port => _port;

    /// <summary>
    /// Port of the underlying DevFlow agent this inspector is proxying to. Used by
    /// the broker to detect when an agent has reconnected on a different port and
    /// the cached InspectorServer's AgentClient is now pointing at a dead port.
    /// </summary>
    public int AgentPort => _agentPort;

    public InspectorServer(
        int port,
        string agentHost,
        int agentPort,
        string? policyStartPath = null)
    {
        _port = port;
        _agentHost = agentHost;
        _agentPort = agentPort;
        _client = new AgentClient(agentHost, agentPort);
        _policyStartPath = policyStartPath;
        _layoutDiagnosticsPolicy = LayoutDiagnosticsPolicyLoader.Load(policyStartPath);
        _projectLayoutDiagnosticsPolicy =
            LayoutDiagnosticsPolicyLoader.LoadProjectPolicy(policyStartPath);
    }

    private void InvalidateScreenshotCache()
    {
        lock (_cacheLock)
        {
            _cachedScreenshot = null;
        }
    }

    /// <summary>
    /// Safely extract the "elements" array from a hit-test response. Returns false if
    /// the agent returned malformed JSON, missing the property, or wrong shape — in which
    /// case the inspector should fall back to a "no element here" path instead of crashing
    /// and leaking the exception text to the browser.
    /// </summary>
    private static bool TryParseHitTestElements(string? hitResult, out JsonDocument? doc, out JsonElement elements)
    {
        doc = null;
        elements = default;
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
            return true;
        }
        catch (JsonException)
        {
            doc?.Dispose();
            doc = null;
            return false;
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

            var request = new HttpRequestInfo { Method = method, Path = path, Body = body };
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
            var agentToClient = RelayAgentEventsWithDiagnosticsAsync(agentWs, clientWs, ct);
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

    private async Task RelayAgentEventsWithDiagnosticsAsync(
        System.Net.WebSockets.WebSocket source,
        System.Net.WebSockets.WebSocket destination,
        CancellationToken cancellationToken)
    {
        const int MaxAssembledMessageBytes = 4 * 1024 * 1024;
        var buffer = new byte[8192];
        using var assembled = new MemoryStream();
        var assembledMessageType =
            System.Net.WebSockets.WebSocketMessageType.Text;
        using var sendGate = new SemaphoreSlim(1, 1);
        await using var diagnosticsDebouncer = new DiagnosticsDeltaDebouncer(
            CaptureLayoutDiagnosticsDeltaAsync,
            async (delta, ct) =>
            {
                var payload = JsonSerializer.SerializeToUtf8Bytes(
                    delta,
                    DevFlowCliJsonContext.Default.LayoutDiagnosticsDelta);
                await SendWebSocketMessageAsync(
                    destination,
                    sendGate,
                    new ArraySegment<byte>(payload),
                    System.Net.WebSockets.WebSocketMessageType.Text,
                    endOfMessage: true,
                    ct);
            },
            cancellationToken);
        try
        {
            while (!cancellationToken.IsCancellationRequested
                && source.State == System.Net.WebSockets.WebSocketState.Open
                && destination.State == System.Net.WebSockets.WebSocketState.Open)
            {
                var result = await source.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    break;

                if (assembled.Length == 0)
                    assembledMessageType = result.MessageType;
                assembled.Write(buffer, 0, result.Count);
                if (assembled.Length > MaxAssembledMessageBytes)
                    break;
                if (!result.EndOfMessage)
                    continue;
                var payload = assembled.ToArray();
                assembled.SetLength(0);
                await SendWebSocketMessageAsync(
                    destination,
                    sendGate,
                    new ArraySegment<byte>(payload),
                    assembledMessageType,
                    endOfMessage: true,
                    cancellationToken);

                if (assembledMessageType
                    == System.Net.WebSockets.WebSocketMessageType.Text)
                {
                    diagnosticsDebouncer.Signal();
                }
            }
        }
        catch
        {
        }
    }

    private static async Task SendWebSocketMessageAsync(
        System.Net.WebSockets.WebSocket destination,
        SemaphoreSlim sendGate,
        ArraySegment<byte> payload,
        System.Net.WebSockets.WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken)
    {
        await sendGate.WaitAsync(cancellationToken);
        try
        {
            await destination.SendAsync(
                payload,
                messageType,
                endOfMessage,
                cancellationToken);
        }
        finally
        {
            sendGate.Release();
        }
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
                    "/screenshot.png" => await HandleScreenshotAsync(),
                    var framePath when framePath.StartsWith(
                        "/screenshot/",
                        StringComparison.Ordinal) =>
                        HandleScreenshotFrame(framePath),
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
                    "/api/diagnostics/suppress" => HandleDiagnosticSuppression(request.Body, remove: false),
                    "/api/diagnostics/unsuppress" => HandleDiagnosticSuppression(request.Body, remove: true),
                    "/api/diagnostics/agent-payload" => HandleDiagnosticAgentPayload(request.Body),
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
        var tree = await _client.GetTreeAsync(
            maxDepth: 0,
            window: null,
            includeNative: false);

        // Find the root page element (first child of Window with content).
        // On Mac Catalyst, the default screenshot captures the full screen but element
        // bounds are relative to the page content. By screenshotting the page element
        // directly we get a 1:1 match between pixel coordinates and element bounds.
        var rootPageId = FindRootPageId(tree);
        var (rootOffsetX, rootOffsetY) = GetRootPageOffset(tree, rootPageId);
        lock (_cacheLock)
        {
            _rootPageId = rootPageId;
            _rootOffsetX = rootOffsetX;
            _rootOffsetY = rootOffsetY;
        }
        var screenshot = await GetCachedScreenshotAsync(rootPageId);
        var hasScreenshot = screenshot?.Length > 0;

        double viewportWidth = 800, viewportHeight = 600;
        if (hasScreenshot)
        {
            var (pw, ph) = GetPngDimensions(screenshot!);
            viewportWidth = pw;
            viewportHeight = ph;
        }

        var html = HtmlRenderer.Render(tree, hasScreenshot, (int)viewportWidth, (int)viewportHeight, 1, 1, rootOffsetX, rootOffsetY);
        return (200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
    }

    /// <summary>
    /// Returns JSON state for AJAX polling: screenshot (as timestamped URL) + element divs HTML.
    /// This avoids full page reload flash.
    /// </summary>
    private async Task<(int, string, byte[])> HandleStateAsync()
    {
        List<ElementInfo> tree = [];
        var treeRevision = string.Empty;
        LayoutInspectionResult? diagnostics = null;
        string? rootPageId = null;
        var rootOffsetX = 0d;
        var rootOffsetY = 0d;
        byte[]? screenshot = null;
        var coherentFrame = false;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var treeSnapshot = await _client.GetTreeSnapshotAsync(
                includeNative: false);
            tree = treeSnapshot?.Elements ?? [];
            treeRevision = treeSnapshot?.Revision ?? string.Empty;
            lock (_cacheLock)
            {
                diagnostics =
                    DateTime.UtcNow - _latestLayoutDiagnosticsAt
                        < LayoutDiagnosticsCacheDuration
                    && _latestLayoutDiagnostics?.Snapshot.TreeRevision
                        == treeRevision
                        ? _latestLayoutDiagnostics
                        : null;
            }
            diagnostics ??= await CaptureLayoutDiagnosticsAsync("wait");
            if (diagnostics is not null
                && !string.Equals(
                    treeRevision,
                    diagnostics.Snapshot.TreeRevision,
                    StringComparison.Ordinal))
            {
                InvalidateScreenshotCache();
                continue;
            }

            rootPageId = FindRootPageId(tree);
            (rootOffsetX, rootOffsetY) =
                GetRootPageOffset(tree, rootPageId);
            lock (_cacheLock)
            {
                _rootPageId = rootPageId;
                _rootOffsetX = rootOffsetX;
                _rootOffsetY = rootOffsetY;
            }

            InvalidateScreenshotCache();
            screenshot = await GetCachedScreenshotAsync(rootPageId);
            if (string.IsNullOrEmpty(treeRevision))
            {
                coherentFrame = true;
                break;
            }

            var postScreenshotTree =
                await _client.GetTreeSnapshotAsync(includeNative: false);
            if (string.Equals(
                treeRevision,
                postScreenshotTree?.Revision,
                StringComparison.Ordinal))
            {
                coherentFrame = true;
                break;
            }
        }

        if (!coherentFrame)
            diagnostics = null;
        var hasScreenshot = screenshot?.Length > 0;

        double viewportWidth = 800, viewportHeight = 600;
        if (hasScreenshot)
        {
            var (pw, ph) = GetPngDimensions(screenshot!);
            viewportWidth = pw;
            viewportHeight = ph;
        }

        var elementsHtml = HtmlRenderer.RenderElements(tree, 1, rootOffsetX, rootOffsetY);
        var screenshotUrl = "screenshot.png";
        lock (_cacheLock)
        {
            _latestLayoutDiagnostics = diagnostics;
            _latestLayoutDiagnosticsAt = DateTime.UtcNow;
            if (hasScreenshot)
            {
                var now = DateTime.UtcNow;
                foreach (var expired in _screenshotFrames
                    .Where(frame => now - frame.Value.CreatedAt
                        > TimeSpan.FromSeconds(10))
                    .Select(frame => frame.Key)
                    .ToList())
                {
                    _screenshotFrames.Remove(expired);
                }
                while (_screenshotFrames.Count >= 8)
                {
                    var oldest = _screenshotFrames.MinBy(
                        frame => frame.Value.CreatedAt).Key;
                    _screenshotFrames.Remove(oldest);
                }

                var frameId = Guid.NewGuid().ToString("N");
                _screenshotFrames[frameId] = (screenshot!, now);
                screenshotUrl = $"screenshot/{frameId}.png";
            }
        }

        var json = JsonSerializer.Serialize(new
        {
            screenshotUrl,
            elements = elementsHtml,
            viewportWidth,
            viewportHeight,
            rootOffsetX,
            rootOffsetY,
            diagnostics
        });

        return (200, "application/json", Encoding.UTF8.GetBytes(json));
    }

    private async Task<LayoutInspectionResult?> CaptureLayoutDiagnosticsAsync(string stabilityMode)
    {
        try
        {
            return await _client.AnalyzeLayoutAsync(new LayoutInspectionRequest
            {
                Profile = "agent",
                MinimumSeverity = "info",
                IncludeEvidence = true,
                Scope = new LayoutInspectionScope { IncludeNativeElements = false },
                Suppressions = GetLayoutSuppressions(),
                Stability = new LayoutStabilityOptions
                {
                    Mode = stabilityMode,
                    StableFrames = 2,
                    QuietPeriodMs = 50,
                    TimeoutMs = 1000
                }
            });
        }
        catch (LayoutDiagnosticsException ex) when (ex.Retryable)
        {
            lock (_cacheLock)
                return _latestLayoutDiagnostics;
        }
    }

    private async Task<LayoutDiagnosticsDelta?> CaptureLayoutDiagnosticsDeltaAsync()
    {
        if (!await _diagnosticsDeltaGate.WaitAsync(0))
            return null;
        try
        {
            var current = await CaptureLayoutDiagnosticsAsync("immediate");
            if (current is null)
                return null;

            LayoutInspectionResult? previous;
            lock (_cacheLock)
            {
                previous = _latestLayoutDiagnostics;
                _latestLayoutDiagnostics = current;
                _latestLayoutDiagnosticsAt = DateTime.UtcNow;
            }
            return LayoutDiagnosticsDeltaBuilder.Build(previous, current);
        }
        finally
        {
            _diagnosticsDeltaGate.Release();
        }
    }

    private sealed class DiagnosticsDeltaDebouncer : IAsyncDisposable
    {
        private static readonly TimeSpan DebounceDelay =
            TimeSpan.FromMilliseconds(100);
        private readonly System.Threading.Channels.Channel<byte> _signals =
            System.Threading.Channels.Channel.CreateBounded<byte>(
                new System.Threading.Channels.BoundedChannelOptions(1)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.DropWrite
                });
        private readonly Func<Task<LayoutDiagnosticsDelta?>> _capture;
        private readonly Func<LayoutDiagnosticsDelta, CancellationToken, Task> _send;
        private readonly CancellationToken _cancellationToken;
        private readonly Task _worker;

        public DiagnosticsDeltaDebouncer(
            Func<Task<LayoutDiagnosticsDelta?>> capture,
            Func<LayoutDiagnosticsDelta, CancellationToken, Task> send,
            CancellationToken cancellationToken)
        {
            _capture = capture;
            _send = send;
            _cancellationToken = cancellationToken;
            _worker = RunAsync();
        }

        public void Signal() => _signals.Writer.TryWrite(0);

        public async ValueTask DisposeAsync()
        {
            _signals.Writer.TryComplete();
            try
            {
                await _worker;
            }
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
            {
            }
        }

        private async Task RunAsync()
        {
            while (await _signals.Reader.WaitToReadAsync(_cancellationToken))
            {
                while (_signals.Reader.TryRead(out _))
                {
                }

                await Task.Delay(DebounceDelay, _cancellationToken);
                while (_signals.Reader.TryRead(out _))
                {
                }

                var delta = await _capture();
                if (delta is not null)
                    await _send(delta, _cancellationToken);
            }
        }
    }

    private List<LayoutSuppression> GetLayoutSuppressions()
    {
        lock (_cacheLock)
            return _layoutDiagnosticsPolicy.Suppressions.ToList();
    }

    private (int, string, byte[]) HandleDiagnosticSuppression(
        string? body,
        bool remove)
    {
        var request = JsonSerializer.Deserialize<InspectorDiagnosticRequest>(
            body ?? "{}",
            DevFlowCliJsonContext.Default.InspectorDiagnosticRequest);
        if (string.IsNullOrWhiteSpace(request?.FindingId))
            return (400, "text/plain", Encoding.UTF8.GetBytes("findingId is required"));

        LayoutFinding? finding;
        lock (_cacheLock)
        {
            finding = _latestLayoutDiagnostics?.Findings.FirstOrDefault(candidate =>
                candidate.Id.Equals(request.FindingId, StringComparison.OrdinalIgnoreCase));
        }

        if (finding is null)
            return (404, "text/plain", Encoding.UTF8.GetBytes("Finding not found"));
        var suppressionKey = string.IsNullOrWhiteSpace(finding.SuppressionKey)
            ? finding.Id
            : finding.SuppressionKey;

        LayoutDiagnosticsPolicy updatedProjectPolicy;
        if (remove)
        {
            var userMatches = LayoutDiagnosticsPolicyLoader.LoadUserPolicy()
                .Suppressions
                .Where(suppression =>
                    LayoutDiagnosticsSuppressionMatcher.Matches(suppression, finding))
                .ToList();
            try
            {
                updatedProjectPolicy = LayoutDiagnosticsPolicyLoader.UpdateProjectPolicy(
                    _policyStartPath,
                    projectPolicy =>
                    {
                        var exactProjectMatches = projectPolicy.Suppressions
                            .Where(suppression =>
                                suppression.Fingerprint?.Equals(
                                    suppressionKey,
                                    StringComparison.OrdinalIgnoreCase) == true)
                            .ToList();
                        var broadProjectMatches = projectPolicy.Suppressions
                            .Where(suppression =>
                                suppression.Fingerprint?.Equals(
                                    suppressionKey,
                                    StringComparison.OrdinalIgnoreCase) != true
                                && LayoutDiagnosticsSuppressionMatcher.Matches(
                                    suppression,
                                    finding))
                            .ToList();

                        if (exactProjectMatches.Count == 0
                            || broadProjectMatches.Count > 0
                            || userMatches.Count > 0)
                        {
                            var provenance = new List<string>();
                            if (exactProjectMatches.Count > 0)
                                provenance.Add("project-exact");
                            if (broadProjectMatches.Count > 0)
                                provenance.Add("project-broad");
                            if (userMatches.Count > 0)
                                provenance.Add("user");
                            throw new LayoutSuppressionConflictException(provenance);
                        }

                        projectPolicy.Suppressions.RemoveAll(suppression =>
                            suppression.Fingerprint?.Equals(
                                suppressionKey,
                                StringComparison.OrdinalIgnoreCase) == true);
                    });
            }
            catch (LayoutSuppressionConflictException ex)
            {
                return JsonResponse(
                    409,
                    new
                    {
                        success = false,
                        findingId = request.FindingId,
                        suppressed = true,
                        projectRemovable = false,
                        provenance = ex.Provenance,
                        message = ex.Provenance.Count == 0
                            ? "No project-owned exact suppression exists for this finding."
                            : "This finding is also suppressed by a user or broad project policy. Edit that policy to unsuppress it."
                    });
            }
        }
        else
        {
            updatedProjectPolicy = LayoutDiagnosticsPolicyLoader.UpdateProjectPolicy(
                _policyStartPath,
                projectPolicy =>
                {
                    if (projectPolicy.Suppressions.Any(suppression =>
                        suppression.Fingerprint?.Equals(
                            suppressionKey,
                            StringComparison.OrdinalIgnoreCase) == true))
                    {
                        return;
                    }

                    projectPolicy.Suppressions.Add(new LayoutSuppression
                    {
                        Fingerprint = suppressionKey,
                        Reason = string.IsNullOrWhiteSpace(request.Reason)
                            ? "Suppressed in DevFlow Inspector"
                            : request.Reason
                    });
                });
        }

        var updatedCombinedPolicy =
            LayoutDiagnosticsPolicyLoader.Load(_policyStartPath);
        lock (_cacheLock)
        {
            _projectLayoutDiagnosticsPolicy = updatedProjectPolicy;
            _layoutDiagnosticsPolicy =
                updatedCombinedPolicy;
            _latestLayoutDiagnostics = null;
            _latestLayoutDiagnosticsAt = default;
        }

        return JsonResponse(
            200,
            new
            {
                success = true,
                findingId = request.FindingId,
                suppressed = !remove,
                projectRemovable = !remove,
                provenance = remove ? Array.Empty<string>() : ["project-exact"]
            });
    }

    private static (int, string, byte[]) JsonResponse(int statusCode, object value)
    {
        var json = JsonSerializer.Serialize(value);
        return (statusCode, "application/json", Encoding.UTF8.GetBytes(json));
    }

    private sealed class LayoutSuppressionConflictException(
        IReadOnlyList<string> provenance) : Exception
    {
        public IReadOnlyList<string> Provenance { get; } = provenance;
    }

    private (int, string, byte[]) HandleDiagnosticAgentPayload(string? body)
    {
        var request = JsonSerializer.Deserialize<InspectorDiagnosticRequest>(
            body ?? "{}",
            DevFlowCliJsonContext.Default.InspectorDiagnosticRequest);
        if (string.IsNullOrWhiteSpace(request?.FindingId))
            return (400, "text/plain", Encoding.UTF8.GetBytes("findingId is required"));

        LayoutFinding? finding;
        lock (_cacheLock)
        {
            finding = _latestLayoutDiagnostics?.Findings.FirstOrDefault(candidate =>
                candidate.Id.Equals(request.FindingId, StringComparison.OrdinalIgnoreCase));
        }
        if (finding is null)
            return (404, "text/plain", Encoding.UTF8.GetBytes("Finding not found"));

        var json = JsonSerializer.Serialize(
            finding,
            DevFlowCliJsonContext.Default.LayoutFinding);
        return (200, "application/json", Encoding.UTF8.GetBytes(json));
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

    private async Task<(int, string, byte[])> HandleScreenshotAsync()
    {
        string? rootPageId;
        lock (_cacheLock) { rootPageId = _rootPageId; }
        var png = await GetCachedScreenshotAsync(rootPageId);
        if (png == null || png.Length == 0)
            return (404, "text/plain", Encoding.UTF8.GetBytes("No screenshot available"));
        return (200, "image/png", png);
    }

    private (int, string, byte[]) HandleScreenshotFrame(string path)
    {
        var fileName = path["/screenshot/".Length..];
        if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return (404, "text/plain", Encoding.UTF8.GetBytes("Not Found"));
        var frameId = fileName[..^".png".Length];
        lock (_cacheLock)
        {
            if (_screenshotFrames.TryGetValue(frameId, out var frame))
                return (200, "image/png", frame.Bytes);
        }
        return (404, "text/plain", Encoding.UTF8.GetBytes("Screenshot frame expired"));
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

    private async Task<byte[]?> GetCachedScreenshotAsync(string? elementId = null)
    {
        lock (_cacheLock)
        {
            // Cache key must include elementId — a cached full-page screenshot
            // is not a valid response for a per-element request and vice versa.
            // Without this check, callers that vary elementId would receive
            // whichever shot happened to be cached first within the 200ms window.
            if (_cachedScreenshot != null
                && string.Equals(_cachedScreenshotElementId, elementId, StringComparison.Ordinal)
                && DateTime.UtcNow - _screenshotCacheTime < ScreenshotCacheDuration)
                return _cachedScreenshot;
        }

        var fresh = await _client.ScreenshotAsync(elementId: elementId);
        lock (_cacheLock)
        {
            _cachedScreenshot = fresh;
            _cachedScreenshotElementId = elementId;
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

            var hitResult = await _client.HitTestAsync(x, y);

            // Parse hit-test result — response is { elements: [{ id, ... }, ...] }
            // The agent may return malformed JSON or omit "elements" if it
            // encountered an internal error; treat that as "no element here"
            // rather than leaking the JsonException text to the browser.
            if (TryParseHitTestElements(hitResult, out var hitDoc, out var elements))
            {
                using (hitDoc)
                {
                    if (elements.GetArrayLength() > 0)
                    {
                        // Try elements from most specific to most general until one accepts tap
                        for (int i = 0; i < elements.GetArrayLength(); i++)
                        {
                            if (!elements[i].TryGetProperty("id", out var idProp)) continue;
                            var elementId = idProp.GetString();
                            if (!string.IsNullOrEmpty(elementId))
                            {
                                var success = await _client.TapAsync(elementId);
                                if (success)
                                {
                                    InvalidateScreenshotCache();
                                    return (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"));
                                }
                            }
                        }
                    }
                }
            }
            return (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"reason\":\"No tappable element at coordinates\"}"));
        }

        // Support elementId-based tap
        if (root.TryGetProperty("elementId", out var elIdProp))
        {
            var elementId = elIdProp.GetString();
            if (!string.IsNullOrEmpty(elementId))
            {
                var success = await _client.TapAsync(elementId);
                InvalidateScreenshotCache();
                return success
                    ? (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"))
                    : (500, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false}"));
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
            var hitResult = await _client.HitTestAsync(xProp.GetDouble() + offsetX, yProp.GetDouble() + offsetY);
            if (TryParseHitTestElements(hitResult, out var hitDoc, out var elements))
            {
                using (hitDoc)
                {
                    // Try each element from most specific to general until one accepts scroll
                    for (int i = 0; i < elements.GetArrayLength(); i++)
                    {
                        if (!elements[i].TryGetProperty("id", out var idProp)) continue;
                        var elementId = idProp.GetString();
                        if (!string.IsNullOrEmpty(elementId))
                        {
                            var success = await _client.ScrollAsync(elementId: elementId, deltaX: deltaX, deltaY: deltaY);
                            if (success)
                            {
                                InvalidateScreenshotCache();
                                return (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"));
                            }
                        }
                    }
                }
            }
        }

        // Fallback: scroll without element target
        {
            var success = await _client.ScrollAsync(deltaX: deltaX, deltaY: deltaY);
            InvalidateScreenshotCache();
            return success
                ? (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"))
                : (500, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false}"));
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

        var success = await _client.FillAsync(elementId, text);
        InvalidateScreenshotCache();
        return success
            ? (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"))
            : (500, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false}"));
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

        var success = await _client.KeyAsync(key, elementId);
        InvalidateScreenshotCache();
        return success
            ? (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"))
            : (500, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false}"));
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
            using var sendGate = new SemaphoreSlim(1, 1);
            await using var diagnosticsDebouncer = new DiagnosticsDeltaDebouncer(
                CaptureLayoutDiagnosticsDeltaAsync,
                async (delta, cancellationToken) =>
                {
                    var deltaPayload = JsonSerializer.SerializeToUtf8Bytes(
                        delta,
                        DevFlowCliJsonContext.Default.LayoutDiagnosticsDelta);
                    await SendWebSocketFrameAsync(
                        clientStream,
                        sendGate,
                        deltaPayload,
                        cancellationToken);
                },
                linkedCt);
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
                        await SendWebSocketFrameAsync(
                            clientStream,
                            sendGate,
                            payload,
                            linkedCt);
                        diagnosticsDebouncer.Signal();
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

    private static async Task SendWebSocketFrameAsync(
        NetworkStream stream,
        SemaphoreSlim sendGate,
        byte[] payload,
        CancellationToken ct)
    {
        await sendGate.WaitAsync(ct);
        try
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
        finally
        {
            sendGate.Release();
        }
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
        var path = requestLine[1].Split('?')[0].TrimEnd('/');
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
            413 => "Payload Too Large",
            500 => "Internal Server Error",
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

    internal sealed class InspectorDiagnosticRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("findingId")]
        public string? FindingId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }

    internal sealed class HttpRequestInfo
    {
        public string Method { get; init; } = "";
        public string Path { get; init; } = "";
        public Dictionary<string, string> Headers { get; init; } = new();
        public string? Body { get; init; }
    }
}
