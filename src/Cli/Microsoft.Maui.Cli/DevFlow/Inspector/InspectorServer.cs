using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private readonly string? _embedToken;
    private readonly string? _agentId;
    private readonly string? _appName;
    private readonly string? _platform;
    private readonly string? _project;
    private readonly string? _sessionId;
    private readonly XamlSourcePropertyEditor _sourcePropertyEditor;
    private readonly InspectorAlertController _alertController;
    // Per-inspector read token gating the data tabs (Logs/Network/Preferences/Device/Sensors/
    // Files) — the app data these expose (tokens in network, secrets in prefs/logs) exceeds the
    // visible tree. Injected into the served page as a <meta>; devflow.js echoes it back in the
    // X-DevFlow-Inspector-Token header. Same-origin only (a cross-origin fetch can't set a custom
    // header without a preflight the broker never answers), and a no-Origin local client (curl) can't
    // read it from the page. Redaction (below) is the primary defense; this is defense-in-depth.
    private readonly string _readToken = Guid.NewGuid().ToString("N");

    // Browser tabs supply their own lease identity. Requests without one (standalone tests and local
    // non-browser callers) share this inspector-instance identity.
    private readonly string _fallbackMutationLeaseId = Guid.NewGuid().ToString("N");
    // 0 = idle, 1 = a flow replay is driving the app. Guards against interleaving a replay with
    // user mutations / recording (a replay can be triggered from any embedding host or tab).
    private int _replayInProgress;
    private readonly SemaphoreSlim _flowAdmissionGate = new(1, 1);
    private readonly SemaphoreSlim _frameCreationGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PersistTransactionGates =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
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
    private long? _cachedScreenshotCaptureEpoch;
    private bool _cachedScreenshotFullscreen;
    private DateTime _screenshotCacheTime;
    private InspectorFrame? _latestFrame;
    private long _cacheGeneration;
    private readonly Dictionary<string, InspectorFrame> _frames = new(StringComparer.Ordinal);
    // Last observed capture epoch, used only to decide whether this agent enforces capture-bound
    // mutations. Per-frame capture metadata lives on InspectorFrame, which is the source of truth
    // for the coordinates, offsets and epochs a browser tab acted against — it supersedes the
    // standalone screenshot-frame + root-offset fields diagnostics originally hung off.
    private long? _captureEpoch;
    private Task<bool?>? _requiresCaptureEpochTask;
    private static readonly TimeSpan ScreenshotCacheDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan FrameReuseDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan FrameCacheDuration = TimeSpan.FromSeconds(10);
    private const int MaxCachedFrames = 32;
    private const int MaxScreenshotBytes = 64 * 1024 * 1024;
    private const long MaxCachedFrameBytes = 64L * 1024 * 1024;
    private static readonly TimeSpan LayoutDiagnosticsCacheDuration =
        TimeSpan.FromMilliseconds(250);
    // How long a diagnostics-bearing state request keeps pushed deltas alive. Comfortably longer
    // than the inspector's poll interval so an open panel never flickers off.
    private static readonly TimeSpan DiagnosticsInterestWindow = TimeSpan.FromSeconds(30);
    private DateTime _diagnosticsInterestUtc = DateTime.MinValue;

    // Cap request bodies to avoid local DoS via huge POST payloads.
    private const long MaxRequestBodyBytes = 1_048_576; // 1 MB
    private const long MaxWorkflowFileBytes = 1_048_576;
    private const int MaxWorkflowFiles = 100;

    public int Port => _port;

    /// <summary>
    /// Port of the underlying DevFlow agent this inspector is proxying to. Used by
    /// the broker to detect when an agent has reconnected on a different port and
    /// the cached InspectorServer's AgentClient is now pointing at a dead port.
    /// </summary>
    public int AgentPort => _agentPort;

    public InspectorServer(int port, string agentHost, int agentPort, string? embedToken = null)
        : this(port, agentHost, agentPort, embedToken, agentId: null, appName: null, platform: null, project: null, sessionId: null)
    {
    }

    internal InspectorServer(
        int port,
        string agentHost,
        int agentPort,
        string? embedToken,
        string? agentId,
        string? appName,
        string? platform,
        string? project,
        string? sessionId,
        string? policyStartPath = null)
    {
        _port = port;
        _agentHost = agentHost;
        _agentPort = agentPort;
        _embedToken = embedToken;
        _agentId = agentId;
        _appName = appName;
        _platform = platform;
        _project = string.IsNullOrWhiteSpace(project) ? null : project;
        _sessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId;
        _sourcePropertyEditor = new XamlSourcePropertyEditor(project, sessionId);
        _alertController = new InspectorAlertController(agentHost, agentPort, appName, platform);
        _client = new AgentClient(agentHost, agentPort)
        {
            MutationLeaseHolderKind = "web-inspector",
            MutationLeaseLabel = "DevFlow Web Inspector"
        };
        // Fall back to the inspected project so layout-diagnostics suppressions resolve even when
        // the host does not pass an explicit policy start path.
        _policyStartPath = string.IsNullOrWhiteSpace(policyStartPath) ? _project : policyStartPath;
        _layoutDiagnosticsPolicy = LayoutDiagnosticsPolicyLoader.Load(_policyStartPath);
        _projectLayoutDiagnosticsPolicy =
            LayoutDiagnosticsPolicyLoader.LoadProjectPolicy(_policyStartPath);
    }

    private void InvalidateScreenshotCache()
    {
        lock (_cacheLock)
        {
            _cachedScreenshot = null;
            _cachedScreenshotElementId = null;
            _cachedScreenshotCaptureEpoch = null;
            _latestFrame = null;
            _cacheGeneration++;
        }
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
        => (400, "application/json", Encoding.UTF8.GetBytes(new JsonObject { ["error"] = error }.ToJsonString()));

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
            Encoding.UTF8.GetBytes(new JsonObject
            {
                ["ok"] = false,
                ["reason"] = outcome.Reason,
                ["retryable"] = outcome.Retryable
            }.ToJsonString()));
    }

    private static (int, string, byte[]) UiReadOutcomeResponse(UiReadResult outcome)
    {
        var statusCode = outcome.StatusCode is >= 400 and <= 599
            ? outcome.StatusCode.Value
            : 502;
        return (
            statusCode,
            "application/json",
            Encoding.UTF8.GetBytes(new JsonObject
            {
                ["ok"] = false,
                ["reason"] = outcome.Reason,
                ["retryable"] = outcome.Retryable
            }.ToJsonString()));
    }

    private async Task<bool> RequiresCaptureEpochAsync()
    {
        Task<bool?> detectionTask;
        lock (_cacheLock)
            detectionTask = _requiresCaptureEpochTask ??= DetectCaptureEpochRequirementAsync();

        try
        {
            var detected = await detectionTask;
            if (detected.HasValue)
                return detected.Value;
        }
        catch
        {
        }

        // A transport failure or non-capabilities JSON response must not permanently downgrade
        // this connection. Share one in-flight request, then let the next interaction retry.
        lock (_cacheLock)
        {
            if (ReferenceEquals(_requiresCaptureEpochTask, detectionTask))
                _requiresCaptureEpochTask = null;

            return _captureEpoch.HasValue;
        }
    }

    private async Task<bool?> DetectCaptureEpochRequirementAsync()
    {
        var response = await _client.GetCapabilitiesAsync();
        if (response.ValueKind != JsonValueKind.Object
            || !response.TryGetProperty("capabilities", out var capabilities)
            || capabilities.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!capabilities.TryGetProperty("ui.actions", out var uiActions)
            || uiActions.ValueKind != JsonValueKind.Object
            || !uiActions.TryGetProperty("features", out var features))
        {
            return false;
        }

        if (features.ValueKind != JsonValueKind.Array)
            return null;

        return features.EnumerateArray().Any(feature =>
            feature.ValueKind == JsonValueKind.String
            && feature.GetString() == "stale-capture-rejection");
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
                Body = body,
                // Carry the read-token header through to RouteAsync so the data-tab gate works in
                // broker mode too (the standalone path already parses all headers).
                Headers = new Dictionary<string, string>
                {
                    ["x-devflow-inspector-token"] = context.Request.Headers["X-DevFlow-Inspector-Token"] ?? "",
                    ["x-devflow-writer"] = context.Request.Headers["X-DevFlow-Writer"] ?? "",
                    ["x-devflow-lease"] = context.Request.Headers["X-DevFlow-Lease"] ?? "",
                    ["x-devflow-holder"] = context.Request.Headers["X-DevFlow-Holder"] ?? "",
                    ["x-devflow-label"] = context.Request.Headers["X-DevFlow-Label"] ?? "",
                },
                QueryString = context.Request.Url?.Query ?? string.Empty,
                Query = context.Request.QueryString.AllKeys
                    .Where(static key => key is not null)
                    .ToDictionary(
                        static key => key!,
                        key => context.Request.QueryString[key!] ?? "",
                        StringComparer.OrdinalIgnoreCase),
            };
            var (statusCode, contentType, responseBody) = await RouteAsync(request);

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = contentType;
            context.Response.Headers.Set("Cache-Control", "no-store");
            context.Response.Headers.Set("X-Content-Type-Options", "nosniff");
            // No CORS headers: the inspector UI is served same-origin from the broker.
            // Allowing cross-origin would let any web page drive the locally connected app.
            // Anti-framing headers (defense-in-depth against clickjacking): even though
            // the Origin validator already blocks cross-origin API calls, these headers
            // prevent a malicious page from rendering the inspector in an iframe.
            // A request that proves knowledge of the broker's unguessable embed token
            // (readable only from the local broker.json) is a trusted local host shell — relax the
            // anti-framing headers so the canvas / VS Code webview can embed the inspector. A remote
            // clickjacking page cannot read the token, so it still gets DENY.
            if (IsTrustedEmbed(_embedToken, context.Request.QueryString["embed"]))
            {
                // A request bearing the secret embed token is a trusted LOCAL host shell (the
                // canvas or the VS Code webview). Such hosts frame the inspector from origins we
                // cannot enumerate reliably — notably VS Code desktop webviews, whose ancestor is
                // an opaque `vscode-webview://<guid>` origin that CSP host/scheme sources do not
                // dependably match in Chromium. The unguessable token (readable only from the local
                // broker.json) is itself the anti-clickjacking gate: a remote page cannot obtain it,
                // and a request WITHOUT it still falls through to the DENY branch below. So for
                // token-bearing requests we drop the framing restrictions entirely rather than
                // maintain a brittle allow-list — this matches how a plain localhost dev server
                // (no framing headers) embeds cleanly in a webview.
                context.Response.Headers.Remove("X-Frame-Options");
                // Safe to strip the whole CSP because the broker path sets only `frame-ancestors`
                // in the untrusted branch below.
                context.Response.Headers.Remove("Content-Security-Policy");
                // The token is a bearer secret carried in the query string: keep it out of any
                // Referer, and out of shared history / proxy caches.
                context.Response.Headers.Set("Referrer-Policy", "no-referrer");
            }
            else
            {
                context.Response.Headers.Set("X-Frame-Options", "DENY");
                context.Response.Headers.Set("Content-Security-Policy", "frame-ancestors 'none'");
            }
            context.Response.ContentLength64 = responseBody.Length;
            await context.Response.OutputStream.WriteAsync(responseBody);
            context.Response.Close();
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 64)
        {
            // ERROR_NETNAME_DELETED: the browser closed the connection while a response was being
            // written. This is expected during reloads/tab closes and there is no client left to
            // receive an error response.
            try { context.Response.Close(); } catch { }
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
        try { _flowAdmissionGate.Dispose(); } catch { }
        try { _frameCreationGate.Dispose(); } catch { }
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
            var leaseId = request.Headers.TryGetValue("x-devflow-lease", out var lt) &&
                !string.IsNullOrWhiteSpace(lt)
                ? lt
                : request.Headers.TryGetValue("x-devflow-writer", out var wt) &&
                    !string.IsNullOrWhiteSpace(wt)
                    ? wt
                    : _fallbackMutationLeaseId;
            var holderKind = request.Headers.TryGetValue("x-devflow-holder", out var hk) &&
                !string.IsNullOrWhiteSpace(hk)
                ? hk
                : "web-inspector";
            var holderLabel = request.Headers.TryGetValue("x-devflow-label", out var hl) &&
                !string.IsNullOrWhiteSpace(hl)
                ? hl
                : "DevFlow Web Inspector";
            using var leaseScope = _client.UseMutationLease(leaseId, holderKind, holderLabel);

            // While a replay is driving the app, reject concurrent user mutations + record steps so
            // the replay isn't interleaved (a replay may be triggered from any embedding host/tab).
            // Read-only endpoints and the replay itself are unaffected.
            if (request.Method == "POST" && Volatile.Read(ref _replayInProgress) == 1 && IsBlockedDuringReplay(request.Path))
                return (409, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"A replay is in progress — try again when it finishes.\"}"));

            // Data tabs expose more than the visible tree (network/preferences/logs/files), so gate
            // them on the per-inspector read token that only same-origin devflow.js can echo back.
            if (IsTokenGatedPath(request.Path))
            {
                var token = request.Headers.TryGetValue("x-devflow-inspector-token", out var t) ? t : null;
                if (string.IsNullOrEmpty(token) || !string.Equals(token, _readToken, StringComparison.Ordinal))
                    return (403, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"forbidden\"}"));
            }

            // Mutations share the agent-enforced global lease with Canvas, MCP, CLI, and other hosts.
            if (request.Method == "POST" && IsMutation(request.Path))
            {
                var status = await _client.ControlMutationLeaseAsync(
                    "claim",
                    force: false,
                    leaseId,
                    holderKind,
                    holderLabel);
                if (!status.YouHold)
                {
                    var payload = new JsonObject
                    {
                        ["ok"] = false,
                        ["reason"] = "writer",
                        ["error"] = "Another session is driving this app.",
                        ["holderKind"] = status.HolderKind,
                        ["label"] = status.Label,
                        ["expiresInMs"] = status.ExpiresInMs,
                        ["authority"] = status.Authority
                    }.ToJsonString();
                    return (409, "application/json", Encoding.UTF8.GetBytes(payload));
                }
            }

            return request.Method switch
            {
                "GET" => request.Path switch
                {
                    "/" or "" => await HandleRootAsync(),
                    "/api/state" => await HandleStateAsync(request),
                    "/api/eventSupport" => await HandleEventSupportAsync(),
                    "/screenshot.png" => await HandleScreenshotAsync(request.Query.GetValueOrDefault("frame")),
                    "/devflow.js" => HandleEmbeddedFile("devflow.js", "application/javascript"),
                    "/inspector-api.js" => HandleEmbeddedFile("inspector-api.js", "application/javascript"),
                    "/inspector-dialog.js" => HandleEmbeddedFile("inspector-dialog.js", "application/javascript"),
                    "/inspector-data-context.js" => HandleEmbeddedFile("inspector-data-context.js", "application/javascript"),
                    "/inspector-properties.js" => HandleEmbeddedFile("inspector-properties.js", "application/javascript"),
                    "/inspector-tree.js" => HandleEmbeddedFile("inspector-tree.js", "application/javascript"),
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
                    "/api/hitTest" => await HandleProxyHitTestAsync(request.Body),
                    "/api/getProperties" => await HandleProxyGetPropertiesAsync(request.Body),
                    "/api/getProperty" => await HandleProxyGetPropertyAsync(request.Body),
                    "/api/setProperty" => await HandleProxySetPropertyAsync(request.Body),
                    "/api/persistProperty" => await HandlePersistPropertyAsync(request.Body),
                    "/api/navigate" => await HandleProxyNavigateAsync(request.Body),
                    "/api/checkpoint" => await HandleCheckpointAsync(request.Body),
                    "/api/source" => await HandleSourceAsync(request.Body),
                    "/api/flows/record/start" => await HandleFlowRecordStartAsync(request.Body),
                    "/api/flows/record/step" => await HandleFlowRecordStepAsync(request.Body),
                    "/api/flows/record/stop" => await HandleFlowRecordStopAsync(request.Body),
                    "/api/flows/record/cancel" => await HandleFlowRecordCancelAsync(request.Body),
                    "/api/flows/record/status" => await HandleFlowRecordStatusAsync(request.Body),
                    "/api/flows/files/list" => await HandleFlowFilesListAsync(),
                    "/api/flows/files/load" => await HandleFlowFileLoadAsync(request.Body),
                    "/api/flows/replay" => await HandleFlowReplayAsync(request.Body),
                    "/api/logs" => await HandleLogsAsync(request.Body),
                    "/api/network" => await HandleNetworkAsync(request.Body),
                    "/api/network/detail" => await HandleNetworkDetailAsync(request.Body),
                    "/api/preferences" => await HandlePreferencesAsync(request.Body),
                    "/api/device" => await HandleDeviceAsync(request.Body),
                    "/api/sensors" => await HandleSensorsAsync(request.Body),
                    "/api/geolocation" => await HandleGeolocationAsync(request.Body),
                    "/api/files/roots" => await HandleFilesRootsAsync(request.Body),
                    "/api/files/list" => await HandleFilesListAsync(request.Body),
                    "/api/alerts" => await HandleAlertsAsync(),
                    "/api/alerts/dismiss" => await HandleAlertDismissAsync(request.Body),
                    "/api/cdp/webviews" => await HandleCdpWebViewsAsync(request.Body),
                    "/api/cdp/source" => await HandleCdpSourceAsync(request.Body),
                    "/api/cdp/eval" => await HandleCdpEvalAsync(request.Body),
                    "/api/control" => await HandleControlAsync(request.Body, leaseId, holderKind, holderLabel),
                    "/api/diagnostics/suppress" => HandleDiagnosticSuppression(request.Body, remove: false),
                    "/api/diagnostics/unsuppress" => HandleDiagnosticSuppression(request.Body, remove: true),
                    "/api/diagnostics/agent-payload" => HandleDiagnosticAgentPayload(request.Body),
                    _ => (404, "text/plain", Encoding.UTF8.GetBytes("Not Found"))
                },
                _ => (405, "text/plain", Encoding.UTF8.GetBytes("Method Not Allowed"))
            };
        }
        catch (Exception ex) when (IsAgentUnavailableException(ex))
        {
            return (503, "application/json", Encoding.UTF8.GetBytes(
                "{\"ok\":false,\"error\":\"The DevFlow agent is unavailable.\"}"));
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

    private static bool IsAgentUnavailableException(Exception exception)
        => exception is HttpRequestException or SocketException or IOException or TaskCanceledException
            || (exception.InnerException is not null && IsAgentUnavailableException(exception.InnerException));

    private async Task<(int, string, byte[])> HandleRootAsync()
    {
        // The shell is served even when the agent is unreachable: devflow.js renders the
        // "disconnected" overlay and keeps polling /api/state, which is the endpoint that reports
        // an unavailable agent.
        var frame = await CreateFrameAsync();
        var html = HtmlRenderer.Render(
            frame.Tree,
            frame.Png.Length > 0,
            frame.Width,
            frame.Height,
            density: 1,
            elementScale: 1,
            frame.RootOffsetX,
            frame.RootOffsetY,
            screenshotUrl: $"screenshot.png?frame={Uri.EscapeDataString(frame.Id)}");
        // Inject the per-inspector read token so same-origin devflow.js can gate the data tabs.
        var tokenMeta = $"<meta name=\"devflow-inspector-token\" content=\"{_readToken}\">";
        var agentMeta = new StringBuilder()
            .Append(BuildMeta("devflow-agent-id", _agentId))
            .Append(BuildMeta("devflow-app-name", _appName))
            .Append(BuildMeta("devflow-platform", _platform))
            .Append(BuildMeta("devflow-agent-port", _agentPort.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToString();
        html = html.Contains("</head>", StringComparison.Ordinal)
            ? html.Replace("</head>", tokenMeta + agentMeta + "</head>")
            : tokenMeta + agentMeta + html;
        return (200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
    }

    private static string BuildMeta(string name, string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : $"<meta name=\"{name}\" content=\"{WebUtility.HtmlEncode(value)}\">";

    /// <summary>
    /// Returns JSON state for AJAX polling: screenshot (as timestamped URL) + element divs HTML.
    /// This avoids full page reload flash.
    /// </summary>
    private async Task<(int, string, byte[])> HandleStateAsync(HttpRequestInfo request)
    {
        // Layout diagnostics are expensive (a full analysis pass per frame), so they are opt-in:
        // the browser only asks while its Layout panel is open. Without this every state poll —
        // the inspector's normal heartbeat — paid for findings nobody was looking at.
        var wantsDiagnostics = IsTruthy(request.Query.GetValueOrDefault("diagnostics"));
        if (wantsDiagnostics)
        {
            lock (_cacheLock)
                _diagnosticsInterestUtc = DateTime.UtcNow;
        }
        var frame = await CreateFrameAsync();
        if (frame.Tree.Count == 0)
        {
            return (
                503,
                "application/json",
                Encoding.UTF8.GetBytes(new JsonObject
                {
                    ["ok"] = false,
                    ["error"] = "The DevFlow agent is unavailable.",
                    ["retryable"] = true
                }.ToJsonString()));
        }

        if (frame.Png.Length == 0)
        {
            return (
                503,
                "application/json",
                Encoding.UTF8.GetBytes(new JsonObject
                {
                    ["ok"] = false,
                    ["error"] = "Unable to capture the current app state.",
                    ["retryable"] = true
                }.ToJsonString()));
        }

        var diagnostics = wantsDiagnostics
            ? await ResolveFrameDiagnosticsAsync(frame.TreeRevision)
            : null;
        var json = new JsonObject
        {
            ["frameId"] = frame.Id,
            ["screenshotUrl"] = $"screenshot.png?frame={Uri.EscapeDataString(frame.Id)}",
            ["elements"] = frame.ElementsHtml,
            ["viewportWidth"] = frame.Width,
            ["viewportHeight"] = frame.Height,
            ["rootOffsetX"] = frame.RootOffsetX,
            ["rootOffsetY"] = frame.RootOffsetY,
            ["diagnostics"] = diagnostics is null
                ? null
                : JsonSerializer.SerializeToNode(diagnostics, DevFlowCliJsonContext.Default.LayoutInspectionResult),
            ["captureEpoch"] = frame.CaptureEpoch,
            ["registryGeneration"] = frame.RegistryGeneration,
            ["windowId"] = frame.WindowId
        }.ToJsonString();

        return (200, "application/json", Encoding.UTF8.GetBytes(json));
    }

    private async Task<(int, string, byte[])> HandleEventSupportAsync()
    {
        var capabilities = await _client.GetCapabilitiesAsync();
        var supported = SupportsUiEvents(capabilities);
        return Ok(new JsonObject { ["supported"] = supported }.ToJsonString());
    }

    internal static bool? SupportsUiEvents(JsonElement capabilities)
    {
        if (capabilities.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return null;
        if (capabilities.ValueKind != JsonValueKind.Object ||
            !capabilities.TryGetProperty("capabilities", out var domains) ||
            domains.ValueKind != JsonValueKind.Object ||
            !domains.TryGetProperty("ui.events", out var events) ||
            events.ValueKind != JsonValueKind.Object ||
            !events.TryGetProperty("features", out var features) ||
            features.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return features.EnumerateArray().Any(feature =>
            feature.ValueKind == JsonValueKind.String &&
            string.Equals(feature.GetString(), "stream", StringComparison.Ordinal));
    }

    /// <summary>
    /// Layout-diagnostic findings for a frame, paired with the tree revision the frame was built
    /// from. #412 ran its own retry loop to keep findings and screenshot coherent; the frame cache
    /// already guarantees a coherent tree/screenshot pair, so here we only have to reject findings
    /// computed over a different revision — pairing them would point overlays at moved elements.
    /// </summary>
    /// <summary>Accepts the usual query-flag spellings so callers can use ?diagnostics=1 or =true.</summary>
    private static bool IsTruthy(string? value)
        => value is not null &&
           (value.Equals("1", StringComparison.Ordinal) ||
            value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            value.Length == 0);

    private async Task<LayoutInspectionResult?> ResolveFrameDiagnosticsAsync(string treeRevision)
    {
        lock (_cacheLock)
        {
            if (DateTime.UtcNow - _latestLayoutDiagnosticsAt < LayoutDiagnosticsCacheDuration &&
                _latestLayoutDiagnostics is not null &&
                string.Equals(
                    _latestLayoutDiagnostics.Snapshot.TreeRevision,
                    treeRevision,
                    StringComparison.Ordinal))
            {
                return _latestLayoutDiagnostics;
            }
        }

        var diagnostics = await CaptureLayoutDiagnosticsAsync("wait");
        if (diagnostics is null)
            return null;

        // An empty revision means the agent does not version its tree; nothing to reconcile.
        if (!string.IsNullOrEmpty(treeRevision) &&
            !string.Equals(diagnostics.Snapshot.TreeRevision, treeRevision, StringComparison.Ordinal))
        {
            return null;
        }

        lock (_cacheLock)
        {
            _latestLayoutDiagnostics = diagnostics;
            _latestLayoutDiagnosticsAt = DateTime.UtcNow;
        }
        return diagnostics;
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
                // Must match the tree the Inspector fetches (native-inclusive), otherwise the
                // snapshot revisions are computed over different node sets and can never agree
                // while a native dialog or detached top-level window is present.
                Scope = new LayoutInspectionScope { IncludeNativeElements = true },
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
        // Push deltas only while a client is actually showing the Layout panel. The interest
        // window is refreshed by each diagnostics-bearing /api/state poll, so closing the panel
        // stops the analysis within one window instead of running it forever.
        lock (_cacheLock)
        {
            if (DateTime.UtcNow - _diagnosticsInterestUtc > DiagnosticsInterestWindow)
                return null;
        }

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
                    new JsonObject
                    {
                        ["success"] = false,
                        ["findingId"] = request.FindingId,
                        ["suppressed"] = true,
                        ["projectRemovable"] = false,
                        ["provenance"] = JsonSerializer.SerializeToNode(ex.Provenance.ToArray(), DevFlowCliJsonContext.Default.StringArray),
                        ["message"] = ex.Provenance.Count == 0
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
            new JsonObject
            {
                ["success"] = true,
                ["findingId"] = request.FindingId,
                ["suppressed"] = !remove,
                ["projectRemovable"] = !remove,
                ["provenance"] = JsonSerializer.SerializeToNode(
                    remove ? Array.Empty<string>() : ["project-exact"],
                    DevFlowCliJsonContext.Default.StringArray)
            });
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

    private async Task<(List<ElementInfo> Tree, string Revision)> GetTreeWithRetriesAsync()
    {
        const int maxAttempts = 3;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            // Snapshot (not plain tree) so the frame carries the revision layout diagnostics are
            // paired against — findings computed over a different revision cannot be trusted.
            var snapshot = await _client.GetTreeSnapshotAsync(includeNative: true);
            var tree = snapshot?.Elements ?? [];
            if (tree.Count > 0)
                return (tree, snapshot?.Revision ?? string.Empty);
            if (attempt + 1 < maxAttempts)
                await Task.Delay(TimeSpan.FromMilliseconds(25 * (attempt + 1)));
        }

        return ([], string.Empty);
    }

    /// <summary>
    /// Finds the ID of the topmost page element in the tree.
    /// When a modal page is showing, it appears as a later child of the Window,
    /// so we take the last child which is the topmost visible page.
    /// </summary>
    internal static string? FindRootPageId(List<ElementInfo> tree)
    {
        if (tree.Count == 0) return null;
        var window = tree[0];
        if (window.Children is not { Count: > 0 }) return null;
        // Last child is the topmost (modal pages are added after the shell). Registered native
        // elements are appended beneath their owner, so a window-owned registration would
        // otherwise be mistaken for the topmost page and replace it as the captured frame.
        for (var index = window.Children.Count - 1; index >= 0; index--)
        {
            if (!string.Equals(window.Children[index].Origin, "native", StringComparison.Ordinal))
                return window.Children[index].Id;
        }
        return null;
    }

    internal static List<ElementInfo> SelectFrameTree(List<ElementInfo> tree, string? rootPageId)
    {
        if (rootPageId is null || tree.Count == 0)
            return tree;
        var rootPage = tree[0].Children?.FirstOrDefault(child => child.Id == rootPageId);
        return rootPage is null ? tree : [rootPage];
    }

    /// <summary>
    /// Whether the frame must be captured full-screen rather than scoped to the root page.
    /// True when a visible native element lives outside the page subtree the frame would keep —
    /// either as a detached root, or as a sibling of the page under the window — because
    /// <see cref="SelectFrameTree"/> would otherwise drop it from the overlay.
    /// </summary>
    internal static bool RequiresFullscreenCapture(List<ElementInfo> tree, string? rootPageId)
    {
        if (tree.Skip(1).Any(HasVisibleNativeBounds))
            return true;

        if (tree.Count == 0) return false;
        var window = tree[0];
        if (window.Children is not { Count: > 0 }) return false;

        return window.Children.Any(child =>
            !string.Equals(child.Id, rootPageId, StringComparison.Ordinal)
            && HasVisibleNativeBounds(child));
    }

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

    private async Task<(int, string, byte[])> HandleScreenshotAsync(string? frameId)
    {
        byte[]? png = null;
        if (!string.IsNullOrWhiteSpace(frameId))
        {
            lock (_cacheLock)
            {
                PruneFramesLocked();
                if (_frames.TryGetValue(frameId, out var frame))
                    png = frame.Png;
            }
        }

        if (png is null && !string.IsNullOrWhiteSpace(frameId))
            return (404, "text/plain", Encoding.UTF8.GetBytes("Inspector frame expired"));
        png ??= (await CreateFrameAsync()).Png;
        if (png == null || png.Length == 0)
            return (404, "text/plain", Encoding.UTF8.GetBytes("No screenshot available"));
        return (200, "image/png", png);
    }

    private async Task<InspectorFrame> CreateFrameAsync()
    {
        lock (_cacheLock)
        {
            if (_latestFrame is not null && DateTime.UtcNow - _latestFrame.CreatedUtc < FrameReuseDuration)
                return _latestFrame;
        }

        await _frameCreationGate.WaitAsync(_lifetimeCts.Token);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                long generation;
                lock (_cacheLock)
                {
                    if (_latestFrame is not null && DateTime.UtcNow - _latestFrame.CreatedUtc < FrameReuseDuration)
                        return _latestFrame;
                    generation = _cacheGeneration;
                }

                var (tree, treeRevision) = await GetTreeWithRetriesAsync();
                var rootPageId = FindRootPageId(tree);
                // Detached native roots (dialogs, native overlays) live outside the page element, so
                // a page-scoped capture would miss them — fall back to a full-screen capture.
                var fullscreen = RequiresFullscreenCapture(tree, rootPageId);
                var (rootOffsetX, rootOffsetY) = fullscreen
                    ? (0d, 0d)
                    : GetRootPageOffset(tree, rootPageId);
                var (captureEpoch, registryGeneration, windowId) = GetCaptureMetadata(tree);
                var frameTree = fullscreen ? tree : SelectFrameTree(tree, rootPageId);
                var screenshot = await GetCachedScreenshotAsync(
                    fullscreen ? null : rootPageId,
                    captureEpoch,
                    registryGeneration,
                    fullscreen) ?? Array.Empty<byte>();
                var (width, height) = screenshot.Length > 0 ? GetPngDimensions(screenshot) : (0, 0);
                if (width <= 0 || height <= 0)
                {
                    width = 800;
                    height = 600;
                }

                var frame = new InspectorFrame(
                    Guid.NewGuid().ToString("N"),
                    DateTime.UtcNow,
                    frameTree,
                    screenshot,
                    width,
                    height,
                    rootOffsetX,
                    rootOffsetY,
                    HtmlRenderer.RenderElements(frameTree, 1, rootOffsetX, rootOffsetY),
                    captureEpoch,
                    registryGeneration,
                    windowId,
                    treeRevision);
                lock (_cacheLock)
                {
                    if (generation != _cacheGeneration)
                    {
                        if (attempt == 0) continue;
                        throw new InvalidOperationException("Inspector state changed repeatedly while capturing a frame.");
                    }

                    _captureEpoch = captureEpoch;

                    PruneFramesLocked();
                    _frames[frame.Id] = frame;
                    _latestFrame = frame;
                    while (_frames.Count > MaxCachedFrames ||
                        (_frames.Count > 1 && GetCachedFrameBytesLocked() > MaxCachedFrameBytes))
                    {
                        var oldest = _frames.MinBy(static pair => pair.Value.CreatedUtc);
                        if (oldest.Key is null) break;
                        _frames.Remove(oldest.Key);
                    }
                }
                return frame;
            }
        }
        finally
        {
            _frameCreationGate.Release();
        }
    }

    private void PruneFramesLocked()
    {
        var cutoff = DateTime.UtcNow - FrameCacheDuration;
        foreach (var id in _frames.Where(pair => pair.Value.CreatedUtc < cutoff).Select(static pair => pair.Key).ToArray())
            _frames.Remove(id);
    }

    private long GetCachedFrameBytesLocked()
        => _frames.Values.Sum(static frame => (long)frame.Png.Length);

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
        long generation;
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
            generation = _cacheGeneration;
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
        if (fresh is { Length: > MaxScreenshotBytes })
        {
            Console.Error.WriteLine($"[inspector] screenshot exceeded {MaxScreenshotBytes} bytes");
            return null;
        }
        lock (_cacheLock)
        {
            if (generation == _cacheGeneration)
            {
                _cachedScreenshot = fresh;
                _cachedScreenshotElementId = elementId;
                _cachedScreenshotCaptureEpoch = captureEpoch;
                _cachedScreenshotFullscreen = fullscreen;
                _screenshotCacheTime = DateTime.UtcNow;
            }
        }
        return fresh;
    }

    // ── Proxy handlers ──

    private async Task<(int, string, byte[])> HandleProxyHitTestAsync(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Body required\"}"));

        using var request = JsonDocument.Parse(body);
        var root = request.RootElement;
        if (!root.TryGetProperty("x", out var xProperty) ||
            !root.TryGetProperty("y", out var yProperty) ||
            xProperty.ValueKind != JsonValueKind.Number ||
            yProperty.ValueKind != JsonValueKind.Number)
        {
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"x/y required\"}"));
        }

        var hitResult = await _client.HitTestAsync(xProperty.GetDouble(), yProperty.GetDouble());
        if (!TryParseHitTestElements(hitResult, out var hitDocument, out var elements, out _, out _))
            return Ok("{\"ok\":true,\"elementId\":null,\"candidates\":[]}");

        using (hitDocument)
        {
            var candidates = new JsonArray();
            for (var index = 0; index < elements.GetArrayLength() && candidates.Count < 12; index++)
            {
                var element = elements[index];
                if (!element.TryGetProperty("id", out var idProperty) || idProperty.ValueKind != JsonValueKind.String)
                    continue;
                var id = idProperty.GetString();
                if (string.IsNullOrEmpty(id)) continue;

                candidates.Add((JsonNode?)new JsonObject
                {
                    ["id"] = id,
                    ["type"] = ReadOptionalString(element, "type"),
                    ["automationId"] = ReadOptionalString(element, "automationId"),
                    ["text"] = ReadOptionalString(element, "text")
                });
            }

            var elementId = candidates.Count > 0 ? (string?)candidates[0]!["id"] : null;
            var payload = new JsonObject
            {
                ["ok"] = true,
                ["elementId"] = elementId,
                ["candidates"] = candidates
            }.ToJsonString();
            return Ok(payload);
        }
    }

    private static string? ReadOptionalString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private async Task<(int, string, byte[])> HandleProxyTapAsync(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Body required\"}"));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Coordinates are already translated into window space by devflow.js using the frame's
        // root offsets, so every browser tab acts against the exact frame it rendered.
        if (root.TryGetProperty("x", out var xProp) && root.TryGetProperty("y", out var yProp))
        {
            var x = xProp.GetDouble();
            var y = yProp.GetDouble();

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

        // Coordinates are already translated into window space by devflow.js using the frame's
        // root offsets, so every browser tab acts against the exact frame it rendered.
        if (root.TryGetProperty("x", out var xProp) && root.TryGetProperty("y", out var yProp))
        {
            var x = xProp.GetDouble();
            var y = yProp.GetDouble();
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

    private sealed record InspectorFrame(
        string Id,
        DateTime CreatedUtc,
        List<ElementInfo> Tree,
        byte[] Png,
        int Width,
        int Height,
        double RootOffsetX,
        double RootOffsetY,
        string ElementsHtml,
        long? CaptureEpoch,
        long? RegistryGeneration,
        int? WindowId,
        string TreeRevision = "");

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

    // Reads a single property value off an element, proxied to the agent. Powers the shared
    // Inspector's rich property grid, using the same endpoint as the Canvas and VS Code hosts.
    private async Task<(int, string, byte[])> HandleProxyGetPropertyAsync(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Body required\"}"));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var elementId = root.TryGetProperty("elementId", out var idProp) ? idProp.GetString() : null;
        var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;

        if (!IsValidPropertyRef(elementId, name))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"elementId and name required\"}"));

        var value = await _client.GetPropertyAsync(elementId!, name!);
        var payload = new JsonObject { ["ok"] = true, ["value"] = value }.ToJsonString();
        return (200, "application/json", Encoding.UTF8.GetBytes(payload));
    }

    private async Task<(int, string, byte[])> HandleProxyGetPropertiesAsync(string? body)
    {
        var elementId = ReadStringField(body, "elementId");
        if (string.IsNullOrWhiteSpace(elementId) || elementId.Length > 1024)
            return BadRequest("elementId is required");

        var result = await _client.GetPropertyDescriptorsAsync(elementId);
        if (result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Array)
        {
            return Ok("{\"ok\":false,\"supported\":false}");
        }

        var descriptors = new JsonArray();
        foreach (var property in properties.EnumerateArray())
        {
            var node = JsonNode.Parse(property.GetRawText())!.AsObject();
            var name = property.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            node["persistable"] = !string.IsNullOrWhiteSpace(name) &&
                XamlSourcePropertyEditor.IsSupportedPropertyName(name);
            descriptors.Add((JsonNode?)node);
        }
        return Ok(new JsonObject { ["ok"] = true, ["supported"] = true, ["properties"] = descriptors }.ToJsonString());
    }

    // Live-edits a single property value on an element, proxied to the agent, then invalidates the
    // screenshot cache so the next frame reflects the change.
    private async Task<(int, string, byte[])> HandleProxySetPropertyAsync(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Body required\"}"));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var elementId = root.TryGetProperty("elementId", out var idProp) ? idProp.GetString() : null;
        var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;

        // Only scalar JSON is a valid property value. Number/bool raw text is culture-invariant.
        if (!TryReadScalarField(root, "value", out var value))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"value must be a string, number, or boolean\"}"));
        if (!IsValidPropertyRef(elementId, name) || value is null)
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"elementId, name and value required\"}"));

        var success = await _client.SetPropertyAsync(elementId!, name!, value);
        InvalidateScreenshotCache();
        return success
            ? (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"))
            : (500, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false}"));
    }

    // Persists an Inspector property value to an existing direct-literal XAML attribute. The agent
    // supplies source metadata; the broker validates and writes the local project file.
    private async Task<(int, string, byte[])> HandlePersistPropertyAsync(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"Body required\"}"));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var elementId = root.TryGetProperty("elementId", out var idProp) ? idProp.GetString() : null;
        var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
        if (!TryReadScalarField(root, "value", out var value))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"value must be a string, number, or boolean\"}"));
        if (!IsValidPropertyRef(elementId, name) || value is null)
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"elementId, name and value required\"}"));
        if (!XamlSourcePropertyEditor.IsSupportedPropertyName(name!))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"This property is not supported for XAML source persistence.\"}"));

        var transactionId = Guid.NewGuid().ToString("N");
        MutationLeaseStatus? transaction = null;
        try
        {
            transaction = await _client.ControlMutationLeaseAsync(
                "begin", false, null, null, null, transactionId);
            if (!transaction.YouHold || transaction.TransactionId != transactionId)
            {
                return (409, "application/json", Encoding.UTF8.GetBytes(
                    "{\"ok\":false,\"error\":\"Could not reserve the current mutation lease for XAML persistence.\"}"));
            }

        ElementInfo? element;
        try
        {
            element = await _client.GetElementAsync(elementId!);
        }
        catch
        {
            return (502, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"Could not resolve the element source.\"}"));
        }

        if (element is null)
            return (404, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"Element not found.\"}"));

        var sourcePath = element.SourceFile;
        if (string.IsNullOrWhiteSpace(sourcePath))
            return (422, "application/json", Encoding.UTF8.GetBytes(
                "{\"ok\":false,\"error\":\"This element does not have writable Debug XAML source metadata.\"}"));

        using var transactionCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        transactionCts.CancelAfter(TimeSpan.FromSeconds(45));
        var persistGate = PersistTransactionGates.GetOrAdd(sourcePath, static _ => new SemaphoreSlim(1, 1));
        await persistGate.WaitAsync(transactionCts.Token);
        try
        {

            var preflight = await _sourcePropertyEditor.ValidateAsync(element, name!, value, transactionCts.Token);
            if (!preflight.Success)
                return BuildPersistPropertyResponse(preflight);

            string? previousValue;
            try
            {
                previousValue = await _client.GetPropertyAsync(elementId!, name!);
            }
            catch
            {
                return (502, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"Could not read the current property value from the running app.\"}"));
            }
            if (previousValue is null)
            {
                return (422, "application/json", Encoding.UTF8.GetBytes(
                    "{\"ok\":false,\"error\":\"The current runtime property value is unavailable, so the change cannot be applied safely.\"}"));
            }

            bool runtimeAccepted;
            try
            {
                runtimeAccepted = await _client.SetPropertyAsync(elementId!, name!, value);
            }
            catch
            {
                return (502, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"Could not validate the property value against the running app.\"}"));
            }
            if (!runtimeAccepted)
            {
                return (422, "application/json", Encoding.UTF8.GetBytes(
                    "{\"ok\":false,\"error\":\"The running app rejected this property value, so the XAML source was not changed.\"}"));
            }
            InvalidateScreenshotCache();

            var result = await _sourcePropertyEditor.PersistAsync(element, name!, value, transactionCts.Token);
            if (!result.Success)
            {
                var restored = false;
                try { restored = await _client.SetPropertyAsync(elementId!, name!, previousValue); }
                catch { }
                InvalidateScreenshotCache();
                if (!restored)
                {
                    result = result with
                    {
                        Error = $"{result.Error} The source was not changed, but the running property could not be restored to its previous value."
                    };
                }
            }

            return BuildPersistPropertyResponse(result);
        }
        finally
        {
            persistGate.Release();
        }
        }
        finally
        {
            try
            {
                await _client.ControlMutationLeaseAsync(
                    "end", false, null, null, null, transactionId);
            }
            catch { }
        }
    }

    private static (int, string, byte[]) BuildPersistPropertyResponse(XamlSourceEditResult result)
    {
        var statusCode = result.Status switch
        {
            XamlSourceEditStatus.Success => 200,
            XamlSourceEditStatus.InvalidRequest => 400,
            XamlSourceEditStatus.Forbidden => 403,
            XamlSourceEditStatus.Stale => 409,
            XamlSourceEditStatus.SourceUnavailable or XamlSourceEditStatus.Unsupported => 422,
            _ => 500,
        };
        var payload = new JsonObject
        {
            ["ok"] = result.Success,
            ["error"] = result.Error,
            ["file"] = result.File,
            ["line"] = result.Line,
            ["column"] = result.Column,
            ["sourceHash"] = result.SourceHash
        }.ToJsonString();
        return (statusCode, "application/json", Encoding.UTF8.GetBytes(payload));
    }

    private static bool TryReadScalarField(JsonElement root, string name, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var property))
            return false;

        switch (property.ValueKind)
        {
            case JsonValueKind.String:
                value = property.GetString();
                return value is not null;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                value = property.GetRawText();
                return true;
            default:
                return false;
        }
    }

    // Guards the proxied property endpoints: non-empty and bounded elementId/name.
    private static bool IsValidPropertyRef(string? elementId, string? name)
        => !string.IsNullOrWhiteSpace(elementId) && !string.IsNullOrWhiteSpace(name)
           && elementId!.Length <= 1024 && name!.Length <= 256;

    // ── Workflow recording ──
    // The broker owns one app-scoped recording; the current valid lease holder controls it and the
    // agent observes every accepted mutation across lease handoffs.
    // These routes are compatibility adapters for the shared browser UI.
    private async Task<(int, string, byte[])> HandleFlowRecordStartAsync(string? body)
    {
        await _flowAdmissionGate.WaitAsync();
        try
        {
            if (Volatile.Read(ref _replayInProgress) == 1)
            {
                return (409, "application/json", Encoding.UTF8.GetBytes(
                    "{\"ok\":false,\"error\":\"A replay is in progress — try again when it finishes.\"}"));
            }

            string? name = null, app = null, platform = null, preconditions = null;
            if (!string.IsNullOrEmpty(body))
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
                app = root.TryGetProperty("app", out var a) ? a.GetString() : null;
                platform = root.TryGetProperty("platform", out var p) ? p.GetString() : null;
                preconditions = root.TryGetProperty("preconditions", out var pc) ? pc.GetString() : null;
            }
            if (string.IsNullOrWhiteSpace(name)) name = "scenario";

            // Best-effort agent metadata + the current route (the recording's start checkpoint).
            string? route = null;
            try
            {
                var status = await _client.GetStatusAsync();
                app ??= status?.App?.Name;
                platform ??= status?.Device?.Platform;
                route = status?.Route;
            }
            catch { /* best-effort — a recording can start without agent metadata */ }

            var result = await _client.ControlMutationRecordingAsync("start", name, app, platform, preconditions);
            var payload = new JsonObject
            {
                ["ok"] = result.Ok,
                ["recordingId"] = result.RecordingId,
                ["name"] = result.Name ?? name,
                ["steps"] = result.Steps,
                ["route"] = route,
                ["error"] = result.Error
            }.ToJsonString();
            return (result.Ok ? 200 : 400, "application/json", Encoding.UTF8.GetBytes(payload));
        }
        finally
        {
            _flowAdmissionGate.Release();
        }
    }

    private async Task<(int, string, byte[])> HandleFlowRecordStepAsync(string? body)
    {
        string? action = null;
        string? assertsJson = null;
        string? recordingId = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return BadRequest("Body must be a JSON object.");
                action = doc.RootElement.TryGetProperty("action", out var actionElement)
                    && actionElement.ValueKind == JsonValueKind.String
                        ? actionElement.GetString()
                        : null;
                assertsJson = doc.RootElement.TryGetProperty("assertsJson", out var assertsElement)
                    && assertsElement.ValueKind == JsonValueKind.String
                        ? assertsElement.GetString()
                        : null;
                recordingId = doc.RootElement.TryGetProperty("recordingId", out var recordingElement)
                    && recordingElement.ValueKind == JsonValueKind.String
                        ? recordingElement.GetString()
                        : null;
            }
            catch (JsonException)
            {
                return BadRequest("Invalid JSON body.");
            }
        }

        var result = string.Equals(action, Flows.FlowActions.Assert, StringComparison.OrdinalIgnoreCase)
            ? await _client.ObserveMutationRecordingAsync(new MutationRecordingObservation
            {
                Action = Flows.FlowActions.Assert,
                AssertsJson = assertsJson
            }, recordingId)
            : await _client.ControlMutationRecordingAsync("status");
        var payload = new JsonObject
        {
            ["ok"] = result.Ok,
            ["seq"] = result.Seq ?? result.Steps,
            ["stepCount"] = result.Steps,
            ["fragile"] = result.Fragile,
            ["error"] = result.Error
        }.ToJsonString();
        return (result.Ok ? 200 : 400, "application/json", Encoding.UTF8.GetBytes(payload));
    }

    private async Task<(int, string, byte[])> HandleFlowRecordStopAsync(string? body)
    {
        var recordingId = ReadStringField(body, "recordingId");
        var result = await _client.ControlMutationRecordingAsync("stop", null, null, null, null, recordingId);
        if (!result.Ok && result.Empty)
        {
            var cancelled = await _client.ControlMutationRecordingAsync("cancel-if-empty", null, null, null, null, recordingId);
            var emptyPayload = new JsonObject
            {
                ["ok"] = cancelled.Ok && cancelled.Empty,
                ["empty"] = cancelled.Empty,
                ["steps"] = 0,
                ["error"] = cancelled.Ok ? null : cancelled.Error ?? result.Error
            }.ToJsonString();
            return (cancelled.Ok && cancelled.Empty ? 200 : 409, "application/json", Encoding.UTF8.GetBytes(emptyPayload));
        }

        var payload = new JsonObject
        {
            ["ok"] = result.Ok,
            ["markdown"] = result.Markdown,
            ["name"] = result.Name,
            ["steps"] = result.Steps,
            ["warnings"] = result.Warnings is null ? null : JsonSerializer.SerializeToNode(result.Warnings, DevFlowCliJsonContext.Default.StringArray),
            ["error"] = result.Error
        }.ToJsonString();
        return (result.Ok ? 200 : 400, "application/json", Encoding.UTF8.GetBytes(payload));
    }

    private async Task<(int, string, byte[])> HandleFlowRecordStatusAsync(string? body)
    {
        var result = await _client.ControlMutationRecordingAsync("status");
        var payload = new JsonObject
        {
            ["ok"] = result.Ok,
            ["recording"] = result.Recording,
            ["recordingId"] = result.RecordingId,
            ["name"] = result.Name,
            ["steps"] = result.Steps,
            ["error"] = result.Error
        }.ToJsonString();
        return (result.Ok ? 200 : 400, "application/json", Encoding.UTF8.GetBytes(payload));
    }

    private async Task<(int, string, byte[])> HandleFlowRecordCancelAsync(string? body)
    {
        var result = await _client.ControlMutationRecordingAsync(
            "cancel",
            null,
            null,
            null,
            null,
            ReadStringField(body, "recordingId"));
        var payload = new JsonObject
        {
            ["ok"] = result.Ok,
            ["recording"] = result.Recording,
            ["recordingId"] = result.RecordingId,
            ["error"] = result.Error
        }.ToJsonString();
        return (result.Ok ? 200 : 400, "application/json", Encoding.UTF8.GetBytes(payload));
    }

    // Replays a recorded flow (its Markdown) against the live app via the existing FlowReplayer —
    // the same engine as maui_flow_replay — and returns a per-step pass/fail report. This RE-DRIVES
    // the app (destructive by nature); the UI gates it behind an explicit button and blocks it while
    // a recording is active.

    private async Task<(int, string, byte[])> HandleFlowFilesListAsync()
    {
        var root = await ResolveWorkflowRootAsync();
        if (root is null)
        {
            return JsonResponse(200, new JsonObject
            {
                ["ok"] = true,
                ["supported"] = false,
                ["tests"] = new JsonArray(),
                ["error"] = "The registered app project could not be resolved. Use Choose file instead."
            });
        }

        if (!Directory.Exists(root))
            return JsonResponse(200, new JsonObject { ["ok"] = true, ["supported"] = true, ["tests"] = new JsonArray() });

        try
        {
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                return JsonError(403, "The project workflow directory cannot be a symbolic link or reparse point.");

            var tests = new JsonArray();
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                .Where(path => string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path))
                .Where(file =>
                    (file.Attributes & FileAttributes.ReparsePoint) == 0 &&
                    file.Length <= MaxWorkflowFileBytes)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxWorkflowFiles))
            {
                tests.Add((JsonNode?)new JsonObject
                {
                    ["name"] = file.Name,
                    ["size"] = file.Length,
                    ["modifiedAt"] = file.LastWriteTimeUtc
                });
            }
            return JsonResponse(200, new JsonObject { ["ok"] = true, ["supported"] = true, ["tests"] = tests });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return JsonError(500, "Could not list project workflow tests.");
        }
    }

    private async Task<(int, string, byte[])> HandleFlowFileLoadAsync(string? body)
    {
        if (!TryReadWorkflowFileName(body, out var name, out var requestError))
            return JsonError(400, requestError!);

        var root = await ResolveWorkflowRootAsync();
        if (root is null)
            return JsonError(400, "The registered app project could not be resolved. Use Choose file instead.");

        string path;
        try
        {
            path = Path.GetFullPath(Path.Combine(root, name!));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return JsonError(400, "The workflow filename is invalid.");
        }

        if (!XamlSourcePropertyEditor.IsUnderRoot(path, root) ||
            !string.Equals(Path.GetFileName(path), name, StringComparison.Ordinal) ||
            !string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase))
        {
            return JsonError(403, "Only top-level Markdown files in the project maui-tests directory can be loaded.");
        }

        try
        {
            if (!File.Exists(path))
                return JsonError(404, "The selected workflow test no longer exists.");
            var info = new FileInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                XamlSourcePropertyEditor.PathContainsReparsePoint(root, path))
            {
                return JsonError(403, "Workflow tests reached through symbolic links or reparse points cannot be loaded.");
            }
            if (info.Length > MaxWorkflowFileBytes)
                return JsonError(413, "Workflow test files larger than 1 MB cannot be loaded.");

            var markdown = await File.ReadAllTextAsync(path, _lifetimeCts.Token);
            var parsed = Flows.FlowMarkdown.Parse(markdown, path);
            if (!parsed.Ok || parsed.Flow is null)
                return JsonError(400, parsed.Error ?? "Could not parse the workflow test.");
            if (parsed.Flow.Steps.Count > MaxReplaySteps)
                return JsonError(400, $"Flow too large (max {MaxReplaySteps} steps).");
            var validation = Flows.FlowValidator.Validate(parsed.Flow);
            if (!validation.Ok)
            {
                return JsonResponse(400, new JsonObject
                {
                    ["ok"] = false,
                    ["error"] = "Flow failed validation.",
                    ["errors"] = JsonSerializer.SerializeToNode(validation.Errors, DevFlowCliJsonContext.Default.ListString),
                    ["warnings"] = JsonSerializer.SerializeToNode(validation.Warnings, DevFlowCliJsonContext.Default.ListString)
                });
            }

            return JsonResponse(200, new JsonObject
            {
                ["ok"] = true,
                ["name"] = name,
                ["markdown"] = markdown,
                ["steps"] = parsed.Flow.Steps.Count,
                ["warnings"] = JsonSerializer.SerializeToNode(validation.Warnings, DevFlowCliJsonContext.Default.ListString)
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return JsonError(500, "Could not read the selected workflow test.");
        }
    }

    private async Task<string?> ResolveWorkflowRootAsync()
    {
        if (_project is null)
            return null;

        if (Path.IsPathFullyQualified(_project))
        {
            string projectPath;
            try { projectPath = Path.GetFullPath(_project); }
            catch { return null; }
            if (Directory.Exists(projectPath))
                return Path.Combine(projectPath, "maui-tests");
            if (File.Exists(projectPath))
                return Path.Combine(Path.GetDirectoryName(projectPath)!, "maui-tests");
        }

        try
        {
            var tree = await _client.GetTreeAsync();
            var sourceFile = EnumerateElements(tree)
                .Select(element => element.SourceFile)
                .FirstOrDefault(file => !string.IsNullOrWhiteSpace(file) && Path.IsPathFullyQualified(file));
            if (sourceFile is null)
                return null;
            var projectRoot = XamlSourcePropertyEditor.FindProjectRoot(sourceFile, _project, _sessionId);
            return projectRoot is null ? null : Path.Combine(projectRoot, "maui-tests");
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<ElementInfo> EnumerateElements(IEnumerable<ElementInfo> roots)
    {
        foreach (var element in roots)
        {
            yield return element;
            if (element.Children is { Count: > 0 })
            {
                foreach (var child in EnumerateElements(element.Children))
                    yield return child;
            }
        }
    }

    private static bool TryReadWorkflowFileName(string? body, out string? name, out string? error)
    {
        name = null;
        error = null;
        if (string.IsNullOrWhiteSpace(body))
        {
            error = "Body required.";
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("name", out var value) ||
                value.ValueKind != JsonValueKind.String)
            {
                error = "name must be a string.";
                return false;
            }
            name = value.GetString()?.Trim();
        }
        catch (JsonException)
        {
            error = "Invalid JSON body.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(name) ||
            name.Length > 255 ||
            !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal) ||
            !string.Equals(Path.GetExtension(name), ".md", StringComparison.OrdinalIgnoreCase))
        {
            error = "name must be a top-level .md filename.";
            return false;
        }
        return true;
    }

    private static (int, string, byte[]) JsonResponse(int status, JsonNode value)
        => (status, "application/json", Encoding.UTF8.GetBytes(value.ToJsonString()));

    private static (int, string, byte[]) JsonError(int status, string error)
        => JsonResponse(status, new JsonObject { ["ok"] = false, ["error"] = error });

    private async Task<(int, string, byte[])> HandleFlowReplayAsync(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Body required\"}"));

        string? markdown;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("markdown", out var value) ||
                value.ValueKind != JsonValueKind.String)
            {
                return (400, "application/json", Encoding.UTF8.GetBytes(
                    "{\"ok\":false,\"error\":\"markdown must be a string.\"}"));
            }
            markdown = value.GetString();
        }
        catch (JsonException)
        {
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"Invalid JSON body.\"}"));
        }
        if (string.IsNullOrEmpty(markdown))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"markdown required\"}"));

        var parsed = Flows.FlowMarkdown.Parse(markdown);
        if (!parsed.Ok || parsed.Flow is null)
            return (400, "application/json", Encoding.UTF8.GetBytes(new JsonObject { ["ok"] = false, ["error"] = parsed.Error ?? "Could not parse the flow." }.ToJsonString()));
        if (parsed.Flow.Steps.Count > MaxReplaySteps)
            return (400, "application/json", Encoding.UTF8.GetBytes($"{{\"ok\":false,\"error\":\"Flow too large (max {MaxReplaySteps} steps).\"}}"));

        var validation = Flows.FlowValidator.Validate(parsed.Flow);
        if (!validation.Ok)
        {
            var payload = new JsonObject
            {
                ["ok"] = false,
                ["error"] = "Flow failed validation.",
                ["errors"] = JsonSerializer.SerializeToNode(validation.Errors, DevFlowCliJsonContext.Default.ListString),
                ["warnings"] = JsonSerializer.SerializeToNode(validation.Warnings, DevFlowCliJsonContext.Default.ListString)
            }.ToJsonString();
            return (400, "application/json", Encoding.UTF8.GetBytes(payload));
        }

        await _flowAdmissionGate.WaitAsync();
        try
        {
            var recording = await _client.ControlMutationRecordingAsync("status");
            if (recording.Ok && recording.Recording)
            {
                return (409, "application/json", Encoding.UTF8.GetBytes(
                    "{\"ok\":false,\"error\":\"Stop the active recording before replaying a flow.\"}"));
            }

            // Single-flight: only one replay at a time; the RouteAsync gate blocks concurrent
            // mutations while this flag is set.
            if (Interlocked.CompareExchange(ref _replayInProgress, 1, 0) != 0)
                return (409, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"A replay is already in progress.\"}"));
        }
        finally
        {
            _flowAdmissionGate.Release();
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var replayer = new Flows.FlowReplayer(_client);
            var report = await replayer.ReplayAsync(parsed.Flow, null, cts.Token);
            return (200, "application/json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(report, DevFlowCliJsonPreserveNullContext.Default.FlowReplayReport)));
        }
        catch (OperationCanceledException)
        {
            // Surface a timeout as a normal replay failure, not a generic server error.
            var timeout = new JsonObject
            {
                ["ok"] = false,
                ["error"] = "Replay timed out.",
                ["total"] = parsed.Flow.Steps.Count,
                ["passed"] = 0,
                ["failed"] = parsed.Flow.Steps.Count
            }.ToJsonString();
            return (200, "application/json", Encoding.UTF8.GetBytes(timeout));
        }
        finally
        {
            Interlocked.Exchange(ref _replayInProgress, 0);
        }
    }

    private const int MaxReplaySteps = 2000;

    // POST paths rejected (409) while a replay is driving the app.
    internal static bool IsBlockedDuringReplay(string path) => path switch
    {
        "/api/tap" or "/api/scroll" or "/api/gesture" or "/api/back" or "/api/fill" or "/api/key"
            or "/api/setProperty" or "/api/persistProperty" or "/api/navigate" or "/api/cdp/eval"
            or "/api/alerts/dismiss" or "/api/flows/record/start" or "/api/flows/record/step" => true,
        _ => false,
    };

    // Proxies a Shell navigation to the agent — powers the "Return to start route" checkpoint restore.
    private async Task<(int, string, byte[])> HandleProxyNavigateAsync(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Body required\"}"));
        using var doc = JsonDocument.Parse(body);
        var route = doc.RootElement.TryGetProperty("route", out var v) ? v.GetString() : null;
        if (string.IsNullOrWhiteSpace(route) || route!.Length > 2048)
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"route required\"}"));
        var success = await _client.NavigateAsync(route);
        InvalidateScreenshotCache();
        return success
            ? (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"))
            : (500, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"navigate failed\"}"));
    }

    // Returns the app's current Shell route so the inspector can capture a "return here" checkpoint
    // (e.g. just before a replay). Read-only; navigation itself goes through /api/navigate.
    private async Task<(int, string, byte[])> HandleCheckpointAsync(string? body)
    {
        string? route = null;
        try { route = (await _client.GetStatusAsync())?.Route; } catch { /* best-effort */ }
        var payload = new JsonObject { ["ok"] = true, ["route"] = route }.ToJsonString();
        return (200, "application/json", Encoding.UTF8.GetBytes(payload));
    }

    // Returns the XAML source location for an element on demand, so absolute paths are
    // never embedded in every element div). Powers click-to-XAML "open source" via the host bridge.
    private async Task<(int, string, byte[])> HandleSourceAsync(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Body required\"}"));
        using var doc = JsonDocument.Parse(body);
        var elementId = doc.RootElement.TryGetProperty("elementId", out var v) ? v.GetString() : null;
        if (string.IsNullOrWhiteSpace(elementId) || elementId!.Length > 1024)
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"elementId required\"}"));
        var el = await _client.GetElementAsync(elementId);
        if (el?.SourceFile is null)
            return (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"No source available for this element.\"}"));
        var payload = new JsonObject
        {
            ["ok"] = true,
            ["file"] = el.SourceFile,
            ["line"] = el.SourceLine,
            ["column"] = el.SourceColumn,
            ["sourceHash"] = el.SourceHash
        }.ToJsonString();
        return (200, "application/json", Encoding.UTF8.GetBytes(payload));
    }

    // ── Data tabs — read-only proxies to existing AgentClient reads (Logs/Network/Preferences/
    // Device/Sensors/Files). POST-with-body so they inherit the Origin/CSRF guard; token-gated
    // (IsTokenGatedPath) so only same-origin devflow.js can call them; secrets redacted server-side
    // by default. None are in IsBlockedDuringReplay, so reads stay live during a replay. Secure
    // Storage is intentionally NOT exposed (no safe read-only presentation for secret values). ──

    private const int MaxLogLimit = 200;
    private const int MaxNetworkLimit = 200;

    // Paths whose responses expose more than the visible tree and therefore require the read token.
    internal static bool IsTokenGatedPath(string path) => path switch
    {
        "/api/source" or "/api/persistProperty" or "/api/logs" or "/api/network" or "/api/network/detail" or "/api/preferences"
            or "/api/device" or "/api/sensors" or "/api/geolocation"
            or "/api/files/roots" or "/api/files/list"
            or "/api/flows/files/list" or "/api/flows/files/load"
            or "/api/alerts" or "/api/alerts/dismiss"
            or "/api/cdp/webviews" or "/api/cdp/source" or "/api/cdp/eval" => true,
        _ => false,
    };

    private async Task<(int, string, byte[])> HandleAlertsAsync()
    {
        var result = await _alertController.DetectAsync();
        return Ok(JsonSerializer.Serialize(result, DevFlowCliJsonPreserveNullContext.Default.InspectorAlertResult));
    }

    private async Task<(int, string, byte[])> HandleAlertDismissAsync(string? body)
    {
        var buttonLabel = ReadStringField(body, "buttonLabel");
        if (string.IsNullOrWhiteSpace(buttonLabel) || buttonLabel.Length > 256)
            return BadRequest("buttonLabel is required and must be 256 characters or fewer");
        var result = await _alertController.DismissAsync(buttonLabel);
        return Ok(JsonSerializer.Serialize(result, DevFlowCliJsonPreserveNullContext.Default.InspectorAlertResult));
    }

    private async Task<(int, string, byte[])> HandleLogsAsync(string? body)
    {
        var limit = ReadIntField(body, "limit", 100, 1, MaxLogLimit);
        var source = ReadStringField(body, "source");
        try
        {
            var raw = await _client.GetLogsAsync(limit, 0, string.IsNullOrWhiteSpace(source) ? null : source);
            // The agent returns a JSON array of {t,l,c,m,e,s}. Mask obvious secrets (JWT/Bearer) in
            // the raw text — safe because the replacements never introduce quotes.
            var masked = MaskSecrets(raw);
            return Ok(WrapRaw("logs", masked));
        }
        catch { return Ok("{\"ok\":false,\"error\":\"logs unavailable\"}"); }
    }

    private async Task<(int, string, byte[])> HandleNetworkAsync(string? body)
    {
        var limit = ReadIntField(body, "limit", 100, 1, MaxNetworkLimit);
        try
        {
            var requests = await _client.GetNetworkRequestsAsync(limit);
            foreach (var r in requests) RedactNetwork(r);
            var node = new JsonObject
            {
                ["ok"] = true,
                ["requests"] = JsonSerializer.SerializeToNode(requests, DevFlowCliJsonPreserveNullContext.Default.ListNetworkRequest)
            };
            return Ok(node.ToJsonString());
        }
        catch { return Ok("{\"ok\":false,\"error\":\"network unavailable\"}"); }
    }

    private async Task<(int, string, byte[])> HandleNetworkDetailAsync(string? body)
    {
        var id = ReadStringField(body, "id");
        if (string.IsNullOrWhiteSpace(id) || id!.Length > 256)
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"id required\"}"));
        try
        {
            var detail = await _client.GetNetworkRequestDetailAsync(id);
            if (detail is null) return Ok("{\"ok\":false,\"error\":\"not found\"}");
            RedactNetwork(detail);
            var node = new JsonObject
            {
                ["ok"] = true,
                ["request"] = JsonSerializer.SerializeToNode(detail, DevFlowCliJsonPreserveNullContext.Default.NetworkRequest)
            };
            return Ok(node.ToJsonString());
        }
        catch { return Ok("{\"ok\":false,\"error\":\"network unavailable\"}"); }
    }

    private async Task<(int, string, byte[])> HandlePreferencesAsync(string? body)
    {
        var shared = ReadStringField(body, "sharedName");
        try
        {
            var prefs = await _client.GetPreferencesAsync(string.IsNullOrWhiteSpace(shared) ? null : shared);
            // Unknown shape across platforms — mask obvious secrets in the serialized text; the client
            // additionally masks values by key heuristic and only reveals on demand.
            var masked = MaskSecrets(CliJson.PrettyPrint(prefs, indented: false));
            return Ok(WrapRaw("preferences", masked));
        }
        catch { return Ok("{\"ok\":false,\"error\":\"preferences unavailable\"}"); }
    }

    // device-info/display are always safe; battery/connectivity are best-effort (may be unsupported).
    private static readonly string[] DeviceEndpoints = { "device-info", "device-display", "battery", "connectivity" };

    private async Task<(int, string, byte[])> HandleDeviceAsync(string? body)
    {
        var device = new JsonObject();
        foreach (var ep in DeviceEndpoints)
        {
            try { device[ep] = JsonValue.Create(await _client.GetPlatformInfoAsync(ep)); }
            catch { /* unsupported on this platform — omit */ }
        }
        return Ok(new JsonObject { ["ok"] = true, ["device"] = device }.ToJsonString());
    }

    private async Task<(int, string, byte[])> HandleSensorsAsync(string? body)
    {
        try
        {
            var sensors = await _client.GetSensorsAsync();
            return Ok(new JsonObject { ["ok"] = true, ["sensors"] = JsonValue.Create(sensors) }.ToJsonString());
        }
        catch { return Ok("{\"ok\":false,\"error\":\"sensors unavailable\"}"); }
    }

    private async Task<(int, string, byte[])> HandleGeolocationAsync(string? body)
    {
        // Explicit user gesture only (the client wires this to a button, never auto-load); blocked
        // during replay is unnecessary (reads pass) but the client pauses it. Clamp the timeout.
        try
        {
            var loc = await _client.GetGeolocationAsync(accuracy: "medium", timeoutSeconds: 10);
            return Ok(new JsonObject { ["ok"] = true, ["location"] = JsonValue.Create(loc) }.ToJsonString());
        }
        catch { return Ok("{\"ok\":false,\"error\":\"geolocation unavailable\"}"); }
    }

    private async Task<(int, string, byte[])> HandleFilesRootsAsync(string? body)
    {
        try
        {
            var roots = await _client.ListStorageRootsAsync();
            return Ok(new JsonObject { ["ok"] = true, ["roots"] = JsonValue.Create(roots) }.ToJsonString());
        }
        catch { return Ok("{\"ok\":false,\"error\":\"storage unavailable\"}"); }
    }

    private async Task<(int, string, byte[])> HandleFilesListAsync(string? body)
    {
        var root = ReadStringField(body, "root");
        var path = ReadStringField(body, "path");
        // Defense-in-depth at the broker before proxying: reject traversal, NUL, rooted/overlong
        // paths, and overlong root ids. The agent's FileStoragePathResolver is the real boundary.
        if (path is not null && (path.Contains("..", StringComparison.Ordinal) || path.Contains('\0')
                || path.StartsWith('/') || path.StartsWith('\\') || path.Length > 4096))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"invalid path\"}"));
        if (root is not null && (root.Length > 256 || root.Contains('\0')))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"invalid root\"}"));
        try
        {
            var files = await _client.ListFilesAsync(string.IsNullOrWhiteSpace(path) ? null : path,
                                                     string.IsNullOrWhiteSpace(root) ? null : root);
            return Ok(new JsonObject { ["ok"] = true, ["files"] = JsonValue.Create(files) }.ToJsonString());
        }
        catch { return Ok("{\"ok\":false,\"error\":\"files unavailable\"}"); }
    }

    // ── Blazor WebView CDP tab — list WebViews, view source, and evaluate JavaScript through the
    // existing chobitsu.js CDP bridge.
    private async Task<(int, string, byte[])> HandleCdpWebViewsAsync(string? body)
    {
        try
        {
            var wv = await _client.GetCdpWebViewsAsync();
            return Ok(new JsonObject { ["ok"] = true, ["webviews"] = JsonValue.Create(wv) }.ToJsonString());
        }
        catch { return Ok("{\"ok\":false,\"error\":\"no WebViews / CDP unavailable\"}"); }
    }

    private async Task<(int, string, byte[])> HandleCdpSourceAsync(string? body)
    {
        var id = ReadStringField(body, "webviewId");
        try
        {
            var src = await _client.GetCdpSourceAsync(string.IsNullOrWhiteSpace(id) ? null : id);
            return Ok(new JsonObject { ["ok"] = true, ["source"] = src }.ToJsonString());
        }
        catch { return Ok("{\"ok\":false,\"error\":\"source unavailable\"}"); }
    }

    private async Task<(int, string, byte[])> HandleCdpEvalAsync(string? body)
    {
        var expr = ReadStringField(body, "expression");
        var id = ReadStringField(body, "webviewId");
        if (string.IsNullOrWhiteSpace(expr) || expr!.Length > 8192)
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"expression required\"}"));
        try
        {
            var pars = new System.Text.Json.Nodes.JsonObject { ["expression"] = expr, ["returnByValue"] = true };
            var res = await _client.SendCdpCommandAsync("Runtime.evaluate", pars, string.IsNullOrWhiteSpace(id) ? null : id);
            return Ok(new JsonObject { ["ok"] = true, ["result"] = JsonValue.Create(res) }.ToJsonString());
        }
        catch { return Ok("{\"ok\":false,\"error\":\"evaluate failed\"}"); }
    }

    // ── Data helpers ──

    private static (int, string, byte[]) Ok(string json) => (200, "application/json", Encoding.UTF8.GetBytes(json));
    private static (int, string, byte[]) BadRequest(string error)
        => (400, "application/json", Encoding.UTF8.GetBytes(new JsonObject { ["error"] = error }.ToJsonString()));
    private static string WrapRaw(string field, string rawJson)
        => "{\"ok\":true,\"" + field + "\":" + (string.IsNullOrWhiteSpace(rawJson) ? "null" : rawJson) + "}";

    private static int ReadIntField(string? body, string name, int dflt, int min, int max)
    {
        if (string.IsNullOrEmpty(body)) return dflt;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n))
                return Math.Clamp(n, min, max);
        }
        catch { }
        return dflt;
    }

    private static string? ReadStringField(string? body, string name)
    {
        if (string.IsNullOrEmpty(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
        }
        catch { }
        return null;
    }

    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization", "proxy-authorization", "cookie", "set-cookie", "x-api-key", "x-auth-token",
    };

    private static readonly string[] SensitiveHeaderFragments = { "token", "secret", "auth", "cookie", "apikey", "api-key", "api_key" };

    // Structural redaction of a captured HTTP request: mask sensitive headers, drop bodies, and strip
    // secret query-string values from the URL/path.
    private static void RedactNetwork(NetworkRequest r)
    {
        RedactHeaders(r.RequestHeaders);
        RedactHeaders(r.ResponseHeaders);
        r.RequestBody = r.RequestBody is null ? null : "<hidden>";
        r.ResponseBody = r.ResponseBody is null ? null : "<hidden>";
        r.Url = MaskUrlSecrets(r.Url);
        if (r.Path is not null) r.Path = MaskUrlSecrets(r.Path);
    }

    internal static void RedactHeaders(Dictionary<string, string[]>? headers)
    {
        if (headers is null) return;
        foreach (var key in headers.Keys.ToList())
        {
            var lower = key.ToLowerInvariant();
            if (SensitiveHeaders.Contains(key) || SensitiveHeaderFragments.Any(f => lower.Contains(f)))
                headers[key] = new[] { "<redacted>" };
        }
    }

    private static readonly System.Text.RegularExpressions.Regex UrlSecretRegex = new(
        @"(?i)([?&](?:access_token|refresh_token|id_token|token|api[_-]?key|apikey|key|secret|password|code|sig|signature)=)[^&#\s]+",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    internal static string MaskUrlSecrets(string url)
        => string.IsNullOrEmpty(url) ? url : UrlSecretRegex.Replace(url, "$1<redacted>");

    // Mask JWTs, Bearer tokens, and "secretKey":"value" pairs in free-form JSON text without
    // unbalancing quotes (replacements never introduce a double-quote).
    private static readonly System.Text.RegularExpressions.Regex JwtRegex = new(
        @"eyJ[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}",
        System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex BearerRegex = new(
        @"(?i)(bearer\s+)[A-Za-z0-9._~+/=-]{12,}",
        System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex SecretKvRegex = new(
        "(?i)(\"(?:[a-z0-9_-]*(?:token|secret|password|apikey|api[_-]?key|authorization)[a-z0-9_-]*)\"\\s*:\\s*)\"[^\"]*\"",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    internal static string MaskSecrets(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = JwtRegex.Replace(text, "<jwt>");
        text = BearerRegex.Replace(text, "$1<redacted>");
        text = SecretKvRegex.Replace(text, "$1\"<redacted>\"");
        return text;
    }

    internal static bool IsMutation(string path) => path switch
    {
        "/api/tap" or "/api/scroll" or "/api/gesture" or "/api/back" or "/api/fill" or "/api/key"
            or "/api/setProperty" or "/api/persistProperty" or "/api/navigate" or "/api/cdp/eval"
            or "/api/alerts/dismiss"
            or "/api/flows/record/start" or "/api/flows/record/step" or "/api/flows/replay" => true,
        _ => false,
    };

    // Presence + take-control endpoint for the agent-enforced global mutation lease.
    private async Task<(int, string, byte[])> HandleControlAsync(
        string? body,
        string leaseId,
        string holderKind,
        string holderLabel)
    {
        var action = ReadStringField(body, "action") ?? "status";
        var status = await _client.ControlMutationLeaseAsync(
            action,
            force: ReadBoolField(body, "force"),
            leaseId,
            holderKind,
            holderLabel);
        return Ok(new JsonObject
        {
            ["ok"] = status.Ok,
            ["youAreWriter"] = status.YouHold,
            ["heldByOther"] = status.HeldByOther,
            ["holderKind"] = status.HolderKind,
            ["label"] = status.Label,
            ["expiresInMs"] = status.ExpiresInMs,
            ["authority"] = status.Authority
        }.ToJsonString());
    }

    private static bool ReadBoolField(string? body, string name)
    {
        if (string.IsNullOrEmpty(body)) return false;
        try { using var doc = JsonDocument.Parse(body); return doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True; }
        catch { return false; }
    }

    /// <summary>
    /// True when a broker request carries the exact embed token — i.e. a trusted LOCAL host shell
    /// (canvas / VS Code) that may embed the inspector in an iframe. The token is only obtainable
    /// from the local broker.json, so a remote clickjacking page cannot match it.
    /// </summary>
    internal static bool IsTrustedEmbed(string? embedToken, string? requestEmbed)
        => !string.IsNullOrEmpty(embedToken) && string.Equals(embedToken, requestEmbed, StringComparison.Ordinal);

    // ── WebSocket proxy (pass-through to agent /ws/v1/ui/events) ──

    private async Task HandleWebSocketProxy(TcpClient tcpClient, NetworkStream clientStream, HttpRequestInfo request, CancellationToken ct)
    {
        // Complete WebSocket handshake with browser
        if (!request.Headers.TryGetValue("sec-websocket-key", out var wsKey))
            return;

        var acceptKey = ComputeWebSocketAcceptKey(wsKey);

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

    internal static string ComputeWebSocketAcceptKey(string webSocketKey)
        => Convert.ToBase64String(
            System.Security.Cryptography.SHA1.HashData(
                Encoding.ASCII.GetBytes(webSocketKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));

    private static async Task SendWebSocketFrameAsync(NetworkStream stream, SemaphoreSlim sendGate, byte[] payload, CancellationToken ct)
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
        var fullPath = requestLine[1];
        var queryStart = fullPath.IndexOf('?');
        var queryString = queryStart >= 0 ? fullPath[queryStart..] : string.Empty;
        var path = (queryStart >= 0 ? fullPath[..queryStart] : fullPath).TrimEnd('/');
        if (string.IsNullOrEmpty(path)) path = "/";
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (queryStart >= 0 && queryStart < fullPath.Length - 1)
        {
            foreach (var part in fullPath[(queryStart + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split('=', 2);
                query[Uri.UnescapeDataString(pair[0])] = pair.Length == 2
                    ? Uri.UnescapeDataString(pair[1])
                    : "";
            }
        }

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
            Query = query,
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
                     "Cache-Control: no-store\r\n" +
                     "X-Content-Type-Options: nosniff\r\n" +
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
        public Dictionary<string, string> Query { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public string QueryString { get; init; } = "";
        public Dictionary<string, string> Headers { get; init; } = new();
        public string? Body { get; init; }
    }
}
