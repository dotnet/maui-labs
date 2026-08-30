using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Driver;
using Testing = Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Inspector;

/// <summary>
/// Lightweight HTTP server that serves the DevFlow Web Inspector.
/// Generates an interactive HTML page representing the native app's visual tree
/// and proxies interaction commands to the DevFlow agent.
/// </summary>
public sealed partial class InspectorServer : IDisposable
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
    private readonly string? _agentInstanceId;
    private readonly string? _appName;
    private readonly string? _platform;
    private readonly string? _project;
    private readonly string? _sessionId;
    private readonly Testing.MauiPreviewFeatureFlags _previewFlags;
    private readonly Func<string?, bool>? _trustedHostApprovalVerifier;
    private readonly AgentRegistration _checkpointRegistration;
    private readonly RouteCheckpointCoordinator _checkpoints;
    private readonly Func<
        Testing.MauiFlow,
        Func<AgentClient, Testing.IFlowReplayEvidenceCapture?>,
        WorkflowRunLeaseHandoff,
        CancellationToken,
        Task<Testing.FlowReplayReport>>? _workflowReplay;
    // Broker mode supplies a target-bound adapter. Standalone Inspector servers deliberately do
    // not emulate workflow coordination or artifact trust with direct agent calls.
    private readonly InspectorWorkflowServices? _workflowServices;
    private readonly TestAgentSessionService? _testAgentSessions;
    private readonly Func<
        Testing.MauiTestAgentTargetState,
        Task<Testing.MauiTestAgentTargetState?>>? _testAgentTargetStateRefresh;
    private readonly WorkflowRepairValidationService _repairValidation;
    private readonly bool _repairValidationAvailable;
    private readonly XamlSourcePropertyEditor _sourcePropertyEditor;
    private readonly XamlAutomationIdProposalService _xamlSourceProposalService;
    private readonly CSharpAutomationIdProposalService _csharpSourceProposalService;
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
    private int _workbenchRunStarting;
    private string? _workbenchRunStartingIdempotencyKey;
    private string? _activeWorkbenchRunId;
    private readonly SemaphoreSlim _flowAdmissionGate = new(1, 1);
    private readonly SemaphoreSlim _frameCreationGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PersistTransactionGates =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly object _cacheLock = new();
    // Lifetime cancellation source; cancelled in Dispose() so broker-mode WS proxies
    // (which never call Start() to create _cts) still see shutdown.
    private readonly CancellationTokenSource _lifetimeCts = new();
    private byte[]? _cachedScreenshot;
    private string? _cachedScreenshotElementId;
    private DateTime _screenshotCacheTime;
    private InspectorFrame? _latestFrame;
    private long _cacheGeneration;
    private readonly Dictionary<string, InspectorFrame> _frames = new(StringComparer.Ordinal);
    private readonly object _replayEvidenceGate = new();
    private readonly object _workbenchRunGate = new();
    private readonly Dictionary<string, WorkbenchRunCapability> _workbenchRunCapabilities =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkbenchEvidence> _workbenchEvidence =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkbenchRepairClassification> _workbenchRepairClassifications =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkbenchAgentHandoff> _workbenchAgentHandoffs =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkbenchApprovalConfirmation> _workbenchApprovalConfirmations =
        new(StringComparer.Ordinal);
    private PerformanceOwnership? _performanceOwnership;
    private byte[]? _lastReplayEvidence;
    private static readonly TimeSpan ScreenshotCacheDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan FrameReuseDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan FrameCacheDuration = TimeSpan.FromSeconds(10);
    private const int MaxCachedFrames = 32;
    private const int MaxScreenshotBytes = 64 * 1024 * 1024;
    private const long MaxCachedFrameBytes = 64L * 1024 * 1024;

    // Cap request bodies to avoid local DoS via huge POST payloads.
    private const long MaxRequestBodyBytes = 1_048_576; // 1 MB
    private const long MaxWorkflowFileBytes = 1_048_576;
    private const int MaxWorkflowFiles = 100;
    private const int MaxRetainedWorkbenchRuns = 16;
    private const int MaxRetainedWorkbenchEvidence = 8;
    private const int MaxRetainedWorkbenchRepairClassifications = 32;
    private const int MaxRetainedWorkbenchAgentHandoffs = 16;
    private const int MaxRetainedWorkbenchApprovalConfirmations = 64;
    private const int MaxCachedWorkbenchEvidenceBytes = 16 * 1024 * 1024;
    private const int MaxSelectorVerifyAmbiguityMatches = 20;

    public int Port => _port;

    /// <summary>
    /// Port of the underlying DevFlow agent this inspector is proxying to. Used by
    /// the broker to detect when an agent has reconnected on a different port and
    /// the cached InspectorServer's AgentClient is now pointing at a dead port.
    /// </summary>
    public int AgentPort => _agentPort;

    internal string? AgentInstanceId => _agentInstanceId;

    public InspectorServer(int port, string agentHost, int agentPort, string? embedToken = null)
        : this(
            port,
            agentHost,
            agentPort,
            embedToken,
            agentId: null,
            appName: null,
            platform: null,
            project: null,
            sessionId: null,
            previewFlags: null)
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
        RouteCheckpointCoordinator? checkpoints = null,
        AgentRegistration? checkpointRegistration = null,
        Func<
            Testing.MauiFlow,
            Func<AgentClient, Testing.IFlowReplayEvidenceCapture?>,
            WorkflowRunLeaseHandoff,
            CancellationToken,
            Task<Testing.FlowReplayReport>>? workflowReplay = null,
        string? agentInstanceId = null,
        InspectorWorkflowServices? workflowServices = null,
        IWorkflowRepairValidationHost? repairValidationHost = null,
        TestAgentSessionService? testAgentSessions = null,
        Func<
            Testing.MauiTestAgentTargetState,
            Task<Testing.MauiTestAgentTargetState?>>? testAgentTargetStateRefresh = null,
        Testing.MauiPreviewFeatureFlags? previewFlags = null,
        Func<string?, bool>? trustedHostApprovalVerifier = null)
    {
        _port = port;
        _agentHost = agentHost;
        _agentPort = agentPort;
        _embedToken = embedToken;
        _agentId = agentId;
        _agentInstanceId = agentInstanceId;
        _appName = appName;
        _platform = platform;
        _project = string.IsNullOrWhiteSpace(project) ? null : project;
        _sessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId;
        _previewFlags = ResolvePreviewFlags(previewFlags);
        _trustedHostApprovalVerifier = trustedHostApprovalVerifier;
        _checkpointRegistration = checkpointRegistration ?? new AgentRegistration
        {
            Id = agentId ?? $"inspector-{agentPort}",
            Project = project ?? $"inspector:{agentHost}:{agentPort}",
            Tfm = "unknown",
            AppName = appName ?? "",
            Platform = platform ?? "",
            Port = agentPort,
            SessionId = sessionId
        };
        _checkpoints = checkpoints ?? new RouteCheckpointCoordinator();
        _workflowReplay = workflowReplay;
        _workflowServices = workflowServices;
        _testAgentSessions = testAgentSessions;
        _testAgentTargetStateRefresh = testAgentTargetStateRefresh;
        _repairValidationAvailable = repairValidationHost is not null;
        _repairValidation = new WorkflowRepairValidationService(
            repairValidationHost ?? UnavailableWorkflowRepairValidationHost.Instance);
        _sourcePropertyEditor = new XamlSourcePropertyEditor(project, sessionId);
        _xamlSourceProposalService = new XamlAutomationIdProposalService(project, sessionId);
        _csharpSourceProposalService = new CSharpAutomationIdProposalService(project);
        _alertController = new InspectorAlertController(agentHost, agentPort, appName, platform);
        _client = new AgentClient(agentHost, agentPort)
        {
            MutationLeaseHolderKind = "web-inspector",
            MutationLeaseLabel = "DevFlow Web Inspector"
        };
    }

    internal static Testing.MauiPreviewFeatureFlags ResolvePreviewFlags(
        Testing.MauiPreviewFeatureFlags? supplied,
        Func<string, string?>? readEnvironment = null)
        => supplied ??
           (readEnvironment is null
               ? Testing.MauiPreviewFeatureFlagConfiguration.FromEnvironment()
               : Testing.MauiPreviewFeatureFlagConfiguration.FromEnvironment(readEnvironment));

    private void InvalidateScreenshotCache()
    {
        lock (_cacheLock)
        {
            _cachedScreenshot = null;
            _cachedScreenshotElementId = null;
            _latestFrame = null;
            _cacheGeneration++;
        }
    }

    internal string? OwnedPerformanceSessionId
        => Volatile.Read(ref _performanceOwnership)?.SessionId;

    internal string? OwnedPerformanceStopToken
        => Volatile.Read(ref _performanceOwnership)?.StopToken;

    internal void SetPerformanceOwnership(
        string sessionId,
        string stopToken,
        string leaseId,
        string holderKind,
        string? holderLabel)
        => Volatile.Write(
            ref _performanceOwnership,
            new PerformanceOwnership(
                sessionId,
                stopToken,
                leaseId,
                holderKind,
                holderLabel));

    internal bool TryClearPerformanceOwnership(string sessionId, string stopToken)
    {
        var ownership = Volatile.Read(ref _performanceOwnership);
        return ownership is not null &&
            string.Equals(ownership.SessionId, sessionId, StringComparison.Ordinal) &&
            string.Equals(ownership.StopToken, stopToken, StringComparison.Ordinal) &&
            TryClearPerformanceOwnership(ownership);
    }

    private bool TryClearPerformanceOwnership(PerformanceOwnership ownership)
        => ReferenceEquals(
            Interlocked.CompareExchange(ref _performanceOwnership, null, ownership),
            ownership);

    private sealed record PerformanceOwnership(
        string SessionId,
        string StopToken,
        string LeaseId,
        string HolderKind,
        string? HolderLabel);

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
            byte[]? bodyBytes = null;
            if (method == "POST" && context.Request.HasEntityBody)
            {
                var isArtifactImport = string.Equals(
                    path.TrimEnd('/'),
                    "/api/workbench/artifacts/import",
                    StringComparison.OrdinalIgnoreCase);
                var kind = context.Request.QueryString["kind"];
                var maximum = string.Equals(kind, ArtifactTrustImportKinds.FlowRun, StringComparison.Ordinal)
                    ? ArtifactTrustImportService.MaxFlowRunBytes
                    : string.Equals(kind, ArtifactTrustImportKinds.Evidence, StringComparison.Ordinal)
                        ? (int)Evidence.EvidenceFormat.MaxBundleFileBytes
                        : 0;

                if (isArtifactImport && maximum > 0)
                {
                    bodyBytes = await ReadBoundedBytesAsync(
                        context.Request.InputStream,
                        context.Request.ContentLength64,
                        maximum,
                        _lifetimeCts.Token);
                    if (bodyBytes is null)
                    {
                        context.Response.StatusCode = 413;
                        context.Response.Close();
                        return;
                    }
                }
                else
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
            }

            var request = new HttpRequestInfo
            {
                Method = method,
                Path = path,
                Body = body,
                BodyBytes = bodyBytes,
                // Carry the read-token header through to RouteAsync so the data-tab gate works in
                // broker mode too (the standalone path already parses all headers). The owner-only
                // approval token has to come through here as well: it is the sole way a
                // confirmation capability can be minted, and dropping it silently disables every
                // approval decision behind an error that reads like a transient broker restart.
                Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["x-devflow-inspector-token"] = context.Request.Headers["X-DevFlow-Inspector-Token"] ?? "",
                    ["x-devflow-host-approval-token"] = context.Request.Headers["X-DevFlow-Host-Approval-Token"] ?? "",
                    ["x-devflow-writer"] = context.Request.Headers["X-DevFlow-Writer"] ?? "",
                    ["x-devflow-lease"] = context.Request.Headers["X-DevFlow-Lease"] ?? "",
                    ["x-devflow-holder"] = context.Request.Headers["X-DevFlow-Holder"] ?? "",
                    ["x-devflow-label"] = context.Request.Headers["X-DevFlow-Label"] ?? "",
                },
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
        await StopOwnedPerformanceSessionAsync().ConfigureAwait(false);
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

        try
        {
            Task.Run(StopOwnedPerformanceSessionAsync)
                .Wait(TimeSpan.FromSeconds(2));
        }
        catch { }
        try { _lifetimeCts.Cancel(); } catch { }
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _cts?.Dispose(); } catch { }
        try { _lifetimeCts.Dispose(); } catch { }
        try { _flowAdmissionGate.Dispose(); } catch { }
        try { _frameCreationGate.Dispose(); } catch { }
        try { _client.Dispose(); } catch { }
    }

    private async Task StopOwnedPerformanceSessionAsync()
    {
        var ownership = Volatile.Read(ref _performanceOwnership);
        if (ownership is null)
            return;
        try
        {
            using var leaseScope = _client.UseMutationLease(
                ownership.LeaseId,
                ownership.HolderKind,
                ownership.HolderLabel);
            var stopped = await _client.StopProfilerAsync(
                    ownership.SessionId,
                    ownership.StopToken)
                .WaitAsync(TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);
            if (stopped?.IsActive == false)
                TryClearPerformanceOwnership(ownership);
        }
        catch
        {
            // Teardown is best-effort, but the exact id prevents touching an external session.
        }
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
            var brokerRunActive = Volatile.Read(ref _workbenchRunStarting) == 1 || HasActiveWorkbenchRun();
            if (request.Method == "POST" &&
                (Volatile.Read(ref _replayInProgress) == 1 || brokerRunActive) &&
                IsBlockedDuringReplay(request.Path))
            {
                return (409, "application/json", Encoding.UTF8.GetBytes(
                    "{\"ok\":false,\"error\":\"A workflow run is in progress — try again when it finishes.\"}"));
            }

            // Data tabs expose more than the visible tree (network/preferences/logs/files), so gate
            // them on the per-inspector read token that only same-origin devflow.js can echo back.
            if (IsTokenGatedPath(request.Path))
            {
                var token = request.Headers.TryGetValue("x-devflow-inspector-token", out var t) ? t : null;
                var normalizedPath = request.Path.TrimEnd('/');
                var hostTokenVerified = IsTrustedHostApprovalRequest(request);
                // Minting a confirmation capability is owner-token-only: the browser read token is
                // never sufficient, because it is echoed by any same-origin page.
                var trustedHostConfirmationIssue =
                    request.Method == "POST" &&
                    string.Equals(
                        normalizedPath,
                        "/api/workbench/approval-confirmations/issue",
                        StringComparison.OrdinalIgnoreCase) &&
                    hostTokenVerified;
                // Approve verifies the capability itself further down, so it does not need the read
                // token; a caller without a matching capability is refused there regardless.
                var consumesTrustedHostConfirmation =
                    request.Method == "POST" &&
                    normalizedPath.StartsWith(
                        "/api/workbench/agent-requests/",
                        StringComparison.OrdinalIgnoreCase) &&
                    normalizedPath.EndsWith("/approve", StringComparison.OrdinalIgnoreCase);
                // Listing and rejecting carry no later capability check, so the owner token itself
                // must be verified here. Neither can mint authority: reject only closes a request.
                var trustedHostAgentRequestReview =
                    hostTokenVerified &&
                    ((request.Method == "GET" &&
                      string.Equals(
                          normalizedPath,
                          "/api/workbench/agent-requests",
                          StringComparison.OrdinalIgnoreCase)) ||
                     (request.Method == "POST" &&
                      normalizedPath.StartsWith(
                          "/api/workbench/agent-requests/",
                          StringComparison.OrdinalIgnoreCase) &&
                      normalizedPath.EndsWith("/reject", StringComparison.OrdinalIgnoreCase)));
                if (!trustedHostConfirmationIssue &&
                    !consumesTrustedHostConfirmation &&
                    !trustedHostAgentRequestReview &&
                    (string.IsNullOrEmpty(token) || !string.Equals(token, _readToken, StringComparison.Ordinal)))
                    return (403, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"forbidden\"}"));
            }

            // Workbench run and trust routes are intentionally thin adapters over broker-owned
            // services. They never fall back to the legacy direct replay path.
            if (request.Path.StartsWith("/api/workbench/", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsPreviewRouteEnabled(_previewFlags, request.Path))
                {
                    return JsonResponse(404, new
                    {
                        ok = false,
                        error = "This preview capability is disabled."
                    });
                }

                if (request.Method == "POST" &&
                    string.Equals(
                        request.Path.TrimEnd('/'),
                        "/api/workbench/run/start",
                        StringComparison.OrdinalIgnoreCase) &&
                    Volatile.Read(ref _replayInProgress) == 1)
                {
                    return JsonResponse(409, new
                    {
                        ok = false,
                        error = "A legacy replay is in progress. Wait for it to finish before starting a broker workflow run."
                    });
                }
                return await HandleWorkbenchRouteAsync(request, leaseId, holderKind, holderLabel);
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
                    var payload = JsonSerializer.Serialize(new
                    {
                        ok = false,
                        reason = "writer",
                        error = "Another session is driving this app.",
                        holderKind = status.HolderKind,
                        label = status.Label,
                        expiresInMs = status.ExpiresInMs,
                        authority = status.Authority
                    }, CamelCase);
                    return (409, "application/json", Encoding.UTF8.GetBytes(payload));
                }
            }

            return request.Method switch
            {
                "GET" => await HandleGetRouteAsync(request),
                "POST" => await HandlePostRouteAsync(request, leaseId, holderKind, holderLabel),
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
            .Append(BuildMeta("devflow-agent-instance-id", _agentInstanceId))
            .Append(BuildMeta("devflow-app-name", _appName))
            .Append(BuildMeta("devflow-platform", _platform))
            .Append(BuildMeta("devflow-agent-port", _agentPort.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .Append(BuildPreviewFlagMeta())
            .ToString();
        html = html.Contains("</head>", StringComparison.Ordinal)
            ? html.Replace("</head>", tokenMeta + agentMeta + "</head>")
            : tokenMeta + agentMeta + html;
        return (200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
    }

    /// <summary>
    /// Publishes the broker's preview flags to the page so the browser hides what the server would
    /// refuse anyway. This is presentation only: every preview route re-checks the same flags
    /// server-side, so a page that forges a meta tag gains nothing but a 404.
    /// </summary>
    /// <remarks>
    /// Only "true" is emitted. An absent meta reads as disabled in <c>devflow.js</c>, so the
    /// default page — no flags set — shows the durable Inspector surface and nothing else.
    /// </remarks>
    private string BuildPreviewFlagMeta()
    {
        var builder = new StringBuilder();
        foreach (var (name, feature) in PreviewFlagMetaNames)
        {
            if (_previewFlags.IsEnabled(feature))
                builder.Append(BuildMeta(name, "true"));
        }
        foreach (var surface in OptionalSurfaces)
        {
            if (surface.Served)
                builder.Append(BuildMeta(surface.MetaName, "true"));
        }
        return builder.ToString();
    }

    private static readonly (string MetaName, string Feature)[] PreviewFlagMetaNames =
    [
        ("devflow-preview-workbench", "workbench"),
        ("devflow-preview-agent-authoring", "agent-authoring"),
        ("devflow-preview-repair", "repair-proposals"),
        ("devflow-preview-source", "source-proposals"),
        ("devflow-preview-trace-import", "trace-import-export"),
    ];

    private static string BuildMeta(string name, string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : $"<meta name=\"{name}\" content=\"{WebUtility.HtmlEncode(value)}\">";

    /// <summary>
    /// Returns JSON state for AJAX polling: screenshot (as timestamped URL) + element divs HTML.
    /// This avoids full page reload flash.
    /// </summary>
    private async Task<(int, string, byte[])> HandleStateAsync()
    {
        var frame = await CreateFrameAsync();
        if (frame.Tree.Count == 0 && frame.Png.Length == 0)
        {
            return (503, "application/json", Encoding.UTF8.GetBytes(
                "{\"ok\":false,\"error\":\"The DevFlow agent is unavailable.\"}"));
        }

        var json = JsonSerializer.Serialize(new
        {
            frameId = frame.Id,
            screenshotUrl = $"screenshot.png?frame={Uri.EscapeDataString(frame.Id)}",
            elements = frame.ElementsHtml,
            viewportWidth = frame.Width,
            viewportHeight = frame.Height,
            rootOffsetX = frame.RootOffsetX,
            rootOffsetY = frame.RootOffsetY
        });

        return (200, "application/json", Encoding.UTF8.GetBytes(json));
    }

    private async Task<(int, string, byte[])> HandleInspectSnapshotAsync()
    {
        var frame = await CreateFrameAsync();
        if (frame.Tree.Count == 0 && frame.Png.Length == 0)
        {
            return JsonResponse(503, new
            {
                ok = false,
                error = new
                {
                    code = "agent-unavailable",
                    message = "The DevFlow agent is unavailable.",
                    retriable = true
                }
            });
        }

        return JsonResponse(200, InspectorSnapshotService.Create(
            frame.Id,
            frame.CreatedUtc,
            $"screenshot.png?frame={Uri.EscapeDataString(frame.Id)}",
            frame.Tree,
            frame.Width,
            frame.Height,
            frame.RootOffsetX,
            frame.RootOffsetY,
            _agentId,
            _appName,
            _platform));
    }

    private async Task<(int, string, byte[])> HandleInspectQueryAsync(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return JsonResponse(400, new
            {
                ok = false,
                error = new { code = "invalid-argument", message = "A query body is required.", retriable = false }
            });
        }

        var type = ReadStringField(body, "type");
        var automationId = ReadStringField(body, "automationId");
        var text = ReadStringField(body, "text");
        var selector = ReadStringField(body, "selector");
        if (new[] { type, automationId, text, selector }.All(string.IsNullOrWhiteSpace))
        {
            return JsonResponse(400, new
            {
                ok = false,
                error = new
                {
                    code = "invalid-argument",
                    message = "Provide type, automationId, text, or selector.",
                    retriable = false
                }
            });
        }
        if (new[] { type, automationId, text, selector }.Any(value => value?.Length > 1024))
        {
            return JsonResponse(400, new
            {
                ok = false,
                error = new { code = "invalid-argument", message = "Query values cannot exceed 1024 characters.", retriable = false }
            });
        }
        if (!string.IsNullOrWhiteSpace(selector) &&
            new[] { type, automationId, text }.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            return JsonResponse(400, new
            {
                ok = false,
                error = new
                {
                    code = "invalid-argument",
                    message = "selector cannot be combined with type, automationId, or text.",
                    retriable = false
                }
            });
        }

        List<ElementInfo> candidates;
        try
        {
            candidates = !string.IsNullOrWhiteSpace(selector)
                ? await _client.QueryCssAsync(selector)
                : await _client.QueryAsync(type, automationId, text);
        }
        catch (InvalidOperationException)
        {
            return JsonResponse(400, new
            {
                ok = false,
                error = new { code = "invalid-argument", message = "The selector is invalid.", retriable = false }
            });
        }

        var frame = await CreateFrameAsync();
        return JsonResponse(200, new InspectorQueryResponse
        {
            Projection = "activeVisual",
            SnapshotId = frame.Id,
            Revision = frame.Id,
            Elements = InspectorSnapshotService.FilterActiveMatches(frame.Tree, candidates)
        });
    }

    private async Task<(int, string, byte[])> HandleEventSupportAsync()
    {
        var capabilities = await _client.GetCapabilitiesAsync();
        return Ok(JsonSerializer.Serialize(new { supported = SupportsUiEvents(capabilities) }, CamelCase));
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

    internal static List<ElementInfo> SelectFrameTree(List<ElementInfo> tree, string? rootPageId)
    {
        if (rootPageId is null || tree.Count == 0)
            return tree;
        var rootPage = tree[0].Children?.FirstOrDefault(child => child.Id == rootPageId);
        return rootPage is null ? tree : [rootPage];
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
        if (png is null)
            png = (await CreateFrameAsync()).Png;
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

                var tree = await _client.GetTreeAsync();
                var rootPageId = FindRootPageId(tree);
                var (rootOffsetX, rootOffsetY) = GetRootPageOffset(tree, rootPageId);
                var frameTree = PageFilter.ProjectActiveVisualInPlace(SelectFrameTree(tree, rootPageId));
                var screenshot = await GetCachedScreenshotAsync(rootPageId) ?? Array.Empty<byte>();
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
                    HtmlRenderer.RenderElements(frameTree, 1, rootOffsetX, rootOffsetY));
                lock (_cacheLock)
                {
                    if (generation != _cacheGeneration)
                    {
                        if (attempt == 0) continue;
                        throw new InvalidOperationException("Inspector state changed repeatedly while capturing a frame.");
                    }

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

    private async Task<byte[]?> GetCachedScreenshotAsync(string? elementId = null)
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
                && DateTime.UtcNow - _screenshotCacheTime < ScreenshotCacheDuration)
                return _cachedScreenshot;
            generation = _cacheGeneration;
        }

        var fresh = await _client.ScreenshotAsync(elementId: elementId);
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
        if (!TryParseHitTestElements(hitResult, out var hitDocument, out var elements))
            return Ok("{\"ok\":true,\"elementId\":null,\"candidates\":[]}");

        using (hitDocument)
        {
            var candidates = new List<Dictionary<string, string?>>(Math.Min(12, elements.GetArrayLength()));
            for (var index = 0; index < elements.GetArrayLength() && candidates.Count < 12; index++)
            {
                var element = elements[index];
                if (!element.TryGetProperty("id", out var idProperty) || idProperty.ValueKind != JsonValueKind.String)
                    continue;
                var id = idProperty.GetString();
                if (string.IsNullOrEmpty(id)) continue;

                candidates.Add(new Dictionary<string, string?>
                {
                    ["id"] = id,
                    ["type"] = ReadOptionalString(element, "type"),
                    ["automationId"] = ReadOptionalString(element, "automationId"),
                    ["text"] = ReadOptionalString(element, "text")
                });
            }

            var payload = JsonSerializer.Serialize(new
            {
                ok = true,
                elementId = candidates.FirstOrDefault()?["id"],
                candidates
            }, CamelCase);
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
        var requestedElementId =
            root.TryGetProperty("elementId", out var requestedElementProperty) &&
            requestedElementProperty.ValueKind == JsonValueKind.String
                ? requestedElementProperty.GetString()
                : null;

        if (!string.IsNullOrEmpty(requestedElementId))
        {
            var success = await _client.TapAsync(requestedElementId);
            if (success)
                InvalidateScreenshotCache();
            return TapProxyResponse(
                success,
                requestedElementId,
                success ? null : "The selected element did not accept the tap.",
                attemptedCandidates: 1);
        }

        // Coordinates are already translated into window space by devflow.js using the frame's
        // root offsets, so every browser tab acts against the exact frame it rendered.
        if (root.TryGetProperty("x", out var xProp) && root.TryGetProperty("y", out var yProp))
        {
            var x = xProp.GetDouble();
            var y = yProp.GetDouble();

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
                        var attemptedCandidates = 0;
                        // Try elements from most specific to most general until one accepts tap
                        for (int i = 0; i < elements.GetArrayLength(); i++)
                        {
                            if (!elements[i].TryGetProperty("id", out var idProp)) continue;
                            var elementId = idProp.GetString();
                            if (!string.IsNullOrEmpty(elementId))
                            {
                                attemptedCandidates++;
                                var success = await _client.TapAsync(elementId);
                                if (success)
                                {
                                    InvalidateScreenshotCache();
                                    return TapProxyResponse(
                                        true,
                                        elementId,
                                        reason: null,
                                        attemptedCandidates: attemptedCandidates);
                                }
                            }
                        }
                        return TapProxyResponse(
                            false,
                            elementId: null,
                            reason: "No active element at the coordinates accepted the tap.",
                            attemptedCandidates: attemptedCandidates);
                    }
                }
            }
            return TapProxyResponse(
                false,
                elementId: null,
                reason: "No tappable element was found at the coordinates.",
                attemptedCandidates: 0);
        }

        return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"x/y or elementId required\"}"));
    }

    private static (int, string, byte[]) TapProxyResponse(
        bool ok,
        string? elementId,
        string? reason,
        int attemptedCandidates)
        => Ok(JsonSerializer.Serialize(new
        {
        ok,
        elementId,
        reason,
        error = ok ? null : new { code = "tap-rejected", message = reason },
        attemptedCandidates
        }, CamelCase));

    private async Task<(int, string, byte[])> HandleProxyScrollAsync(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Body required\"}"));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var deltaX = 0d;
        var deltaY = 0d;
        if (root.TryGetProperty("deltaX", out var dxProperty) &&
            (dxProperty.ValueKind != JsonValueKind.Number || !dxProperty.TryGetDouble(out deltaX)))
        {
            return BadRequest("deltaX must be a number");
        }
        if (root.TryGetProperty("deltaY", out var dyProperty) &&
            (dyProperty.ValueKind != JsonValueKind.Number || !dyProperty.TryGetDouble(out deltaY)))
        {
            return BadRequest("deltaY must be a number");
        }
        var requestedElementId = root.TryGetProperty("elementId", out var elementIdProperty) &&
            elementIdProperty.ValueKind == JsonValueKind.String
            ? elementIdProperty.GetString()
            : null;
        int? itemIndex = null;
        if (root.TryGetProperty("itemIndex", out var itemIndexProperty))
        {
            if (itemIndexProperty.ValueKind != JsonValueKind.Number ||
                !itemIndexProperty.TryGetInt32(out var parsedItemIndex))
            {
                return BadRequest("itemIndex must be an integer");
            }
            itemIndex = parsedItemIndex;
        }
        var scrollToPosition = root.TryGetProperty("scrollToPosition", out var positionProperty) &&
            positionProperty.ValueKind == JsonValueKind.String
            ? positionProperty.GetString()
            : null;
        var animated = !root.TryGetProperty("animated", out var animatedProperty) ||
            animatedProperty.ValueKind != JsonValueKind.False;
        var hasX = root.TryGetProperty("x", out var xProperty);
        var hasY = root.TryGetProperty("y", out var yProperty);
        var validationError = ValidateInspectScrollArguments(
            requestedElementId,
            itemIndex,
            scrollToPosition,
            hasX,
            hasY,
            deltaX,
            deltaY);
        if (validationError is not null)
            return BadRequest(validationError);

        if (!string.IsNullOrWhiteSpace(requestedElementId) || itemIndex.HasValue || !string.IsNullOrWhiteSpace(scrollToPosition))
        {
            var success = await _client.ScrollAsync(
                requestedElementId,
                deltaX,
                deltaY,
                animated,
                itemIndex: itemIndex,
                scrollToPosition: scrollToPosition);
            InvalidateScreenshotCache();
            return success
                ? (200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"))
                : (500, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false}"));
        }

        // Coordinates are already in window space (translated client-side from the rendered frame).
        if (hasX && hasY)
        {
            if (xProperty.ValueKind != JsonValueKind.Number || yProperty.ValueKind != JsonValueKind.Number)
                return BadRequest("x and y must be numbers");
            var hitResult = await _client.HitTestAsync(xProperty.GetDouble(), yProperty.GetDouble());
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

    internal static string? ValidateInspectScrollArguments(
        string? elementId,
        int? itemIndex,
        string? scrollToPosition,
        bool hasX,
        bool hasY,
        double deltaX,
        double deltaY)
    {
        if (hasX != hasY)
            return "x and y must be provided together";
        if (hasX && (!string.IsNullOrWhiteSpace(elementId) ||
            itemIndex.HasValue ||
            !string.IsNullOrWhiteSpace(scrollToPosition)))
        {
            return "coordinate scrolling cannot be combined with element or item scrolling";
        }
        if (itemIndex is < 0 or > 1_000_000)
            return "itemIndex must be between 0 and 1000000";
        if (!string.IsNullOrWhiteSpace(scrollToPosition) &&
            scrollToPosition.ToLowerInvariant() is not ("start" or "center" or "end" or "makevisible"))
        {
            return "scrollToPosition must be Start, Center, End, or MakeVisible";
        }
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY) ||
            Math.Abs(deltaX) > 1_000_000 || Math.Abs(deltaY) > 1_000_000)
        {
            return "scroll deltas are outside the supported range";
        }
        return null;
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
        string ElementsHtml);

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
        var payload = JsonSerializer.Serialize(new { ok = true, value });
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

        var descriptors = properties.EnumerateArray().Select(property =>
        {
            var node = JsonNode.Parse(property.GetRawText())!.AsObject();
            var name = property.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            node["persistable"] = !string.IsNullOrWhiteSpace(name) &&
                XamlSourcePropertyEditor.IsSupportedPropertyName(name);
            return node;
        }).ToArray();
        return Ok(JsonSerializer.Serialize(new { ok = true, supported = true, properties = descriptors }, CamelCase));
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
        var allowUnsafe = root.TryGetProperty("allowUnsafe", out var allowUnsafeProperty)
            && allowUnsafeProperty.ValueKind == JsonValueKind.True;

        // Only scalar JSON is a valid property value. Number/bool raw text is culture-invariant.
        if (!TryReadScalarField(root, "value", out var value))
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"value must be a string, number, or boolean\"}"));
        if (!IsValidPropertyRef(elementId, name) || value is null)
            return (400, "application/json", Encoding.UTF8.GetBytes("{\"error\":\"elementId, name and value required\"}"));

        var result = await _client.SetPropertyDetailedAsync(
            elementId!,
            name!,
            value,
            allowUnsafe);
        if (result.Success)
            InvalidateScreenshotCache();

        var payload = JsonSerializer.Serialize(new
        {
            ok = result.Success,
            value = result.Value,
            valueSource = result.ValueSource,
            mutationSafety = result.MutationSafety,
            warning = result.Warning,
            error = result.Error
        }, CamelCase);
        return (
            result.Success ? 200 : (result.Warning is null ? 400 : 409),
            "application/json",
            Encoding.UTF8.GetBytes(payload));
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
        var payload = JsonSerializer.Serialize(new
        {
            ok = result.Success,
            error = result.Error,
            file = result.File,
            line = result.Line,
            column = result.Column,
            sourceHash = result.SourceHash
        }, CamelCase);
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
            var payload = JsonSerializer.Serialize(new
            {
                ok = result.Ok,
                recordingId = result.RecordingId,
                name = result.Name ?? name,
                steps = result.Steps,
                route,
                error = result.Error
            }, CamelCase);
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

        var result = string.Equals(action, Testing.FlowActions.Assert, StringComparison.OrdinalIgnoreCase)
            ? await _client.ObserveMutationRecordingAsync(new MutationRecordingObservation
            {
                Action = Testing.FlowActions.Assert,
                AssertsJson = assertsJson
            }, recordingId)
            : await _client.ControlMutationRecordingAsync(
                "status", null, null, null, null, recordingId);
        var payload = JsonSerializer.Serialize(new
        {
            ok = result.Ok,
            seq = result.Seq ?? result.Steps,
            stepCount = result.Steps,
            fragile = result.Fragile,
            error = result.Error
        }, CamelCase);
        return (result.Ok ? 200 : 400, "application/json", Encoding.UTF8.GetBytes(payload));
    }

    private async Task<(int, string, byte[])> HandleFlowRecordStopAsync(string? body)
    {
        var recordingId = ReadStringField(body, "recordingId");
        var result = await _client.ControlMutationRecordingAsync("stop", null, null, null, null, recordingId);
        if (!result.Ok && result.Empty)
        {
            var cancelled = await _client.ControlMutationRecordingAsync("cancel-if-empty", null, null, null, null, recordingId);
            var emptyPayload = JsonSerializer.Serialize(new
            {
                ok = cancelled.Ok && cancelled.Empty,
                empty = cancelled.Empty,
                steps = 0,
                error = cancelled.Ok ? null : cancelled.Error ?? result.Error
            }, CamelCase);
            return (cancelled.Ok && cancelled.Empty ? 200 : 409, "application/json", Encoding.UTF8.GetBytes(emptyPayload));
        }

        var payload = JsonSerializer.Serialize(new
        {
            ok = result.Ok,
            markdown = result.Markdown,
            name = result.Name,
            steps = result.Steps,
            warnings = result.Warnings,
            error = result.Error
        }, CamelCase);
        return (result.Ok ? 200 : 400, "application/json", Encoding.UTF8.GetBytes(payload));
    }

    private async Task<(int, string, byte[])> HandleFlowRecordStatusAsync(string? body)
    {
        var recordingId = ReadStringField(body, "recordingId");
        var result = await _client.ControlMutationRecordingAsync(
            "status", null, null, null, null, recordingId);
        var payload = JsonSerializer.Serialize(new
        {
            ok = result.Ok,
            recording = result.Recording,
            recordingId = result.RecordingId,
            name = result.Name,
            steps = result.Steps,
            error = result.Error
        }, CamelCase);
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
        var payload = JsonSerializer.Serialize(new
        {
            ok = result.Ok,
            recording = result.Recording,
            recordingId = result.RecordingId,
            error = result.Error
        }, CamelCase);
        return (result.Ok ? 200 : 400, "application/json", Encoding.UTF8.GetBytes(payload));
    }

    // Replays a recorded flow through the broker-owned coordinator when this Inspector is broker
    // hosted. Standalone Inspectors retain the public compatibility runner for existing direct use.
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private async Task<(int, string, byte[])> HandleFlowFilesListAsync()
    {
        var root = await ResolveWorkflowRootAsync();
        if (root is null)
        {
            return JsonResponse(200, new
            {
                ok = true,
                supported = false,
                tests = Array.Empty<object>(),
                error = "The registered app project could not be resolved. Use Choose file instead."
            });
        }

        if (!Directory.Exists(root))
            return JsonResponse(200, new { ok = true, supported = true, tests = Array.Empty<object>() });

        try
        {
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                return JsonResponse(403, new { ok = false, error = "The project workflow directory cannot be a symbolic link or reparse point." });

            var tests = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                .Where(path => string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path))
                .Where(file =>
                    (file.Attributes & FileAttributes.ReparsePoint) == 0 &&
                    file.Length <= MaxWorkflowFileBytes)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxWorkflowFiles)
                .Select(file => new
                {
                    name = file.Name,
                    size = file.Length,
                    modifiedAt = file.LastWriteTimeUtc
                })
                .ToArray();
            return JsonResponse(200, new { ok = true, supported = true, tests });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return JsonResponse(500, new { ok = false, error = "Could not list project workflow tests." });
        }
    }

    private async Task<(int, string, byte[])> HandleFlowFileLoadAsync(string? body)
    {
        if (!TryReadWorkflowFileName(body, out var name, out var requestError))
            return JsonResponse(400, new { ok = false, error = requestError });

        var root = await ResolveWorkflowRootAsync();
        if (root is null)
            return JsonResponse(400, new { ok = false, error = "The registered app project could not be resolved. Use Choose file instead." });

        string path;
        try
        {
            path = Path.GetFullPath(Path.Combine(root, name!));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return JsonResponse(400, new { ok = false, error = "The workflow filename is invalid." });
        }

        if (!InspectorSourcePathSafety.IsUnderRoot(path, root) ||
            !string.Equals(Path.GetFileName(path), name, StringComparison.Ordinal) ||
            !string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase))
        {
            return JsonResponse(403, new { ok = false, error = "Only top-level Markdown files in the project maui-tests directory can be loaded." });
        }

        try
        {
            if (!File.Exists(path))
                return JsonResponse(404, new { ok = false, error = "The selected workflow test no longer exists." });
            var info = new FileInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                InspectorSourcePathSafety.PathContainsReparsePoint(root, path))
            {
                return JsonResponse(403, new { ok = false, error = "Workflow tests reached through symbolic links or reparse points cannot be loaded." });
            }
            if (info.Length > MaxWorkflowFileBytes)
                return JsonResponse(413, new { ok = false, error = "Workflow test files larger than 1 MB cannot be loaded." });

            var markdown = await File.ReadAllTextAsync(path, _lifetimeCts.Token);
            var parsed = Testing.FlowMarkdown.Parse(markdown, path);
            if (!parsed.Ok || parsed.Flow is null)
                return JsonResponse(400, new { ok = false, error = parsed.Error ?? "Could not parse the workflow test." });
            if (parsed.Flow.Steps.Count > MaxReplaySteps)
                return JsonResponse(400, new { ok = false, error = $"Flow too large (max {MaxReplaySteps} steps)." });
            var validation = Testing.FlowValidator.Validate(parsed.Flow);
            if (!validation.Ok)
            {
                return JsonResponse(400, new
                {
                    ok = false,
                    error = "Flow failed validation.",
                    errors = validation.Errors,
                    warnings = validation.Warnings
                });
            }

            var migratedLegacySchema = markdown.Contains("\"schemaVersion\"", StringComparison.Ordinal);
            var normalizedMarkdown = migratedLegacySchema
                ? Testing.FlowMarkdown.ReplaceAuthoritativePayload(markdown, parsed.Flow) ?? markdown
                : markdown;

            return JsonResponse(200, new
            {
                ok = true,
                name,
                markdown = normalizedMarkdown,
                steps = parsed.Flow.Steps.Count,
                warnings = migratedLegacySchema
                    ? validation.Warnings.Concat([
                        "Normalized a legacy schemaVersion payload in the loaded draft. Save the test to persist the migrated schema field."
                    ])
                    : validation.Warnings
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return JsonResponse(500, new { ok = false, error = "Could not read the selected workflow test." });
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
            var projectRoot = InspectorSourcePathSafety.FindProjectRoot(sourceFile, _project, _sessionId);
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

    // ── Human authoring: plan sidecars and explicit flow+plan bundle commits ──

    private async Task<WorkflowPlanStore?> ResolveWorkflowPlanStoreAsync()
    {
        var workflowRoot = await ResolveWorkflowRootAsync();
        var projectRoot = workflowRoot is null ? null : Path.GetDirectoryName(workflowRoot);
        return string.IsNullOrWhiteSpace(projectRoot) ? null : new WorkflowPlanStore(projectRoot);
    }

    private async Task<(int, string, byte[])> HandlePlanListAsync()
    {
        var store = await ResolveWorkflowPlanStoreAsync();
        if (store is null)
        {
            return JsonResponse(200, new
            {
                ok = true,
                supported = false,
                error = "Workspace persistence is unavailable. Download the plan bundle or use a host bridge."
            });
        }
        return AuthoringResponse(store.List());
    }

    private async Task<(int, string, byte[])> HandlePlanLoadAsync(string? body)
    {
        if (!TryReadAuthoringRequest(body, out var request, out var error))
            return JsonResponse(400, new { ok = false, error });
        var store = await ResolveWorkflowPlanStoreAsync();
        if (store is null)
            return JsonResponse(200, new { ok = true, supported = false, error = "Workspace persistence is unavailable." });
        return AuthoringResponse(store.Load(request.FlowName));
    }

    private async Task<(int, string, byte[])> HandlePlanValidateAsync(string? body)
    {
        if (!TryReadAuthoringRequest(body, out var request, out var error))
            return JsonResponse(400, new { ok = false, error });
        var store = await ResolveWorkflowPlanStoreAsync();
        if (store is null)
            return JsonResponse(200, new { ok = true, supported = false, error = "Workspace persistence is unavailable." });
        var current = store.Load(request.FlowName);
        var markdown = request.Markdown ?? current.Snapshot?.Markdown;
        return AuthoringResponse(store.Validate(request.FlowName, markdown, request.PlanJson));
    }

    private async Task<(int, string, byte[])> HandlePlanSaveAsync(string? body)
    {
        if (!TryReadAuthoringRequest(body, out var request, out var error))
            return JsonResponse(400, new { ok = false, error });
        var store = await ResolveWorkflowPlanStoreAsync();
        if (store is null)
            return JsonResponse(200, new { ok = true, supported = false, error = "Workspace persistence is unavailable." });
        return AuthoringResponse(store.Save(request));
    }

    private async Task<(int, string, byte[])> HandleFlowValidateAsync(string? body)
    {
        if (!TryReadAuthoringRequest(body, out var request, out var error))
            return JsonResponse(400, new { ok = false, error });
        var store = await ResolveWorkflowPlanStoreAsync();
        if (store is null)
            return JsonResponse(200, new { ok = true, supported = false, error = "Workspace persistence is unavailable." });
        var current = store.Load(request.FlowName);
        var markdown = request.Markdown ?? current.Snapshot?.Markdown;
        return AuthoringResponse(store.Validate(request.FlowName, markdown, request.PlanJson));
    }

    private async Task<(int, string, byte[])> HandleFlowDiffAsync(string? body)
    {
        if (!TryReadAuthoringRequest(body, out var request, out var error))
            return JsonResponse(400, new { ok = false, error });
        var store = await ResolveWorkflowPlanStoreAsync();
        if (store is null)
            return JsonResponse(200, new { ok = true, supported = false, error = "Workspace persistence is unavailable." });
        var current = store.Load(request.FlowName);
        var markdown = request.Markdown ?? current.Snapshot?.Markdown;
        return AuthoringResponse(store.Diff(request.FlowName, markdown, request.PlanJson));
    }

    private async Task<(int, string, byte[])> HandleFlowCommitAsync(string? body)
    {
        if (!TryReadAuthoringRequest(body, out var request, out var error))
            return JsonResponse(400, new { ok = false, error });
        if (string.IsNullOrWhiteSpace(request.Markdown))
            return JsonResponse(400, new { ok = false, error = "markdown is required for an explicit flow commit." });
        var store = await ResolveWorkflowPlanStoreAsync();
        if (store is null)
            return JsonResponse(200, new { ok = true, supported = false, error = "Workspace persistence is unavailable." });
        var commit = new WorkflowBundleCommitRequest
        {
            FlowName = request.FlowName,
            Markdown = request.Markdown,
            PlanJson = request.PlanJson,
            ExpectedPlanRevision = request.ExpectedPlanRevision,
            ExpectedPlanDigest = request.ExpectedPlanDigest,
            ExpectedFlowDigest = request.ExpectedFlowDigest,
            ConfirmOverwrite = request.ConfirmOverwrite,
        };
        return AuthoringResponse(store.Commit(commit));
    }

    private async Task<(int, string, byte[])> HandleAssertionVerifyAsync(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return JsonResponse(400, new { ok = false, error = "Body required." });

        Testing.FlowAssert? assertion;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("assertion", out var assertionNode))
            {
                return JsonResponse(400, new { ok = false, error = "assertion is required." });
            }
            assertion = JsonSerializer.Deserialize(
                assertionNode.GetRawText(),
                Testing.MauiFlowJsonContext.Default.FlowAssert);
        }
        catch (JsonException)
        {
            return JsonResponse(400, new { ok = false, error = "Invalid assertion JSON." });
        }
        if (assertion is null)
            return JsonResponse(400, new { ok = false, error = "assertion is required." });

        var probe = new Testing.MauiFlow
        {
            Steps =
            [
                new Testing.FlowStep
                {
                    Seq = 1,
                    Action = Testing.FlowActions.Assert,
                    Asserts = [assertion],
                },
            ],
        };
        var validation = Testing.MauiFlowValidator.Validate(probe);
        if (!validation.Ok)
        {
            return JsonResponse(400, new
            {
                ok = false,
                error = "Assertion is invalid.",
                errors = validation.Errors,
                warnings = validation.Warnings,
            });
        }
        if (IsRawRuntimeSelector(assertion.Selector))
        {
            return JsonResponse(400, new
            {
                ok = false,
                error = "A raw runtime ID cannot be promoted into an authored assertion selector."
            });
        }
        if (assertion.Kind == "propEquals" &&
            Testing.FlowSecretReference.LooksSensitive(
                assertion.Name,
                assertion.Selector?.AutomationId,
                assertion.Selector?.Text,
                assertion.Selector?.Type,
                assertion.Selector?.Id))
        {
            return JsonResponse(400, new
            {
                ok = false,
                error = "Sensitive properties cannot be persisted or verified as value assertions."
            });
        }

        var verification = await Testing.MauiFlowAssertionVerifier.VerifyAsync(
            new Testing.AgentClientMauiFlowDriver(_client),
            assertion,
            pollTries: 1,
            pollGapMs: 0,
            _lifetimeCts.Token);
        return JsonResponse(200, new
        {
            ok = true,
            passed = verification.Passed,
            observationOnly = verification.ObservationOnly,
            skipped = verification.Skipped,
            matchCount = verification.MatchCount,
            quality = verification.Quality,
            // Authoring-time verification only reports the comparison outcome. Returning the
            // live property value here could disclose a value that looked non-sensitive from its
            // element metadata but is actually secret at runtime.
            actual = verification.Actual is null ? null : "<redacted>",
            error = verification.Error,
        });
    }

    private async Task<(int, string, byte[])> HandleSelectorVerifyAsync(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return JsonResponse(400, new { ok = false, error = "Body required." });

        Testing.FlowSelector? selector;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("selector", out var selectorNode))
            {
                return JsonResponse(400, new { ok = false, error = "selector is required." });
            }
            selector = JsonSerializer.Deserialize(
                selectorNode.GetRawText(),
                Testing.MauiFlowJsonContext.Default.FlowSelector);
        }
        catch (JsonException)
        {
            return JsonResponse(400, new { ok = false, error = "Invalid selector JSON." });
        }
        if (selector is null || selector.IsEmpty)
            return JsonResponse(400, new { ok = false, error = "A selector is required." });
        if (IsRawRuntimeSelector(selector))
            return JsonResponse(400, new { ok = false, error = "A raw runtime ID cannot be promoted into an authored selector." });

        var forms = (string.IsNullOrWhiteSpace(selector.AutomationId) ? 0 : 1) +
            (string.IsNullOrWhiteSpace(selector.Text) ? 0 : 1) +
            (string.IsNullOrWhiteSpace(selector.Id) ? 0 : 1) +
            (selector.TypeIndex is not null ? 1 : 0);
        if (forms != 1)
            return JsonResponse(400, new { ok = false, error = "Exactly one active selector is required." });
        var hasStableItemKey = !string.IsNullOrWhiteSpace(selector.StableItemKey);
        var hasCollectionScope = !string.IsNullOrWhiteSpace(selector.CollectionScope);
        if (hasStableItemKey != hasCollectionScope)
            return JsonResponse(400, new { ok = false, error = "stableItemKey and collectionScope must be supplied together." });
        if ((hasStableItemKey || hasCollectionScope) && string.IsNullOrWhiteSpace(selector.AutomationId))
            return JsonResponse(400, new { ok = false, error = "A scoped item selector also requires an AutomationId." });
        if (hasStableItemKey && !Testing.FlowSelector.IsOpaqueStableItemKey(selector.StableItemKey))
            return JsonResponse(400, new { ok = false, error = "stableItemKey must be an opaque SHA-256 identity." });

        var resolution = await new Testing.FlowActionabilityEngine(
            new Testing.AgentClientMauiFlowDriver(_client),
            tries: 1,
            gapMs: 0)
            .ResolveAsync(selector, _lifetimeCts.Token);

        // A unique verification keeps its established response shape, including its compact
        // target summary. Ambiguity gets a separate value-free, bounded projection so a human can
        // inspect candidates without exposing rendered text, control values, property bags, or
        // source paths.
        if (string.Equals(resolution.Kind, Testing.FlowFailureKinds.Ambiguous, StringComparison.Ordinal))
        {
            var candidates = resolution.Candidates ?? [];
            var matches = candidates
                .Take(MaxSelectorVerifyAmbiguityMatches)
                .Select(CreateSelectorVerifyAmbiguityMatch)
                .ToArray();
            var totalCount = Math.Max(resolution.MatchCount, candidates.Count);
            var truncated = totalCount > matches.Length;
            return JsonResponse(200, new
            {
                ok = resolution.Ok,
                matchCount = resolution.MatchCount,
                quality = resolution.Quality,
                error = resolution.Error,
                element = (object?)null,
                // Flat aliases make the additive details straightforward for older lightweight
                // Inspector clients. The nested object is the Workbench's named ambiguity
                // context; both projections contain the exact same safe summaries.
                totalCount,
                truncated,
                matches,
                ambiguity = new
                {
                    totalCount,
                    truncated,
                    matches,
                },
            });
        }

        return JsonResponse(200, new
        {
            ok = resolution.Ok,
            matchCount = resolution.MatchCount,
            quality = resolution.Quality,
            error = resolution.Error,
            element = resolution.Element is null
                ? null
                : new
                {
                    type = resolution.Element.Type,
                    automationId = resolution.Element.AutomationId,
                    stableItemKey = Testing.FlowSelector.IsOpaqueStableItemKey(resolution.Element.StableItemKey)
                        ? resolution.Element.StableItemKey
                        : null,
                    collectionScope = resolution.Element.CollectionScope,
                    text = resolution.Element.Text,
                    hasSource = !string.IsNullOrWhiteSpace(resolution.Element.SourceFile),
                },
        });
    }

    private static object CreateSelectorVerifyAmbiguityMatch(ElementInfo element)
    {
        var hasSource = !string.IsNullOrWhiteSpace(element.SourceFile);
        return new
        {
            id = element.Id,
            type = element.Type,
            role = element.Role,
            automationId = element.AutomationId,
            stableItemKey = Testing.FlowSelector.IsOpaqueStableItemKey(element.StableItemKey)
                ? element.StableItemKey
                : null,
            collectionScope = element.CollectionScope,
            isVisible = element.IsVisible,
            isEnabled = element.IsEnabled,
            bounds = CreateSelectorVerifyBounds(element.Bounds),
            windowBounds = CreateSelectorVerifyBounds(element.WindowBounds),
            hasSource,
            sourceLine = hasSource ? element.SourceLine : null,
        };
    }

    private static object? CreateSelectorVerifyBounds(BoundsInfo? bounds)
        => bounds is null
            ? null
            : new
            {
                x = bounds.X,
                y = bounds.Y,
                width = bounds.Width,
                height = bounds.Height,
            };

    private static bool IsRawRuntimeSelector(Testing.FlowSelector? selector)
        => selector is not null &&
           !string.IsNullOrWhiteSpace(selector.Id) &&
           string.IsNullOrWhiteSpace(selector.AutomationId) &&
           string.IsNullOrWhiteSpace(selector.Text) &&
           selector.TypeIndex is null;

    private static bool TryReadAuthoringRequest(
        string? body,
        out WorkflowPlanSaveRequest request,
        out string? error)
    {
        request = new WorkflowPlanSaveRequest();
        error = null;
        if (string.IsNullOrWhiteSpace(body))
        {
            error = "Body required.";
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Body must be a JSON object.";
                return false;
            }
            var root = document.RootElement;
            var planJson = ReadJsonOrString(root, "planJson") ?? ReadJsonOrString(root, "plan");
            request = new WorkflowPlanSaveRequest
            {
                FlowName = ReadJsonString(root, "name"),
                PlanJson = planJson,
                ExpectedPlanRevision = ReadJsonInt(root, "expectedPlanRevision"),
                ExpectedPlanDigest = ReadJsonString(root, "expectedPlanDigest"),
                ExpectedFlowDigest = ReadJsonString(root, "expectedFlowDigest"),
                ConfirmOverwrite = ReadJsonBool(root, "confirmOverwrite"),
            };
            if (root.TryGetProperty("markdown", out var markdown) && markdown.ValueKind == JsonValueKind.String)
            {
                request = request with { Markdown = markdown.GetString() };
            }
            return true;
        }
        catch (JsonException)
        {
            error = "Invalid JSON body.";
            return false;
        }
    }

    private static string? ReadJsonString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadJsonOrString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }

    private static int? ReadJsonInt(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static bool ReadJsonBool(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;

    private static (int, string, byte[]) AuthoringResponse(WorkflowPlanStoreResult result)
    {
        var snapshot = result.Snapshot;
        var status = result.Ok ? 200 : result.Stale ? 409 : result.Code is "flow-not-found" ? 404 : 400;
        return JsonResponse(status, new
        {
            ok = result.Ok,
            code = result.Code,
            error = result.Error,
            stale = result.Stale,
            requiresOverwriteConfirmation = result.RequiresOverwriteConfirmation,
            items = result.Items,
            errors = result.Errors,
            warnings = result.Warnings,
            issues = CreateAuthoringIssues(result.Errors, result.Warnings),
            diff = result.Diff,
            flow = snapshot is null ? null : new
            {
                name = snapshot.Name,
                markdown = snapshot.Markdown,
                document = snapshot.Flow,
                digest = snapshot.FlowDigest,
            },
            plan = snapshot is null ? null : new
            {
                json = snapshot.PlanJson,
                document = snapshot.Plan,
                digest = snapshot.PlanDigest,
                revision = snapshot.Plan?.Revision,
            },
        });
    }

    internal static object[] CreateAuthoringIssues(
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings)
        => errors.Select(message => CreateAuthoringIssue(message, "error", true))
            .Concat(warnings.Select(message => CreateAuthoringIssue(message, "warning", false)))
            .ToArray();

    private static object CreateAuthoringIssue(string message, string severity, bool blocking)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            message,
            @"^step (?<step>\d+)(?: \((?<action>[^)]+)\))?: (?<detail>.+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        var detail = match.Success ? match.Groups["detail"].Value : message;
        var stepSequence = match.Success && int.TryParse(match.Groups["step"].Value, out var parsed)
            ? parsed
            : (int?)null;
        var code = detail.Contains("ambiguous selector", StringComparison.OrdinalIgnoreCase)
            ? "selector-ambiguous"
            : detail.Contains("resolve exactly one", StringComparison.OrdinalIgnoreCase)
                ? "selector-match-count"
                : detail.Contains("fragile selector", StringComparison.OrdinalIgnoreCase) ||
                  detail.Contains("selector is fragile", StringComparison.OrdinalIgnoreCase)
                    ? "selector-fragile"
                    : detail.Contains("expected result", StringComparison.OrdinalIgnoreCase) ||
                      detail.Contains("outcome check", StringComparison.OrdinalIgnoreCase)
                        ? "expected-result-missing"
                        : "review-required";
        var remediation = code is "selector-ambiguous" or "selector-match-count" or "selector-fragile"
            ? "resolve-selector"
            : code == "expected-result-missing"
                ? "add-expected-result"
                : "review";
        return new
        {
            severity,
            blocking,
            code,
            stepSequence,
            action = match.Success ? match.Groups["action"].Value : null,
            message,
            remediation,
        };
    }

    private static (int, string, byte[]) JsonResponse(int status, object value)
        => (status, "application/json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, CamelCase)));

    // ── Test Workbench run and trace adapters ────────────────────────────────────────────────
    // These routes intentionally do no replay, artifact parsing, or trust evaluation themselves.
    // Broker mode supplies the target-bound service; standalone Inspector mode reports that the
    // feature is unavailable rather than falling back to direct JavaScript/agent execution.

    private async Task<(int, string, byte[])> HandleWorkbenchRouteAsync(
        HttpRequestInfo request,
        string leaseId,
        string holderKind,
        string holderLabel)
    {
        var path = request.Path.TrimEnd('/');
        if (string.Equals(
                path,
                "/api/workbench/approval-confirmations/issue",
                StringComparison.OrdinalIgnoreCase))
        {
            return request.Method == "POST"
                ? HandleWorkbenchApprovalConfirmationIssue(request)
                : JsonResponse(405, new { ok = false, error = "Method not allowed." });
        }

        if (string.Equals(path, "/api/workbench/improve/analyze", StringComparison.OrdinalIgnoreCase))
        {
            return request.Method == "POST"
                ? await HandleWorkbenchImproveAnalyzeAsync(request.Body)
                : JsonResponse(405, new { ok = false, error = "Method not allowed." });
        }

        if (string.Equals(path, "/api/workbench/agent-requests", StringComparison.OrdinalIgnoreCase))
        {
            return request.Method == "GET"
                ? HandleWorkbenchAgentRequests()
                : JsonResponse(405, new { ok = false, error = "Method not allowed." });
        }

        var agentRequestSegments = path.Trim('/').Split('/');
        if (agentRequestSegments.Length == 5 &&
            string.Equals(agentRequestSegments[0], "api", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(agentRequestSegments[1], "workbench", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(agentRequestSegments[2], "agent-requests", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Method != "POST")
                return JsonResponse(405, new { ok = false, error = "Method not allowed." });

            var approvalRequestId = Uri.UnescapeDataString(agentRequestSegments[3]);
            return agentRequestSegments[4].ToLowerInvariant() switch
            {
                "approve" => await HandleWorkbenchAgentRequestApproveAsync(
                    approvalRequestId,
                    request.Body,
                    IsTrustedHostApprovalRequest(request)),
                "reject" => HandleWorkbenchAgentRequestReject(
                    approvalRequestId,
                    request.Body,
                    IsTrustedHostApprovalRequest(request)),
                _ => JsonResponse(404, new { ok = false, error = "Not found." }),
            };
        }

        var services = _workflowServices;
        if (services is null)
        {
            return JsonResponse(503, new
            {
                ok = false,
                error = "Broker-owned workflow runs and trace imports are unavailable in this Inspector."
            });
        }

        if (string.Equals(path, "/api/workbench/repair/classify", StringComparison.OrdinalIgnoreCase))
        {
            return request.Method == "POST"
                ? await HandleWorkbenchRepairClassifyAsync(services, request.Body)
                : JsonResponse(405, new { ok = false, error = "Method not allowed." });
        }

        if (string.Equals(path, "/api/workbench/repair/propose", StringComparison.OrdinalIgnoreCase))
        {
            return request.Method == "POST"
                ? await HandleWorkbenchRepairProposeAsync(services, request.Body)
                : JsonResponse(405, new { ok = false, error = "Method not allowed." });
        }

        if (string.Equals(path, "/api/workbench/repair/grant", StringComparison.OrdinalIgnoreCase))
        {
            return request.Method == "POST"
                ? HandleWorkbenchRepairGrant(services, request.Body)
                : JsonResponse(405, new { ok = false, error = "Method not allowed." });
        }

        if (string.Equals(path, "/api/workbench/source/analyze", StringComparison.OrdinalIgnoreCase))
        {
            return request.Method == "POST"
                ? await HandleWorkbenchXamlSourceAnalyzeAsync(request.Body)
                : JsonResponse(405, new { ok = false, error = "Method not allowed." });
        }

        if (string.Equals(path, "/api/workbench/source/propose", StringComparison.OrdinalIgnoreCase))
        {
            return request.Method == "POST"
                ? await HandleWorkbenchXamlSourceProposeAsync(services, request.Body)
                : JsonResponse(405, new { ok = false, error = "Method not allowed." });
        }

        if (string.Equals(path, "/api/workbench/source/csharp/analyze", StringComparison.OrdinalIgnoreCase))
        {
            return request.Method == "POST"
                ? await HandleWorkbenchCSharpSourceAnalyzeAsync(request.Body)
                : JsonResponse(405, new { ok = false, error = "Method not allowed." });
        }

        if (string.Equals(path, "/api/workbench/source/csharp/propose", StringComparison.OrdinalIgnoreCase))
        {
            return request.Method == "POST"
                ? await HandleWorkbenchCSharpSourceProposeAsync(services, request.Body)
                : JsonResponse(405, new { ok = false, error = "Method not allowed." });
        }

        if (string.Equals(path, "/api/workbench/target", StringComparison.OrdinalIgnoreCase))
        {
            return request.Method == "GET"
                ? await HandleWorkbenchTargetAsync(services)
                : JsonResponse(405, new { ok = false, error = "Method not allowed." });
        }

        if (string.Equals(path, "/api/workbench/run/capabilities", StringComparison.OrdinalIgnoreCase))
        {
            return request.Method == "GET"
                ? JsonResponse(200, new { ok = true, capabilities = services.GetCapabilities() })
                : JsonResponse(405, new { ok = false, error = "Method not allowed." });
        }

        if (string.Equals(path, "/api/workbench/run/journal", StringComparison.OrdinalIgnoreCase))
        {
            return request.Method == "GET"
                ? HandleWorkbenchRunJournal(
                    services,
                    request.Query.GetValueOrDefault("idempotencyKey"))
                : JsonResponse(405, new { ok = false, error = "Method not allowed." });
        }

        if (string.Equals(path, "/api/workbench/agent-handoff", StringComparison.OrdinalIgnoreCase))
        {
            return request.Method == "POST"
                ? await HandleWorkbenchAgentHandoffAsync(services, request.Body)
                : JsonResponse(405, new { ok = false, error = "Method not allowed." });
        }

        if (string.Equals(path, "/api/workbench/run/preflight", StringComparison.OrdinalIgnoreCase))
        {
            return request.Method == "POST"
                ? await HandleWorkbenchPreflightAsync(services, request.Body)
                : JsonResponse(405, new { ok = false, error = "Method not allowed." });
        }

        if (string.Equals(path, "/api/workbench/run/start", StringComparison.OrdinalIgnoreCase))
        {
            return request.Method == "POST"
                ? await HandleWorkbenchRunStartAsync(
                    services,
                    request.Body,
                    leaseId,
                    holderKind,
                    holderLabel)
                : JsonResponse(405, new { ok = false, error = "Method not allowed." });
        }

        if (string.Equals(path, "/api/workbench/artifacts/import", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Method != "POST")
                return JsonResponse(405, new { ok = false, error = "Method not allowed." });
            if (request.BodyBytes is null)
                return JsonResponse(400, new { ok = false, error = "A binary artifact body is required." });

            var imported = services.ImportArtifact(
                request.BodyBytes,
                request.Query.GetValueOrDefault("kind"));
            return JsonResponse(imported.StatusCode, imported.Response);
        }

        var segments = path.Trim('/').Split('/');
        if (segments.Length == 5 &&
            string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(segments[1], "workbench", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(segments[2], "repair", StringComparison.OrdinalIgnoreCase))
        {
            var proposalId = Uri.UnescapeDataString(segments[3]);
            var action = segments[4];
            if (!request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                return JsonResponse(405, new { ok = false, error = "Method not allowed." });

            return action.ToLowerInvariant() switch
            {
                "status" => HandleWorkbenchRepairStatus(services, proposalId),
                "preview" => await HandleWorkbenchRepairPreviewAsync(services, proposalId),
                "reject" => await HandleWorkbenchRepairRejectAsync(services, proposalId, request.Body),
                "validate" => await HandleWorkbenchRepairValidateAsync(services, proposalId, request.Body),
                "approve" => await HandleWorkbenchRepairApproveAsync(services, proposalId, request.Body),
                "apply" => await HandleWorkbenchRepairApplyAsync(services, proposalId, request.Body),
                "verify" => await HandleWorkbenchRepairVerifyAsync(services, proposalId, request.Body),
                "rollback" => await HandleWorkbenchRepairRollbackAsync(services, proposalId, request.Body),
                _ => JsonResponse(404, new { ok = false, error = "Not found." }),
            };
        }

        if (segments.Length == 6 &&
            string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(segments[1], "workbench", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(segments[2], "source", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(segments[3], "csharp", StringComparison.OrdinalIgnoreCase))
        {
            var proposalId = Uri.UnescapeDataString(segments[4]);
            var action = segments[5];
            if (!request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                return JsonResponse(405, new { ok = false, error = "Method not allowed." });

            return action.ToLowerInvariant() switch
            {
                "status" => HandleWorkbenchCSharpSourceStatus(services, proposalId),
                "preview" => await HandleWorkbenchCSharpSourcePreviewAsync(services, proposalId),
                // Reject only discards a review object, so the browser read token is enough for it.
                // Approving, narrowing, or applying one is deferred: this layer serves no route that
                // can turn a caller-supplied `hostKind`/`humanConfirmed` into source-write authority.
                "reject" => await HandleWorkbenchCSharpSourceRejectAsync(services, proposalId, request.Body),
                _ => JsonResponse(404, new { ok = false, error = "Not found." }),
            };
        }

        if (segments.Length == 5 &&
            string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(segments[1], "workbench", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(segments[2], "source", StringComparison.OrdinalIgnoreCase))
        {
            var proposalId = Uri.UnescapeDataString(segments[3]);
            var action = segments[4];
            if (!request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                return JsonResponse(405, new { ok = false, error = "Method not allowed." });

            return action.ToLowerInvariant() switch
            {
                "status" => HandleWorkbenchXamlSourceStatus(services, proposalId),
                "preview" => await HandleWorkbenchXamlSourcePreviewAsync(services, proposalId),
                // See the C# table above: reject is the only reviewed-source decision this layer
                // serves, and it can never write, roll back, or acknowledge a source change.
                "reject" => await HandleWorkbenchXamlSourceRejectAsync(services, proposalId, request.Body),
                _ => JsonResponse(404, new { ok = false, error = "Not found." }),
            };
        }

        if (segments.Length == 5 &&
            string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(segments[1], "workbench", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(segments[2], "run", StringComparison.OrdinalIgnoreCase))
        {
            var runId = Uri.UnescapeDataString(segments[3]);
            var action = segments[4];
            if (string.Equals(action, "status", StringComparison.OrdinalIgnoreCase))
            {
                return request.Method == "POST"
                    ? HandleWorkbenchRunStatus(services, runId, request.Body)
                    : JsonResponse(405, new { ok = false, error = "Method not allowed." });
            }
            if (string.Equals(action, "cancel", StringComparison.OrdinalIgnoreCase))
            {
                return request.Method == "POST"
                    ? HandleWorkbenchRunCancel(services, runId, request.Body)
                    : JsonResponse(405, new { ok = false, error = "Method not allowed." });
            }
            if (string.Equals(action, "evidence", StringComparison.OrdinalIgnoreCase))
            {
                return request.Method == "POST"
                    ? HandleWorkbenchEvidenceDownload(services, runId, request.Body)
                    : JsonResponse(405, new { ok = false, error = "Method not allowed." });
            }
        }

        if (segments.Length == 5 &&
            string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(segments[1], "workbench", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(segments[2], "artifacts", StringComparison.OrdinalIgnoreCase))
        {
            var artifactId = Uri.UnescapeDataString(segments[3]);
            var action = segments[4];
            if (request.Method != "POST")
                return JsonResponse(405, new { ok = false, error = "Method not allowed." });

            return string.Equals(action, "status", StringComparison.OrdinalIgnoreCase)
                ? HandleWorkbenchArtifactStatus(services, artifactId, request.Body)
                : string.Equals(action, "projection", StringComparison.OrdinalIgnoreCase)
                    ? HandleWorkbenchArtifactProjection(services, artifactId, request.Body)
                    : string.Equals(action, "bind-local-reproduction", StringComparison.OrdinalIgnoreCase)
                        ? HandleWorkbenchArtifactLocalReproduction(services, artifactId, request.Body)
                        : JsonResponse(404, new { ok = false, error = "Not found." });
        }

        return JsonResponse(404, new { ok = false, error = "Not found." });
    }

    private (int, string, byte[]) HandleWorkbenchAgentRequests()
    {
        if (_testAgentSessions is null ||
            string.IsNullOrWhiteSpace(_agentId) ||
            string.IsNullOrWhiteSpace(_agentInstanceId))
        {
            return JsonResponse(503, new
            {
                ok = false,
                error = "Broker-owned test-agent approvals are unavailable in this Inspector."
            });
        }

        var requests = _testAgentSessions.ListApprovalRequests(
            _agentId,
            _agentInstanceId,
            includeGrant: false);
        return JsonResponse(200, new
        {
            ok = true,
            appName = _appName,
            platform = _platform,
            pendingCount = requests.Count(request =>
                string.Equals(request.State, Testing.MauiTestAgentApprovalStates.Pending, StringComparison.Ordinal)),
            requests,
            note = "Approve here; typing approval in agent chat never issues a grant.",
        });
    }

    /// <summary>
    /// Turns one reviewed agent request into a grant. <c>humanConfirmed</c> is a caller-supplied
    /// boolean and therefore proves nothing on its own: the grant is issued only when the request
    /// also presents a single-use confirmation capability that the owner-token-gated issue endpoint
    /// minted for this exact target, this exact request ID, and this exact scope and grant length.
    /// </summary>
    private async Task<(int, string, byte[])> HandleWorkbenchAgentRequestApproveAsync(
        string approvalRequestId,
        string? body,
        bool trustedHost)
    {
        if (_testAgentSessions is null || _testAgentTargetStateRefresh is null)
            return JsonResponse(503, new { ok = false, error = "Broker-owned test-agent approvals are unavailable." });
        if (!TryReadWorkbenchAgentRequestDecision(body, out var decision, out var error))
            return JsonResponse(400, new { ok = false, error });
        if (!decision!.HumanConfirmed)
        {
            return JsonResponse(400, new
            {
                ok = false,
                error = "Review the exact scope and confirm it before approving the agent request."
            });
        }
        // The confirmation digest covers the approved scope, so an absent scope has to be refused
        // here rather than dereferenced: the issue endpoint already requires one.
        if (decision.ApprovedScope is null)
        {
            return JsonResponse(400, new
            {
                ok = false,
                error = "The exact approved scope is required. It is part of the confirmed material."
            });
        }

        var lookup = _testAgentSessions.GetApprovalRequest(approvalRequestId, includeGrant: false);
        if (!lookup.Ok || lookup.Request?.TargetState is null)
            return JsonResponse(WorkbenchTestAgentStatusCode(lookup.Error), new { ok = false, error = lookup.Error });
        if (!ApprovalTargetsThisInspector(lookup.Request))
            return JsonResponse(404, new { ok = false, error = "The approval request does not target this Inspector." });

        var currentTargetState = await _testAgentTargetStateRefresh(lookup.Request.TargetState).ConfigureAwait(false);
        if (currentTargetState is null)
        {
            return JsonResponse(409, new
            {
                ok = false,
                error = "The exact target app instance is no longer connected. Refresh the agent request."
            });
        }

        DateTimeOffset? grantExpiresAt = null;
        if (decision.GrantDurationSeconds is { } seconds)
        {
            if (seconds is < 1 or > 900)
                return JsonResponse(400, new { ok = false, error = "grantDurationSeconds must be between 1 and 900." });
            grantExpiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds);
        }

        if (!TryConsumeWorkbenchApprovalConfirmation(
                decision.ConfirmationCapability,
                WorkbenchApprovalConfirmationActions.AgentRequestApprove,
                approvalRequestId,
                AgentApprovalConfirmationMaterial(
                    approvalRequestId,
                    decision.ApprovedScope,
                    decision.GrantDurationSeconds),
                out var confirmationError))
        {
            return JsonResponse(403, new
            {
                ok = false,
                code = string.IsNullOrWhiteSpace(decision.ConfirmationCapability)
                    ? "trusted-host-required"
                    : "approval-confirmation-invalid",
                error = confirmationError,
            });
        }

        var result = _testAgentSessions.ApproveApprovalRequest(
            approvalRequestId,
            decision.ApprovedScope,
            currentTargetState,
            HumanDecision(approved: true, trustedHost, decision.DecidedBy),
            grantExpiresAt);
        return JsonResponse(WorkbenchTestAgentStatusCode(result.Error), new
        {
            ok = result.Ok,
            request = result.Request,
            error = result.Error,
            message = result.Ok
                ? "Approved. The broker delivered the opaque grant through the agent's authoring-session status; nothing needs to be pasted into chat."
                : null,
        });
    }

    private (int, string, byte[]) HandleWorkbenchAgentRequestReject(
        string approvalRequestId,
        string? body,
        bool trustedHost)
    {
        if (_testAgentSessions is null)
            return JsonResponse(503, new { ok = false, error = "Broker-owned test-agent approvals are unavailable." });
        if (!TryReadWorkbenchAgentRequestDecision(body, out var decision, out var error))
            return JsonResponse(400, new { ok = false, error });
        if (!decision!.HumanConfirmed)
            return JsonResponse(400, new { ok = false, error = "Explicit human confirmation is required." });

        var lookup = _testAgentSessions.GetApprovalRequest(approvalRequestId, includeGrant: false);
        if (!lookup.Ok || lookup.Request is null)
            return JsonResponse(WorkbenchTestAgentStatusCode(lookup.Error), new { ok = false, error = lookup.Error });
        if (!ApprovalTargetsThisInspector(lookup.Request))
            return JsonResponse(404, new { ok = false, error = "The approval request does not target this Inspector." });

        var result = _testAgentSessions.RejectApprovalRequest(
            approvalRequestId,
            HumanDecision(approved: false, trustedHost, decision.DecidedBy),
            decision.ReasonCode);
        return JsonResponse(WorkbenchTestAgentStatusCode(result.Error), new
        {
            ok = result.Ok,
            request = result.Request,
            error = result.Error,
            message = result.Ok ? "Rejected. No grant was issued and no app or test state changed." : null,
        });
    }

    /// <summary>
    /// Mints one single-use confirmation capability. This is the only place authority to approve an
    /// agent request originates, and it requires the owner-only native-host approval token that the
    /// broker mints for the local operator. The capability is bound to the live target, the action,
    /// the subject, and a digest over the exact reviewed material, so a later approve call cannot
    /// widen the scope or lengthen the grant that was reviewed here.
    /// </summary>
    private (int, string, byte[]) HandleWorkbenchApprovalConfirmationIssue(HttpRequestInfo httpRequest)
    {
        if (_trustedHostApprovalVerifier is null)
        {
            return JsonResponse(501, new
            {
                ok = false,
                code = "native-host-approval-unavailable",
                error = "No owner-token approval client is configured. Approval is disabled; browser, chat, loopback, and broker-state values cannot substitute for it.",
            });
        }

        if (!IsTrustedHostApprovalRequest(httpRequest))
            return TrustedHostApprovalRequired();
        if (!TryReadWorkbenchApprovalConfirmationIssue(
                httpRequest.Body,
                out var request,
                out var error))
        {
            return JsonResponse(400, new { ok = false, error });
        }
        if (string.IsNullOrWhiteSpace(_agentId) || string.IsNullOrWhiteSpace(_agentInstanceId))
            return JsonResponse(409, new { ok = false, error = "The Inspector has no exact live target." });

        string material;
        switch (request!.Action)
        {
            case WorkbenchApprovalConfirmationActions.AgentRequestApprove:
            {
                if (!DevFlowPreviewPolicy.IsAgentAuthoringEnabled(_previewFlags))
                    return JsonResponse(404, new { ok = false, error = "This preview capability is disabled." });
                if (_testAgentSessions is null ||
                    string.IsNullOrWhiteSpace(request.SubjectId) ||
                    request.ApprovedScope is null)
                {
                    return JsonResponse(400, new
                    {
                        ok = false,
                        error = "An agent approval request ID and exact approved scope are required."
                    });
                }
                var lookup = _testAgentSessions.GetApprovalRequest(request.SubjectId, includeGrant: false);
                if (!lookup.Ok || lookup.Request is null)
                    return JsonResponse(404, new { ok = false, error = lookup.Error });
                if (!ApprovalTargetsThisInspector(lookup.Request))
                    return JsonResponse(404, new { ok = false, error = "The approval request does not target this Inspector." });
                material = AgentApprovalConfirmationMaterial(
                    request.SubjectId,
                    request.ApprovedScope,
                    request.GrantDurationSeconds);
                break;
            }
            default:
                return JsonResponse(400, new { ok = false, error = "The approval confirmation action is unsupported." });
        }

        var confirmation = RememberWorkbenchApprovalConfirmation(
            request.Action!,
            request.SubjectId!,
            material);
        return JsonResponse(201, new
        {
            ok = true,
            confirmationCapability = confirmation.Capability,
            confirmationDigest = confirmation.MaterialDigest,
            expiresAt = confirmation.ExpiresAt,
            target = new
            {
                agentId = _agentId,
                agentInstanceId = _agentInstanceId,
            },
            note = "This single-use capability confirms one exact target, subject, and digest. Chat text is not approval.",
        });
    }

    private (int, string, byte[]) TrustedHostApprovalRequired()
        => JsonResponse(403, new
        {
            ok = false,
            code = "trusted-host-required",
            error = "The owner-only host approval token is required to confirm this exact target and request. Browser or chat text cannot issue a grant.",
        });

    /// <summary>
    /// True when the request presents the owner-only host approval token minted by the broker. This
    /// proves the caller can read owner-restricted local state; it is not, and must never be
    /// described as, proof that a human rather than a local agent process made the call.
    /// </summary>
    private bool IsTrustedHostApprovalRequest(HttpRequestInfo request)
        => _trustedHostApprovalVerifier?.Invoke(
            request.Headers.TryGetValue("x-devflow-host-approval-token", out var hostToken)
                ? hostToken
                : null) == true;

    /// <summary>
    /// Atomically redeems a confirmation capability. Removal happens before any comparison, so a
    /// replay of the same capability fails even when every other field still matches.
    /// </summary>
    private bool TryConsumeWorkbenchApprovalConfirmation(
        string? capability,
        string action,
        string subjectId,
        string material,
        out string error)
    {
        error = "An owner-token confirmation capability is required.";
        if (string.IsNullOrWhiteSpace(capability))
            return false;

        lock (_workbenchRunGate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var expired in _workbenchApprovalConfirmations
                         .Where(pair => pair.Value.ExpiresAt <= now)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                _workbenchApprovalConfirmations.Remove(expired);
            }

            if (!_workbenchApprovalConfirmations.Remove(capability, out var confirmation))
            {
                error = "The host confirmation capability is invalid, expired, or already used.";
                return false;
            }
            if (!string.Equals(confirmation.AgentId, _agentId, StringComparison.Ordinal) ||
                !string.Equals(confirmation.AgentInstanceId, _agentInstanceId, StringComparison.Ordinal) ||
                !string.Equals(confirmation.Action, action, StringComparison.Ordinal) ||
                !string.Equals(confirmation.SubjectId, subjectId, StringComparison.Ordinal) ||
                !FixedDigestEquals(confirmation.MaterialDigest, ApprovalMaterialDigest(material)))
            {
                error = "The host confirmation does not match this exact target, subject, and digest.";
                return false;
            }
            return true;
        }
    }

    private WorkbenchApprovalConfirmation RememberWorkbenchApprovalConfirmation(
        string action,
        string subjectId,
        string material)
    {
        var confirmation = new WorkbenchApprovalConfirmation(
            CreateWorkbenchCapability(),
            _agentId!,
            _agentInstanceId!,
            action,
            subjectId,
            ApprovalMaterialDigest(material),
            DateTimeOffset.UtcNow.AddMinutes(2));
        lock (_workbenchRunGate)
        {
            foreach (var expired in _workbenchApprovalConfirmations
                         .Where(pair => pair.Value.ExpiresAt <= DateTimeOffset.UtcNow)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                _workbenchApprovalConfirmations.Remove(expired);
            }
            while (_workbenchApprovalConfirmations.Count >= MaxRetainedWorkbenchApprovalConfirmations)
            {
                var oldest = _workbenchApprovalConfirmations
                    .OrderBy(pair => pair.Value.ExpiresAt)
                    .First();
                _workbenchApprovalConfirmations.Remove(oldest.Key);
            }
            _workbenchApprovalConfirmations.Add(confirmation.Capability, confirmation);
        }
        return confirmation;
    }

    private static string CreateWorkbenchCapability()
        => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string ApprovalMaterialDigest(string material)
        => Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(material)))
            .ToLowerInvariant();

    private static bool FixedDigestEquals(string expected, string supplied)
        => expected.Length == supplied.Length &&
           System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
               Encoding.UTF8.GetBytes(expected),
               Encoding.UTF8.GetBytes(supplied));

    private string AgentApprovalConfirmationMaterial(
        string approvalRequestId,
        Testing.MauiTestAgentMutationScope scope,
        int? grantDurationSeconds)
        => ApprovalMaterial(
            WorkbenchApprovalConfirmationActions.AgentRequestApprove,
            _agentId,
            _agentInstanceId,
            approvalRequestId,
            ApprovalMaterialDigest(CanonicalScope(scope)),
            grantDurationSeconds?.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Length-prefixes every component so no two different field splits can produce the same digest
    /// input. The value is used verbatim, never truncated: a scope's selector and route lists run to
    /// tens of kilobytes, and clipping them would let a widened scope share a digest with the
    /// narrower one that was actually reviewed.
    /// </summary>
    private static string ApprovalMaterial(params string?[] values)
        => string.Concat(values.Select(static value =>
        {
            var exact = value ?? "";
            return $"{exact.Length}:{exact}";
        }));

    /// <summary>
    /// Canonicalizes an approval scope for digesting. Every <b>entry</b> is length-prefixed, not
    /// just every field: joining a list with a separator alone lets a single entry that contains
    /// the separator produce the same bytes as several entries that do not, so
    /// <c>["a\nb"]</c> and <c>["a", "b"]</c> would share a digest and a scope could be reshaped
    /// between the reviewed text and the redeemed grant without changing the confirmation.
    /// </summary>
    /// <remarks>
    /// Issuing and verifying a confirmation both go through this method, so changing the encoding
    /// changes both sides together; no digest is persisted across a broker restart.
    /// </remarks>
    internal static string CanonicalScope(Testing.MauiTestAgentMutationScope scope)
        => ApprovalMaterial(
            CanonicalList(scope.AllowedActions),
            CanonicalList(scope.AllowedSelectors),
            CanonicalList(scope.AllowedRoutes),
            CanonicalList(scope.AllowedSideEffectClasses),
            Convert.ToString(scope.MaxActionCount, System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToString(scope.MaxValueBytes, System.Globalization.CultureInfo.InvariantCulture));

    private static string CanonicalList(List<string> values)
        => ApprovalMaterial(values
            .OrderBy(static value => value, StringComparer.Ordinal)
            .Cast<string?>()
            .ToArray());

    private static bool TryReadWorkbenchApprovalConfirmationIssue(
        string? body,
        out WorkbenchApprovalConfirmationIssueRequest? request,
        out string? error)
    {
        request = null;
        error = null;
        if (string.IsNullOrWhiteSpace(body))
        {
            error = "An approval confirmation request body is required.";
            return false;
        }
        try
        {
            request = JsonSerializer.Deserialize<WorkbenchApprovalConfirmationIssueRequest>(body, CamelCase);
        }
        catch (JsonException)
        {
            error = "The approval confirmation request is invalid.";
            return false;
        }
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Action) ||
            string.IsNullOrWhiteSpace(request.SubjectId))
        {
            error = "An approval confirmation action and subject ID are required.";
            return false;
        }
        return true;
    }

    /// <summary>
    /// Preview gating for the workbench surface. Agent-request review is available without the
    /// agent-authoring preview because listing and rejecting cannot create authority; issuing a
    /// confirmation stays reachable so it can answer with its own explicit 403/501 rather than a
    /// 404 that reads like the endpoint is missing.
    /// </summary>
    internal static bool IsPreviewRouteEnabled(
        Testing.MauiPreviewFeatureFlags flags,
        string path)
    {
        ArgumentNullException.ThrowIfNull(flags);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalized = path.TrimEnd('/');
        if (normalized.StartsWith("/api/workbench/agent-requests", StringComparison.OrdinalIgnoreCase))
            return DevFlowPreviewPolicy.IsInspectorAgentRequestRouteEnabled(flags, normalized);
        if (string.Equals(
                normalized,
                "/api/workbench/approval-confirmations/issue",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (normalized.StartsWith("/api/workbench/artifacts", StringComparison.OrdinalIgnoreCase))
            return flags.IsEnabled("trace-import-export");
        // Repair and source review are separate advanced opt-ins, not part of ordinary authoring.
        // Falling through to the workbench flag would enable a reviewed-patch surface for anyone who
        // turned on the guided journey, which is exactly the widening these flags exist to prevent.
        if (normalized.StartsWith("/api/workbench/repair", StringComparison.OrdinalIgnoreCase))
            return flags.IsEnabled("repair-proposals");
        if (normalized.StartsWith("/api/workbench/source", StringComparison.OrdinalIgnoreCase))
            return flags.IsEnabled("source-proposals");
        if (string.Equals(normalized, "/api/workbench/agent-handoff", StringComparison.OrdinalIgnoreCase))
            return flags.IsEnabled("agent-authoring");

        return flags.IsEnabled("workbench");
    }

    private bool ApprovalTargetsThisInspector(Testing.MauiTestAgentApprovalRecord request)
        => string.Equals(request.Target?.AgentId, _agentId, StringComparison.Ordinal) &&
           string.Equals(request.Target?.AgentInstanceId, _agentInstanceId, StringComparison.Ordinal);

    /// <summary>
    /// Builds the decision recorded by the broker. A caller that presented the owner-only host
    /// approval token is recorded on the "host" channel and may attach its own bounded labels.
    ///
    /// <para>
    /// Those labels are <b>self-asserted</b>, not attested: any holder of the owner token can send
    /// any label, so they record intent rather than prove origin. No authorization check reads them.
    /// </para>
    /// </summary>
    private static Testing.MauiTestAgentHumanApproval HumanDecision(
        bool approved,
        bool trustedHost,
        WorkbenchDecisionProvenance? decidedBy)
    {
        if (!trustedHost)
            return WorkbenchHumanDecision(approved);

        return new Testing.MauiTestAgentHumanApproval
        {
            Approved = approved,
            ApprovalChannel = "host",
            ApprovedAt = DateTimeOffset.UtcNow,
            Actor = new Testing.MauiActorProvenance
            {
                ActorKind = "host",
                ActorId = SafeProvenanceLabel(decidedBy?.ActorId, 128) ?? "native-host-user",
                // Channel and provider are joined into a single 128-character audit label, so their
                // budgets are chosen to fit that join and never truncate one mid-token.
                Channel = SafeProvenanceLabel(decidedBy?.Channel, 32) ?? "host",
                Provider = SafeProvenanceLabel(decidedBy?.Provider, 64) ?? "native-host",
            },
        };
    }

    /// <summary>
    /// Restricts a caller-supplied provenance label to a short single-line slug so it cannot smuggle
    /// separators, control characters, or markup into audit records and CLI output.
    /// </summary>
    private static string? SafeProvenanceLabel(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var builder = new StringBuilder(Math.Min(value.Length, maximum));
        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
                builder.Append(character);
            if (builder.Length == maximum)
                break;
        }
        return builder.Length == 0 ? null : builder.ToString();
    }

    private static Testing.MauiTestAgentHumanApproval WorkbenchHumanDecision(bool approved)
        => new()
        {
            Approved = approved,
            ApprovalChannel = "workbench",
            ApprovedAt = DateTimeOffset.UtcNow,
            Actor = new Testing.MauiActorProvenance
            {
                ActorKind = "human",
                ActorId = "workbench-user",
                Channel = "workbench",
                Provider = "inspector-server",
            },
        };

    private static int WorkbenchTestAgentStatusCode(Testing.MauiTestAgentError? error)
    {
        if (error is null)
            return 200;
        if (error.Code == Testing.MauiTestAgentErrorCodes.ApprovalRequestNotFound)
            return 404;
        return error.Category switch
        {
            Testing.MauiTestAgentErrorCategories.Authorization => 403,
            Testing.MauiTestAgentErrorCategories.Target or
            Testing.MauiTestAgentErrorCategories.State or
            Testing.MauiTestAgentErrorCategories.Conflict => 409,
            Testing.MauiTestAgentErrorCategories.Capability => 429,
            _ => 400,
        };
    }

    private static bool TryReadWorkbenchAgentRequestDecision(
        string? body,
        out WorkbenchAgentRequestDecision? request,
        out string? error)
    {
        request = null;
        error = null;
        if (string.IsNullOrWhiteSpace(body))
        {
            error = "A human decision body is required.";
            return false;
        }

        try
        {
            request = JsonSerializer.Deserialize<WorkbenchAgentRequestDecision>(body, CamelCase);
        }
        catch (JsonException)
        {
            error = "The human decision body is invalid.";
            return false;
        }

        if (request is null)
        {
            error = "The human decision body is invalid.";
            return false;
        }
        return true;
    }

    // ── Reviewed XAML AutomationId source proposals ─────────────────────────────────────────
    // These adapters deliberately do not share the selector-repair path. Source application is
    // local-host-only, source and flow approvals are independent, and no flow selector is changed.

    private async Task<(int, string, byte[])> HandleWorkbenchXamlSourceAnalyzeAsync(string? body)
    {
        if (!TryReadWorkbenchXamlSourceRequest(body, out WorkbenchXamlSourceProposalRequest? request, out var error))
            return JsonResponse(400, new { ok = false, error });

        var built = await BuildXamlSourceProposalAsync(request!).ConfigureAwait(false);
        return JsonResponse(built.Ok ? 200 : 422, new
        {
            ok = built.Ok,
            eligibility = built.Analysis?.Decision,
            uniqueness = built.Analysis?.Uniqueness,
            preview = built.Proposal,
            code = built.Code,
            error = built.Error,
            sourceApply = "requires-separate-human-source-approval",
            flowFollowUp = "requires-separate-flow-repair-approval",
        });
    }

    private async Task<(int, string, byte[])> HandleWorkbenchXamlSourceProposeAsync(
        InspectorWorkflowServices services,
        string? body)
    {
        if (!TryReadWorkbenchXamlSourceRequest(body, out WorkbenchXamlSourceProposalRequest? request, out var error))
            return JsonResponse(400, new { ok = false, error });

        var built = await BuildXamlSourceProposalAsync(request!).ConfigureAwait(false);
        if (!built.Ok || built.Proposal is null)
        {
            return JsonResponse(422, new
            {
                ok = false,
                eligibility = built.Analysis?.Decision,
                uniqueness = built.Analysis?.Uniqueness,
                code = built.Code,
                error = built.Error,
            });
        }

        // Browser/Inspector proposals are human-authored review objects. MCP test-agent tools are
        // intentionally not routed here and cannot create a source-writing authority.
        var stored = services.ProposeXamlSource(built.Proposal, agentOriginated: false);
        var history = stored.Ok
            ? await AppendXamlSourceHistoryAsync(stored.Proposal).ConfigureAwait(false)
            : null;
        return JsonResponse(stored.Ok ? 201 : 409, new
        {
            ok = stored.Ok,
            proposal = stored.Proposal,
            history,
            code = stored.Code,
            error = stored.Error,
            flowChanged = false,
        });
    }

    private (int, string, byte[]) HandleWorkbenchXamlSourceStatus(
        InspectorWorkflowServices services,
        string proposalId)
    {
        var result = services.GetXamlSource(proposalId);
        return JsonResponse(result.Ok ? 200 : 404, new
        {
            ok = result.Ok,
            proposal = result.Proposal,
            code = result.Code,
            error = result.Error,
        });
    }

    private async Task<(int, string, byte[])> HandleWorkbenchXamlSourcePreviewAsync(
        InspectorWorkflowServices services,
        string proposalId)
    {
        var result = services.PreviewXamlSource(proposalId);
        var history = result.Ok
            ? await AppendXamlSourceHistoryAsync(result.Proposal).ConfigureAwait(false)
            : null;
        return JsonResponse(result.Ok ? 200 : 409, new
        {
            ok = result.Ok,
            proposal = result.Proposal,
            history,
            code = result.Code,
            error = result.Error,
            sourceApply = "not-applied",
            flowChanged = false,
        });
    }

    private async Task<(int, string, byte[])> HandleWorkbenchXamlSourceRejectAsync(
        InspectorWorkflowServices services,
        string proposalId,
        string? body)
    {
        if (!TryReadWorkbenchXamlSourceRequest(body, out WorkbenchXamlSourceRejectRequest? request, out var error))
            return JsonResponse(400, new { ok = false, error });
        var result = services.RejectXamlSource(proposalId, request!.Reviewer, request.ReasonCode);
        var history = result.Ok
            ? await AppendXamlSourceHistoryAsync(result.Proposal).ConfigureAwait(false)
            : null;
        return JsonResponse(result.Ok ? 200 : 409, new
        {
            ok = result.Ok,
            proposal = result.Proposal,
            history,
            code = result.Code,
            error = result.Error,
        });
    }

    private async Task<XamlSourceProposalBuildResult> BuildXamlSourceProposalAsync(
        WorkbenchXamlSourceProposalRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ElementId) ||
            string.IsNullOrWhiteSpace(request.ProposedAutomationId))
        {
            return XamlSourceProposalBuildResult.Failure(
                "source-request-invalid",
                "elementId and proposedAutomationId are required.");
        }

        ElementInfo? element;
        List<ElementInfo> tree;
        try
        {
            element = await _client.GetElementAsync(request.ElementId).ConfigureAwait(false);
            tree = await _client.GetTreeAsync().ConfigureAwait(false);
        }
        catch
        {
            return XamlSourceProposalBuildResult.Failure(
                "source-runtime-unavailable",
                "The current runtime element and live uniqueness scope could not be resolved.");
        }
        if (element is null)
        {
            return XamlSourceProposalBuildResult.Failure(
                "source-element-not-found",
                "The selected runtime element no longer exists.");
        }

        return await _xamlSourceProposalService.BuildAsync(
            element,
            request.ProposedAutomationId,
            tree,
            request.AffectedFlows,
            request.AffectedPlatforms is { Count: > 0 }
                ? request.AffectedPlatforms
                : DefaultXamlSourcePlatforms(),
            _lifetimeCts.Token).ConfigureAwait(false);
    }

    private string? ComputeXamlSourceProjectIdentity()
    {
        if (string.IsNullOrWhiteSpace(_project) || !Path.IsPathFullyQualified(_project))
            return null;
        try
        {
            var full = Path.GetFullPath(_project);
            var root = Directory.Exists(full) ? full : Path.GetDirectoryName(full);
            return string.IsNullOrWhiteSpace(root) ? null :
                "sha256:" + Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        Encoding.UTF8.GetBytes(root))).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    private async Task<WorkflowXamlSourceHistoryAppendResult?> AppendXamlSourceHistoryAsync(
        WorkflowXamlSourceProposalSnapshot? snapshot)
    {
        if (snapshot is null || string.IsNullOrWhiteSpace(_project) || !Path.IsPathFullyQualified(_project))
            return null;
        try
        {
            var full = Path.GetFullPath(_project);
            var root = Directory.Exists(full) ? full : Path.GetDirectoryName(full);
            return string.IsNullOrWhiteSpace(root)
                ? null
                : new WorkflowXamlSourceHistoryStore(root).Append(snapshot);
        }
        catch
        {
            return WorkflowXamlSourceHistoryAppendResult.Failure(
                "source-history-unavailable",
                "A trusted local project root is unavailable for source proposal history.");
        }
    }

    private static List<Testing.MauiXamlSourcePlatformVerification> DefaultXamlSourcePlatforms()
    {
        var appleExternal = OperatingSystem.IsWindows();
        return
        [
            new Testing.MauiXamlSourcePlatformVerification
            {
                Platform = "android",
                TargetFramework = "net10.0-android",
                BuildState = "pending-host-build",
                RuntimeRemapState = "pending-runtime-remap",
                UniquenessState = "pending-runtime-uniqueness",
                ReplayState = "pending-flow-replay",
                OracleState = "pending-independent-oracle",
            },
            new Testing.MauiXamlSourcePlatformVerification
            {
                Platform = "windows",
                TargetFramework = "net10.0-windows10.0.19041.0",
                BuildState = "pending-host-build",
                RuntimeRemapState = "pending-runtime-remap",
                UniquenessState = "pending-runtime-uniqueness",
                ReplayState = "pending-flow-replay",
                OracleState = "pending-independent-oracle",
            },
            new Testing.MauiXamlSourcePlatformVerification
            {
                Platform = "ios",
                TargetFramework = "net10.0-ios",
                BuildState = appleExternal ? "pending-external-qa" : "pending-host-build",
                RuntimeRemapState = "pending-external-qa",
                UniquenessState = "pending-external-qa",
                ReplayState = "pending-external-qa",
                OracleState = "pending-external-qa",
                ReasonCode = appleExternal ? "apple-target-unavailable-on-windows" : null,
            },
            new Testing.MauiXamlSourcePlatformVerification
            {
                Platform = "maccatalyst",
                TargetFramework = "net10.0-maccatalyst",
                BuildState = appleExternal ? "pending-external-qa" : "pending-host-build",
                RuntimeRemapState = "pending-external-qa",
                UniquenessState = "pending-external-qa",
                ReplayState = "pending-external-qa",
                OracleState = "pending-external-qa",
                ReasonCode = appleExternal ? "apple-target-unavailable-on-windows" : null,
            },
        ];
    }

    private static bool TryReadWorkbenchXamlSourceRequest<T>(
        string? body,
        out T? request,
        out string? error)
        where T : class
    {
        request = null;
        error = null;
        if (string.IsNullOrWhiteSpace(body))
        {
            error = "A JSON XAML source proposal request body is required.";
            return false;
        }
        try
        {
            request = JsonSerializer.Deserialize<T>(body, CamelCase);
            if (request is null)
            {
                error = "The XAML source proposal request is invalid.";
                return false;
            }
            return true;
        }
        catch (JsonException)
        {
            error = "The XAML source proposal request is not valid JSON.";
            return false;
        }
    }

    // ── Reviewed Roslyn C# AutomationId source proposals ─────────────────────────────────────
    // These handlers intentionally have no broker-side source write. A native IDE host receives
    // the exact patch after begin-host-apply and must acknowledge pre/post hashes afterward.

    private async Task<(int, string, byte[])> HandleWorkbenchCSharpSourceAnalyzeAsync(string? body)
    {
        if (!TryReadWorkbenchCSharpSourceRequest(body, out WorkbenchCSharpSourceProposalRequest? request, out var error))
            return JsonResponse(400, new { ok = false, error });

        var built = await BuildCSharpSourceProposalAsync(request!).ConfigureAwait(false);
        return JsonResponse(built.Ok ? 200 : 422, new
        {
            ok = built.Ok,
            language = "CSharp",
            eligibility = built.Analysis?.Decision,
            uniqueness = built.Analysis?.Uniqueness,
            preview = built.Proposal,
            code = built.Code,
            error = built.Error,
            sourceApply = "ide-mediated-host-acknowledgment-required",
            brokerSourceWrite = false,
            flowFollowUp = "requires-separate-flow-repair-approval",
        });
    }

    private async Task<(int, string, byte[])> HandleWorkbenchCSharpSourceProposeAsync(
        InspectorWorkflowServices services,
        string? body)
    {
        if (!TryReadWorkbenchCSharpSourceRequest(body, out WorkbenchCSharpSourceProposalRequest? request, out var error))
            return JsonResponse(400, new { ok = false, error });

        var built = await BuildCSharpSourceProposalAsync(request!).ConfigureAwait(false);
        if (!built.Ok || built.Proposal is null)
        {
            return JsonResponse(422, new
            {
                ok = false,
                language = "CSharp",
                eligibility = built.Analysis?.Decision,
                uniqueness = built.Analysis?.Uniqueness,
                code = built.Code,
                error = built.Error,
            });
        }

        var stored = services.ProposeCSharpSource(built.Proposal);
        var history = stored.Ok
            ? await AppendCSharpSourceHistoryAsync(stored.Proposal).ConfigureAwait(false)
            : null;
        return JsonResponse(stored.Ok ? 201 : 409, new
        {
            ok = stored.Ok,
            language = "CSharp",
            proposal = stored.Proposal,
            history,
            code = stored.Code,
            error = stored.Error,
            sourceChanged = false,
            brokerSourceWrite = false,
            flowChanged = false,
        });
    }

    private (int, string, byte[]) HandleWorkbenchCSharpSourceStatus(
        InspectorWorkflowServices services,
        string proposalId)
    {
        var result = services.GetCSharpSource(proposalId);
        return JsonResponse(result.Ok ? 200 : 404, new
        {
            ok = result.Ok,
            language = "CSharp",
            proposal = result.Proposal,
            code = result.Code,
            error = result.Error,
            brokerSourceWrite = false,
        });
    }

    private async Task<(int, string, byte[])> HandleWorkbenchCSharpSourcePreviewAsync(
        InspectorWorkflowServices services,
        string proposalId)
    {
        var result = services.PreviewCSharpSource(proposalId);
        var history = result.Ok
            ? await AppendCSharpSourceHistoryAsync(result.Proposal).ConfigureAwait(false)
            : null;
        return JsonResponse(result.Ok ? 200 : 409, new
        {
            ok = result.Ok,
            language = "CSharp",
            proposal = result.Proposal,
            history,
            code = result.Code,
            error = result.Error,
            sourceChanged = false,
            brokerSourceWrite = false,
        });
    }

    private async Task<(int, string, byte[])> HandleWorkbenchCSharpSourceRejectAsync(
        InspectorWorkflowServices services,
        string proposalId,
        string? body)
    {
        if (!TryReadWorkbenchCSharpSourceRequest(body, out WorkbenchCSharpSourceRejectRequest? request, out var error))
            return JsonResponse(400, new { ok = false, error });
        var result = services.RejectCSharpSource(proposalId, request!.Reviewer, request.ReasonCode);
        var history = result.Ok
            ? await AppendCSharpSourceHistoryAsync(result.Proposal).ConfigureAwait(false)
            : null;
        return JsonResponse(result.Ok ? 200 : 409, new
        {
            ok = result.Ok,
            language = "CSharp",
            proposal = result.Proposal,
            history,
            code = result.Code,
            error = result.Error,
            sourceChanged = false,
        });
    }

    private async Task<CSharpSourceProposalBuildResult> BuildCSharpSourceProposalAsync(
        WorkbenchCSharpSourceProposalRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ElementId) ||
            string.IsNullOrWhiteSpace(request.ProposedAutomationId))
        {
            return CSharpSourceProposalBuildResult.Failure(
                "source-request-invalid",
                "elementId and proposedAutomationId are required.");
        }

        ElementInfo? element;
        List<ElementInfo> tree;
        try
        {
            element = await _client.GetElementAsync(request.ElementId).ConfigureAwait(false);
            tree = await _client.GetTreeAsync().ConfigureAwait(false);
        }
        catch
        {
            return CSharpSourceProposalBuildResult.Failure(
                "source-runtime-unavailable",
                "The current runtime element and live uniqueness scope could not be resolved.");
        }
        if (element is null)
        {
            return CSharpSourceProposalBuildResult.Failure(
                "source-element-not-found",
                "The selected runtime element no longer exists.");
        }
        if (request.SourceFile is not null ||
            request.SourceLine is not null ||
            request.SourceColumn is not null ||
            request.SourceHash is not null ||
            request.SourceConfidence is not null)
        {
            if (string.IsNullOrWhiteSpace(request.SourceFile) ||
                request.SourceLine is not > 0 ||
                request.SourceColumn is not > 0 ||
                request.SourceHash is not { Length: 16 } hash ||
                !hash.All(Uri.IsHexDigit) ||
                !string.Equals(request.SourceConfidence, "roslyn-proven", StringComparison.OrdinalIgnoreCase))
            {
                return CSharpSourceProposalBuildResult.Failure(
                    "source-request-invalid",
                    "A native IDE C# source selection must include an absolute file, line, column, short hash, and roslyn-proven confidence.");
            }

            // The semantic service still confines this path to the registered project, recomputes
            // the hash, and compares the resolved MAUI type to the selected runtime element.
            element.SourceFile = request.SourceFile;
            element.SourceLine = request.SourceLine;
            element.SourceColumn = request.SourceColumn;
            element.SourceHash = request.SourceHash;
            element.SourceConfidence = request.SourceConfidence;
        }

        return await _csharpSourceProposalService.BuildAsync(
            element,
            request.ProposedAutomationId,
            tree,
            request.AffectedFlows,
            request.AffectedPlatforms,
            _lifetimeCts.Token).ConfigureAwait(false);
    }

    private async Task<WorkflowCSharpSourceHistoryAppendResult?> AppendCSharpSourceHistoryAsync(
        WorkflowCSharpSourceProposalSnapshot? snapshot)
    {
        if (snapshot is null || string.IsNullOrWhiteSpace(_project) || !Path.IsPathFullyQualified(_project))
            return null;
        try
        {
            var full = Path.GetFullPath(_project);
            var root = Directory.Exists(full) ? full : Path.GetDirectoryName(full);
            return string.IsNullOrWhiteSpace(root)
                ? null
                : new WorkflowCSharpSourceHistoryStore(root).Append(snapshot);
        }
        catch
        {
            return WorkflowCSharpSourceHistoryAppendResult.Failure(
                "source-history-unavailable",
                "A trusted local project root is unavailable for C# source proposal history.");
        }
    }

    private static bool TryReadWorkbenchCSharpSourceRequest<T>(
        string? body,
        out T? request,
        out string? error)
        where T : class
    {
        request = null;
        error = null;
        if (string.IsNullOrWhiteSpace(body))
        {
            error = "A JSON C# source proposal request body is required.";
            return false;
        }
        try
        {
            request = JsonSerializer.Deserialize<T>(body, CamelCase);
            if (request is null)
            {
                error = "The C# source proposal request is invalid.";
                return false;
            }
            return true;
        }
        catch (JsonException)
        {
            error = "The C# source proposal request is not valid JSON.";
            return false;
        }
    }

    // ── Human-approved selector repair adapters ─────────────────────────────────────────────

    private async Task<(int, string, byte[])> HandleWorkbenchRepairClassifyAsync(
        InspectorWorkflowServices services,
        string? body)
    {
        if (!TryReadWorkbenchRepairRequest(body, out WorkbenchRepairClassifyRequest? request, out var error))
            return JsonResponse(400, new { ok = false, error });

        try
        {
            var status = await _client.GetStatusAsync().ConfigureAwait(false);
            var current = request!.CurrentCheckpoint ?? new Testing.MauiFlowCheckpoint();
            current.AppBuildFingerprint = SafeWorkbenchText(status?.App?.Build) ?? current.AppBuildFingerprint;
            current.AgentInstanceId = _agentInstanceId ?? current.AgentInstanceId;
            current.Route = SafeWorkbenchText(status?.Route) ?? current.Route;
            current.Window = SafeWorkbenchText(status?.Window) ?? current.Window;
            current.Modal = SafeWorkbenchText(status?.Modal) ?? current.Modal;
            current.Locale = SafeWorkbenchText(status?.Locale) ?? current.Locale;
            current.Theme = SafeWorkbenchText(status?.Theme) ?? current.Theme;
            current.Orientation = SafeWorkbenchText(status?.Orientation) ?? current.Orientation;
            current.DisplayProfile = SafeWorkbenchText(status?.DisplayProfile) ?? current.DisplayProfile;

            var prior = request.PriorActiveSelectorResolution;
            if (prior is null &&
                request.Run?.RunId is { Length: > 0 } sourceRunId &&
                (request.Run.Failure?.StepId ?? request.Run.DivergenceStepId) is { Length: > 0 } sourceStepId)
            {
                prior = services.GetPriorSelectorResolution(sourceRunId, sourceStepId).Resolution;
            }
            var decision = Testing.MauiFlowRepairEligibilityEvaluator.Evaluate(
                new Testing.MauiFlowRepairEligibilityInput
                {
                    Run = request.Run,
                    Plan = request.Plan,
                    ReplayEligibility = request.ReplayEligibility,
                    ExpectedCheckpoint = request.ExpectedCheckpoint,
                    CurrentCheckpoint = current,
                    BeforeDispatch = request.BeforeDispatch,
                    IsCurrentLocalRun = request.IsCurrentLocalRun,
                    ArtifactTrust = request.ArtifactTrust,
                    PriorActiveSelectorResolution = prior,
                    TargetFingerprint = request.TargetFingerprint,
                    AdditionalFailureCodes = request.AdditionalFailureCodes ?? [],
                });
            var classificationToken = RememberWorkbenchRepairClassification(
                decision,
                reportRunId: request.Run?.RunId,
                flowDigest: request.Run?.FlowDigest,
                prior);
            return JsonResponse(200, new
            {
                ok = true,
                eligibility = decision,
                classificationToken,
                currentCheckpoint = current,
                repairAuthority = "human-approved-only",
                repairValidationAvailable = _repairValidationAvailable,
            });
        }
        catch (Exception exception) when (IsAgentUnavailableException(exception))
        {
            return JsonResponse(503, new
            {
                ok = false,
                error = "The selected agent is unavailable to establish current repair checkpoint facts."
            });
        }
    }

    private async Task<(int, string, byte[])> HandleWorkbenchRepairProposeAsync(
        InspectorWorkflowServices services,
        string? body)
    {
        if (!TryReadWorkbenchRepairRequest(body, out WorkbenchRepairProposeRequest? request, out var error))
            return JsonResponse(400, new { ok = false, error });

        var classification = GetWorkbenchRepairClassification(request!.ClassificationToken);
        if (classification is null)
        {
            return JsonResponse(409, new
            {
                ok = false,
                error = "Repair eligibility must be freshly classified by this Inspector target before candidate generation."
            });
        }
        if (!string.IsNullOrWhiteSpace(classification.ReportRunId) &&
            !string.Equals(classification.ReportRunId, request.Input?.SourceRunId, StringComparison.Ordinal))
        {
            return JsonResponse(409, new { ok = false, error = "The proposal source run does not match the classified repair." });
        }
        request.Input ??= new Testing.MauiFlowRepairProposalGenerationInput();
        request.Input.Eligibility = classification.Decision;
        request.Input.PriorActiveSelectorResolution = classification.PriorActiveSelectorResolution;
        var generated = Testing.MauiFlowRepairProposalGenerator.Generate(request!.Input);
        var trustedContext = new WorkflowRepairTrustedContext
        {
            Eligibility = classification.Decision,
            ClassifiedCheckpoint = classification.Decision.CurrentCheckpoint,
        };
        var stored = new List<WorkflowRepairProposalSnapshot>();
        foreach (var proposal in generated.Proposals)
        {
            var result = services.ProposeRepair(
                proposal,
                trustedContext,
                request.AgentOriginated);
            if (!result.Ok || result.Proposal is null)
            {
                return JsonResponse(409, new
                {
                    ok = false,
                    error = result.Error,
                    code = result.Code,
                    generation = generated,
                });
            }
            stored.Add(result.Proposal);
        }

        var history = new List<WorkflowRepairHistoryAppendResult>();
        foreach (var proposal in stored)
        {
            var appended = await AppendRepairHistoryAsync(proposal, Testing.MauiFlowRepairOutcomeStates.Proposed)
                .ConfigureAwait(false);
            if (appended is not null)
                history.Add(appended);
        }
        return JsonResponse(200, new
        {
            ok = stored.Count > 0,
            generation = generated,
            proposals = stored,
            history,
            automaticApply = false,
            sourceWrite = false,
            repairValidationAvailable = _repairValidationAvailable,
        });
    }

    private (int, string, byte[]) HandleWorkbenchRepairGrant(
        InspectorWorkflowServices services,
        string? body)
    {
        if (!TryReadWorkbenchRepairRequest(body, out WorkbenchRepairGrantRequest? request, out var error))
            return JsonResponse(400, new { ok = false, error });
        if (!request!.HumanConfirmed)
        {
            return JsonResponse(400, new
            {
                ok = false,
                error = "Explicit human confirmation is required before issuing a repair grant."
            });
        }

        var lookup = services.GetRepair(request.ProposalId ?? string.Empty);
        if (!lookup.Ok || lookup.Proposal is null)
            return JsonResponse(404, new { ok = false, code = lookup.Code, error = lookup.Error });

        var binding = CreateRepairBinding(services, lookup.Proposal, request.Policy);
        var issued = services.IssueRepairGrant(new WorkflowRepairGrantIssueRequest
        {
            ProposalId = request.ProposalId,
            Kind = request.Kind,
            Reviewer = request.Reviewer,
            HumanConfirmed = request.HumanConfirmed,
            ExpiresAt = request.ExpiresAt,
            Binding = binding,
        });
        return JsonResponse(issued.Ok ? 200 : 409, new
        {
            ok = issued.Ok,
            grant = issued.Grant,
            grantDigest = issued.GrantDigest,
            expiresAt = issued.ExpiresAt,
            proposal = issued.Proposal,
            code = issued.Code,
            error = issued.Error,
        });
    }

    private (int, string, byte[]) HandleWorkbenchRepairStatus(
        InspectorWorkflowServices services,
        string proposalId)
    {
        var result = services.GetRepair(proposalId);
        return JsonResponse(result.Ok ? 200 : 404, new
        {
            ok = result.Ok,
            proposal = result.Proposal,
            code = result.Code,
            error = result.Error,
            repairValidationAvailable = _repairValidationAvailable,
        });
    }

    private async Task<(int, string, byte[])> HandleWorkbenchRepairPreviewAsync(
        InspectorWorkflowServices services,
        string proposalId)
    {
        var result = services.PreviewRepair(proposalId);
        var history = result.Ok
            ? await AppendRepairHistoryAsync(result.Proposal, Testing.MauiFlowRepairOutcomeStates.Previewed)
                .ConfigureAwait(false)
            : null;
        return JsonResponse(result.Ok ? 200 : 409, new
        {
            ok = result.Ok,
            proposal = result.Proposal,
            history,
            code = result.Code,
            error = result.Error,
            selectorOnly = result.Proposal?.Proposal.Patch?.SelectorOnly == true,
            repairValidationAvailable = _repairValidationAvailable,
        });
    }

    private async Task<(int, string, byte[])> HandleWorkbenchRepairRejectAsync(
        InspectorWorkflowServices services,
        string proposalId,
        string? body)
    {
        if (!TryReadWorkbenchRepairRequest(body, out WorkbenchRepairRejectRequest? request, out var error))
            return JsonResponse(400, new { ok = false, error });
        var result = services.RejectRepair(proposalId, request!.Reviewer, request.ReasonCode);
        var history = result.Ok
            ? await AppendRepairHistoryAsync(result.Proposal, Testing.MauiFlowRepairOutcomeStates.Rejected)
                .ConfigureAwait(false)
            : null;
        return JsonResponse(result.Ok ? 200 : 409, new
        {
            ok = result.Ok,
            proposal = result.Proposal,
            history,
            code = result.Code,
            error = result.Error,
            repairValidationAvailable = _repairValidationAvailable,
        });
    }

    private async Task<(int, string, byte[])> HandleWorkbenchRepairValidateAsync(
        InspectorWorkflowServices services,
        string proposalId,
        string? body)
    {
        if (!TryReadWorkbenchRepairRequest(body, out WorkbenchRepairValidationRequest? request, out var error))
            return JsonResponse(400, new { ok = false, error });
        if (!_repairValidationAvailable)
        {
            return JsonResponse(503, new
            {
                ok = false,
                error = "No platform lifecycle host is connected for transient repair validation. No flow was changed.",
                hostFallback = true,
            });
        }

        var lookup = services.GetRepair(proposalId);
        if (!lookup.Ok || lookup.Proposal is null)
            return JsonResponse(404, new { ok = false, code = lookup.Code, error = lookup.Error });
        var validation = await _repairValidation.ValidateAsync(
            new WorkflowRepairTransientValidationRequest
            {
                Proposal = lookup.Proposal.Proposal,
                ReplaySafety = request!.ReplaySafety,
                ValidationGrantDigest = request.ValidationGrant,
                InMemorySelectorOverrideOnly = true,
                AllowDownstreamContinuation = request.ReplaySafety?.DownstreamContinuationAllowed == true,
            },
            _lifetimeCts.Token).ConfigureAwait(false);
        var recorded = services.RecordRepairValidation(proposalId, request.ValidationGrant ?? string.Empty, validation);
        var history = recorded.Ok
            ? await AppendRepairHistoryAsync(recorded.Proposal, Testing.MauiFlowRepairOutcomeStates.Previewed)
                .ConfigureAwait(false)
            : null;
        return JsonResponse(recorded.Ok ? 200 : 409, new
        {
            ok = recorded.Ok,
            proposal = recorded.Proposal,
            validation,
            history,
            code = recorded.Code,
            error = recorded.Error,
            flowChanged = false,
            repairValidationAvailable = _repairValidationAvailable,
        });
    }

    private async Task<(int, string, byte[])> HandleWorkbenchRepairApproveAsync(
        InspectorWorkflowServices services,
        string proposalId,
        string? body)
    {
        if (!TryReadWorkbenchRepairRequest(body, out WorkbenchRepairGrantRequest? request, out var error))
            return JsonResponse(400, new { ok = false, error });
        if (!request!.HumanConfirmed)
        {
            return JsonResponse(400, new
            {
                ok = false,
                error = "Explicit human confirmation is required before approval."
            });
        }
        var lookup = services.GetRepair(proposalId);
        if (!lookup.Ok || lookup.Proposal is null)
            return JsonResponse(404, new { ok = false, code = lookup.Code, error = lookup.Error });

        var issued = services.IssueRepairGrant(new WorkflowRepairGrantIssueRequest
        {
            ProposalId = proposalId,
            Kind = WorkflowRepairGrantKinds.Apply,
            Reviewer = request.Reviewer,
            HumanConfirmed = true,
            ExpiresAt = request.ExpiresAt,
            Binding = CreateRepairBinding(services, lookup.Proposal, request.Policy),
        });
        var history = issued.Ok
            ? await AppendRepairHistoryAsync(issued.Proposal, Testing.MauiFlowRepairOutcomeStates.Approved)
                .ConfigureAwait(false)
            : null;
        return JsonResponse(issued.Ok ? 200 : 409, new
        {
            ok = issued.Ok,
            grant = issued.Grant,
            grantDigest = issued.GrantDigest,
            expiresAt = issued.ExpiresAt,
            proposal = issued.Proposal,
            history,
            code = issued.Code,
            error = issued.Error,
            repairValidationAvailable = _repairValidationAvailable,
        });
    }

    private async Task<(int, string, byte[])> HandleWorkbenchRepairApplyAsync(
        InspectorWorkflowServices services,
        string proposalId,
        string? body)
    {
        if (!TryReadWorkbenchRepairRequest(body, out WorkbenchRepairApplyRequest? request, out var error))
            return JsonResponse(400, new { ok = false, error });
        var store = await ResolveWorkflowPlanStoreAsync().ConfigureAwait(false);
        if (store is null)
        {
            return JsonResponse(503, new
            {
                ok = false,
                error = "A trusted project workspace is required before an approved repair can be applied."
            });
        }

        var lookup = services.GetRepair(proposalId);
        if (!lookup.Ok || lookup.Proposal is null)
            return JsonResponse(404, new { ok = false, code = lookup.Code, error = lookup.Error });
        var binding = CreateRepairBinding(services, lookup.Proposal, request!.Policy);
        var begun = services.BeginRepairApply(proposalId, request.ApprovalGrant ?? string.Empty, binding);
        if (!begun.Ok || begun.Proposal is null)
        {
            return JsonResponse(409, new
            {
                ok = false,
                proposal = begun.Proposal,
                code = begun.Code,
                error = begun.Error,
            });
        }

        var applied = store.ApplySelectorRepair(new WorkflowRepairFlowApplyRequest
        {
            Proposal = begun.Proposal.Proposal,
            ExpectedFlowDigest = binding.FlowDigest,
            ExpectedFlowRevision = binding.FlowRevision,
            Reviewer = begun.Proposal.Reviewer,
            GrantDigest = begun.Proposal.GrantDigest,
            ValidationRunIds = begun.Proposal.ValidationRunIds,
        });
        var completed = services.CompleteRepairApply(proposalId, new WorkflowRepairApplyRecord
        {
            Applied = applied.Ok,
            NewFlowRevision = applied.FlowRevision,
            AppliedFlowDigest = applied.FlowDigest,
            ErrorCode = applied.Code,
            Error = applied.Error,
        });
        return JsonResponse(applied.Ok && completed.Ok ? 200 : 409, new
        {
            ok = applied.Ok && completed.Ok,
            proposal = completed.Proposal,
            apply = applied,
            code = completed.Code ?? applied.Code,
            error = completed.Error ?? applied.Error,
        });
    }

    private async Task<(int, string, byte[])> HandleWorkbenchRepairVerifyAsync(
        InspectorWorkflowServices services,
        string proposalId,
        string? body)
    {
        if (!TryReadWorkbenchRepairRequest(body, out WorkbenchRepairVerificationRequest? request, out var error))
            return JsonResponse(400, new { ok = false, error });
        if (!request!.HumanConfirmed)
        {
            return JsonResponse(400, new
            {
                ok = false,
                error = "Explicit human confirmation is required to record host verification results."
            });
        }

        var result = services.RecordRepairVerification(proposalId, request.VerificationRuns ?? []);
        var history = result.Ok
            ? await AppendRepairHistoryAsync(result.Proposal, result.Proposal?.State ?? Testing.MauiFlowRepairOutcomeStates.RollbackRequired)
                .ConfigureAwait(false)
            : null;
        return JsonResponse(result.Ok ? 200 : 409, new
        {
            ok = result.Ok,
            proposal = result.Proposal,
            history,
            code = result.Code,
            error = result.Error,
            requiredCleanReplays = 3,
        });
    }

    private async Task<(int, string, byte[])> HandleWorkbenchRepairRollbackAsync(
        InspectorWorkflowServices services,
        string proposalId,
        string? body)
    {
        if (!TryReadWorkbenchRepairRequest(body, out WorkbenchRepairRollbackRequest? request, out var error))
            return JsonResponse(400, new { ok = false, error });
        var store = await ResolveWorkflowPlanStoreAsync().ConfigureAwait(false);
        if (store is null)
            return JsonResponse(503, new { ok = false, error = "A trusted project workspace is required for rollback." });

        var lookup = services.GetRepair(proposalId);
        if (!lookup.Ok || lookup.Proposal is null)
            return JsonResponse(404, new { ok = false, code = lookup.Code, error = lookup.Error });
        var binding = CreateRepairBinding(services, lookup.Proposal, request!.Policy);
        var begun = services.BeginRepairRollback(proposalId, request.RollbackGrant ?? string.Empty, binding);
        if (!begun.Ok || begun.Proposal is null)
            return JsonResponse(409, new { ok = false, proposal = begun.Proposal, code = begun.Code, error = begun.Error });

        var reverted = store.RollbackSelectorRepair(new WorkflowRepairFlowRollbackRequest
        {
            Proposal = begun.Proposal.Proposal,
            ExpectedAppliedFlowDigest = begun.Proposal.AppliedFlowDigest,
            ExpectedAppliedFlowRevision = begun.Proposal.NewFlowRevision,
            Reviewer = begun.Proposal.Reviewer,
            GrantDigest = begun.Proposal.GrantDigest,
            VerificationRunIds = begun.Proposal.VerificationRunIds,
        });
        var completed = services.CompleteRepairRollback(proposalId, new WorkflowRepairRollbackRecord
        {
            Reverted = reverted.Ok,
            RollbackRevision = reverted.FlowRevision,
            ErrorCode = reverted.Code,
            Error = reverted.Error,
        });
        return JsonResponse(reverted.Ok && completed.Ok ? 200 : 409, new
        {
            ok = reverted.Ok && completed.Ok,
            proposal = completed.Proposal,
            rollback = reverted,
            code = completed.Code ?? reverted.Code,
            error = completed.Error ?? reverted.Error,
        });
    }

    private async Task<WorkflowRepairHistoryAppendResult?> AppendRepairHistoryAsync(
        WorkflowRepairProposalSnapshot? snapshot,
        string state)
    {
        if (snapshot is null)
            return null;
        var store = await ResolveWorkflowPlanStoreAsync().ConfigureAwait(false);
        if (store is null)
        {
            return WorkflowRepairHistoryAppendResult.Failure(
                "workspace-unavailable",
                "A trusted workspace is unavailable for repair history persistence.");
        }
        return store.AppendRepairHistory(new WorkflowRepairHistoryAppendRequest
        {
            Proposal = snapshot.Proposal,
            State = state,
            NewFlowRevision = snapshot.NewFlowRevision,
            RollbackRevision = snapshot.RollbackRevision,
            Reviewer = snapshot.Reviewer,
            GrantDigest = snapshot.GrantDigest,
            ValidationRunIds = snapshot.ValidationRunIds,
            VerificationRunIds = snapshot.VerificationRunIds,
            ReasonCode = snapshot.ReasonCode,
        });
    }

    private static WorkflowRepairGrantBinding CreateRepairBinding(
        InspectorWorkflowServices services,
        WorkflowRepairProposalSnapshot snapshot,
        string? requestedPolicy)
    {
        var baseFlow = snapshot.BaseFlow ?? snapshot.Proposal.BaseFlow;
        return new WorkflowRepairGrantBinding
        {
            FlowPath = baseFlow?.Path,
            FlowDigest = snapshot.AppliedFlowDigest ?? baseFlow?.Digest,
            FlowRevision = snapshot.NewFlowRevision ?? baseFlow?.Revision,
            PatchDigest = snapshot.PatchDigest,
            TargetId = services.Target.AgentId + ":" + services.Target.AgentInstanceId,
            Policy = string.IsNullOrWhiteSpace(requestedPolicy) ? "repair-policy-v1" : requestedPolicy.Trim(),
        };
    }

    private static bool TryReadWorkbenchRepairRequest<T>(
        string? body,
        out T? request,
        out string? error)
        where T : class
    {
        request = null;
        error = null;
        if (string.IsNullOrWhiteSpace(body))
        {
            error = "A JSON repair request body is required.";
            return false;
        }
        try
        {
            request = JsonSerializer.Deserialize<T>(body, CamelCase);
            if (request is null)
            {
                error = "The repair request is invalid.";
                return false;
            }
            return true;
        }
        catch (JsonException)
        {
            error = "The repair request is not valid JSON.";
            return false;
        }
    }

    private async Task<(int, string, byte[])> HandleWorkbenchImproveAnalyzeAsync(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return JsonResponse(400, new { ok = false, error = "A flow is required for selector-health analysis." });

        Testing.MauiFlow? flow;
        Testing.MauiTestPlan? plan = null;
        var reports = new List<Testing.MauiFlowRunReport>();
        var includeLiveTree = true;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("flow", out var flowElement) ||
                flowElement.ValueKind != JsonValueKind.Object)
            {
                return JsonResponse(400, new { ok = false, error = "A flow object is required." });
            }

            flow = JsonSerializer.Deserialize(
                flowElement.GetRawText(),
                Testing.MauiFlowJsonContext.Default.MauiFlow);
            if (flow is null)
                return JsonResponse(400, new { ok = false, error = "The flow is not valid." });

            if (document.RootElement.TryGetProperty("plan", out var planElement) &&
                planElement.ValueKind == JsonValueKind.Object)
            {
                plan = JsonSerializer.Deserialize(
                    planElement.GetRawText(),
                    Testing.MauiTestingJsonContext.Default.MauiTestPlan);
            }
            if (document.RootElement.TryGetProperty("includeLiveTree", out var liveTreeElement) &&
                liveTreeElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                includeLiveTree = liveTreeElement.GetBoolean();
            }
            if (document.RootElement.TryGetProperty("runHistory", out var historyElement) &&
                historyElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var reportElement in historyElement.EnumerateArray().Take(16))
                {
                    if (reportElement.ValueKind != JsonValueKind.Object)
                        continue;
                    var report = JsonSerializer.Deserialize(
                        reportElement.GetRawText(),
                        Testing.MauiTestingJsonContext.Default.MauiFlowRunReport);
                    if (report is not null)
                        reports.Add(report);
                }
            }
        }
        catch (JsonException)
        {
            return JsonResponse(400, new { ok = false, error = "The selector-health request is not valid JSON." });
        }

        try
        {
            var status = await _client.GetStatusAsync().ConfigureAwait(false);
            var context = new MauiSelectorObservationContext
            {
                AppId = SafeWorkbenchText(status?.App?.PackageId ?? status?.App?.Name),
                AppBuild = SafeWorkbenchText(status?.App?.Build),
                Platform = SafeWorkbenchText(status?.Device?.Platform),
                Route = SafeWorkbenchText(status?.Route),
                Window = SafeWorkbenchText(status?.Window),
                Modal = SafeWorkbenchText(status?.Modal),
                Locale = SafeWorkbenchText(status?.Locale),
                Theme = SafeWorkbenchText(status?.Theme),
                Orientation = SafeWorkbenchText(status?.Orientation),
                DisplayProfile = SafeWorkbenchText(status?.DisplayProfile),
                CapabilityVersion = "inspector-selector-health-v1",
                ObservedAt = DateTimeOffset.UtcNow,
            };

            List<MauiSelectorObservationElement> liveElements = [];
            var liveTreeTruncated = false;
            if (includeLiveTree)
            {
                var tree = await _client.GetTreeAsync().ConfigureAwait(false);
                if (tree.Count > 0)
                {
                    var observation = Testing.MauiSelectorObservationFactory.Create(tree[0], tree, context);
                    liveTreeTruncated = observation.Elements.Count > 2_000;
                    liveElements = observation.Elements.Take(2_000).ToList();
                }
            }

            var platforms = reports
                .GroupBy(report => report.Target?.Platform, StringComparer.OrdinalIgnoreCase)
                .Where(static group => !string.IsNullOrWhiteSpace(group.Key))
                .Select(group => new Testing.MauiSelectorHealthPlatformSnapshot
                {
                    Platform = group.Key,
                    Fingerprints = group.SelectMany(report => report.Steps)
                        .Select(static step => step.Fingerprint)
                        .Where(static fingerprint => fingerprint is not null)
                        .Cast<Testing.MauiElementFingerprint>()
                        .ToList(),
                    Candidates = group.SelectMany(report => report.Steps)
                        .SelectMany(static step => step.SelectorCandidates)
                        .ToList(),
                })
                .ToList();
            var analysis = Testing.MauiSelectorHealthAnalyzer.Analyze(new Testing.MauiSelectorHealthAnalysisInput
            {
                Flow = flow!,
                Plan = plan,
                LiveElements = liveElements,
                Context = context,
                PlatformSnapshots = platforms,
                RunHistory = reports,
                LiveTreeComplete = !liveTreeTruncated,
            });
            return JsonResponse(200, new
            {
                ok = true,
                analysis,
                liveTree = new
                {
                    requested = includeLiveTree,
                    available = includeLiveTree && liveElements.Count > 0,
                    truncated = liveTreeTruncated,
                    elementCount = liveElements.Count,
                },
            });
        }
        catch (Exception exception) when (IsAgentUnavailableException(exception))
        {
            return JsonResponse(503, new { ok = false, error = "The selected agent is unavailable for live selector analysis." });
        }
    }

    private async Task<(int, string, byte[])> HandleWorkbenchTargetAsync(InspectorWorkflowServices services)
    {
        if (!services.IsTargetCurrent())
            return JsonResponse(409, new { ok = false, error = "The selected agent instance is no longer connected." });

        try
        {
            var status = await _client.GetStatusAsync().ConfigureAwait(false);
            if (status is null)
                return JsonResponse(503, new { ok = false, error = "The selected agent did not return target status." });

            var target = services.Target;
            var capabilities = services.GetCapabilities();
            return JsonResponse(200, new
            {
                ok = true,
                broker = new
                {
                    schema = capabilities.Schema,
                    supported = capabilities.Supported,
                    requiresExplicitAgentInstance = capabilities.RequiresExplicitAgentInstance,
                    requiresIdempotencyKey = capabilities.RequiresIdempotencyKey,
                    capabilityTokenRequired = capabilities.CapabilityTokenRequired,
                    states = capabilities.States,
                    maxTimeoutMs = capabilities.MaxTimeoutMs,
                    maxSteps = capabilities.MaxSteps,
                    workflowCommandLedger = capabilities.WorkflowCommandLedger,
                    repairValidationAvailable = _repairValidationAvailable,
                },
                target = new
                {
                    agentId = target.AgentId,
                    agentInstanceId = target.AgentInstanceId,
                    agentPort = target.AgentPort,
                    appName = SafeWorkbenchText(status.App?.Name ?? target.AppName),
                    platform = SafeWorkbenchText(status.Device?.Platform ?? target.Platform),
                    app = new
                    {
                        build = SafeWorkbenchText(status.App?.Build),
                        packageId = SafeWorkbenchText(status.App?.PackageId),
                        version = SafeWorkbenchText(status.App?.Version),
                    },
                    device = new
                    {
                        deviceType = SafeWorkbenchText(status.Device?.DeviceType),
                        idiom = SafeWorkbenchText(status.Device?.Idiom),
                    },
                    observedCheckpoint = new
                    {
                        agentInstanceId = target.AgentInstanceId,
                        appBuildFingerprint = SafeWorkbenchText(status.App?.Build),
                        route = SafeWorkbenchText(status.Route),
                        window = SafeWorkbenchText(status.Window),
                        modal = SafeWorkbenchText(status.Modal),
                        locale = SafeWorkbenchText(status.Locale),
                        theme = SafeWorkbenchText(status.Theme),
                        orientation = SafeWorkbenchText(status.Orientation),
                        displayProfile = SafeWorkbenchText(status.DisplayProfile),
                    },
                    capabilities = status.Capabilities,
                },
            });
        }
        catch (Exception exception) when (IsAgentUnavailableException(exception))
        {
            return JsonResponse(503, new { ok = false, error = "The selected agent is unavailable." });
        }
    }

    private async Task<(int, string, byte[])> HandleWorkbenchPreflightAsync(
        InspectorWorkflowServices services,
        string? body)
    {
        if (!TryReadWorkbenchRunEnvelope(body, out var envelope, out var error))
            return JsonResponse(400, new { ok = false, error });

        var enriched = await EnrichWorkbenchRunRequestAsync(envelope!.Run!, services).ConfigureAwait(false);
        if (enriched.Error is not null)
            return JsonResponse(enriched.StatusCode, new { ok = false, error = enriched.Error });

        var preflight = services.Preflight(enriched.Request!);
        return JsonResponse(preflight.StatusCode, preflight);
    }

    private async Task<(int, string, byte[])> HandleWorkbenchRunStartAsync(
        InspectorWorkflowServices services,
        string? body,
        string leaseId,
        string holderKind,
        string holderLabel)
    {
        if (Interlocked.CompareExchange(ref _workbenchRunStarting, 1, 0) != 0)
            return JsonResponse(409, new { ok = false, error = "A workflow run is already starting." });

        CancellationTokenSource? heartbeatCancellation = null;
        Task? heartbeatTask = null;
        try
        {
            if (!TryReadWorkbenchRunEnvelope(body, out var envelope, out var error))
                return JsonResponse(400, new { ok = false, error });
            Volatile.Write(
                ref _workbenchRunStartingIdempotencyKey,
                envelope!.Run!.IdempotencyKey);

            var claim = await _client.ControlMutationLeaseAsync(
                "claim",
                force: false,
                leaseId,
                holderKind,
                holderLabel).ConfigureAwait(false);
            if (!claim.YouHold)
            {
                return JsonResponse(409, new
                {
                    ok = false,
                    reason = "writer",
                    error = "Another session is driving this app.",
                    holderKind = claim.HolderKind,
                    label = claim.Label,
                    expiresInMs = claim.ExpiresInMs,
                    authority = claim.Authority,
                });
            }

            heartbeatCancellation = new CancellationTokenSource();
            heartbeatTask = HeartbeatStartingLeaseAsync(
                leaseId,
                holderKind,
                holderLabel,
                heartbeatCancellation.Token);

            var enriched = await EnrichWorkbenchRunRequestAsync(envelope.Run, services).ConfigureAwait(false);
            if (enriched.Error is not null)
                return JsonResponse(enriched.StatusCode, new { ok = false, error = enriched.Error });

            var consent = WorkbenchEvidenceConsent.From(envelope.Evidence);
            var result = services.Start(
                enriched.Request!,
                client => new InspectorReplayEvidenceCapture(
                    client,
                    consent,
                    StoreWorkbenchEvidence),
                new WorkflowRunLeaseHandoff(leaseId, holderKind, holderLabel));
            if (result.Ok && result.Run is not null && !string.IsNullOrWhiteSpace(result.CapabilityToken))
            {
                RememberWorkbenchRun(
                    result.Run.RunId,
                    result.CapabilityToken!,
                    consent,
                    enriched.Request!.IdempotencyKey);
                if (!result.Run.Terminal)
                    Volatile.Write(ref _activeWorkbenchRunId, result.Run.RunId);
            }

            return JsonResponse(result.StatusCode, result);
        }
        finally
        {
            if (heartbeatCancellation is not null)
            {
                heartbeatCancellation.Cancel();
                if (heartbeatTask is not null)
                {
                    try { await heartbeatTask.ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }
                heartbeatCancellation.Dispose();
            }
            Volatile.Write(ref _workbenchRunStartingIdempotencyKey, null);
            Volatile.Write(ref _workbenchRunStarting, 0);
        }
    }

    private async Task HeartbeatStartingLeaseAsync(
        string leaseId,
        string holderKind,
        string holderLabel,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            MutationLeaseStatus status;
            try
            {
                status = await _client.ControlMutationLeaseAsync(
                    "heartbeat",
                    force: false,
                    leaseId,
                    holderKind,
                    holderLabel).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsAgentUnavailableException(exception))
            {
                return;
            }
            if (!status.YouHold)
                return;
        }
    }

    private (int, string, byte[]) HandleWorkbenchRunStatus(
        InspectorWorkflowServices services,
        string runId,
        string? body)
    {
        if (!TryReadWorkbenchAccess(body, out var access, out var error))
            return JsonResponse(400, new { ok = false, error });

        var token = ResolveWorkbenchRunToken(runId, access!.CapabilityToken);
        var result = services.GetRunStatus(runId, token);
        if (result.Run is not null && !string.IsNullOrWhiteSpace(token))
            RememberWorkbenchRun(runId, token!, null);
        if (result.Run?.Terminal == true)
            ClearActiveWorkbenchRun(runId);

        return JsonResponse(
            result.StatusCode,
            result.Run is null
                ? WorkflowRunStatusResponse.Failure(result.Error ?? "Workflow run was not found.")
                : WorkflowRunStatusResponse.Success(result.Run));
    }

    private (int, string, byte[]) HandleWorkbenchRunCancel(
        InspectorWorkflowServices services,
        string runId,
        string? body)
    {
        if (!TryReadWorkbenchAccess(body, out var access, out var error))
            return JsonResponse(400, new { ok = false, error });

        var token = ResolveWorkbenchRunToken(runId, access!.CapabilityToken);
        var result = services.CancelRun(runId, token);
        if (result.Run is not null && !string.IsNullOrWhiteSpace(token))
            RememberWorkbenchRun(runId, token!, null);
        if (result.Run?.Terminal == true)
            ClearActiveWorkbenchRun(runId);
        return JsonResponse(result.StatusCode, result);
    }

    private (int, string, byte[]) HandleWorkbenchRunJournal(
        InspectorWorkflowServices services,
        string? idempotencyKey)
    {
        var pending = !string.IsNullOrWhiteSpace(idempotencyKey) &&
            string.Equals(
                Volatile.Read(ref _workbenchRunStartingIdempotencyKey),
                idempotencyKey,
                StringComparison.Ordinal);
        List<WorkbenchRunCapability> known;
        lock (_workbenchRunGate)
        {
            known = _workbenchRunCapabilities.Values
                .Where(entry => string.IsNullOrWhiteSpace(idempotencyKey) ||
                    string.Equals(entry.IdempotencyKey, idempotencyKey, StringComparison.Ordinal))
                .OrderBy(entry => entry.CreatedAt)
                .ToList();
        }

        WorkflowRunSnapshot? latestTerminal = null;
        foreach (var knownRun in known.OrderByDescending(entry => entry.CreatedAt))
        {
            var result = services.GetRunStatus(knownRun.RunId, knownRun.CapabilityToken);
            if (result.StatusCode == 404)
            {
                ForgetWorkbenchRun(knownRun.RunId);
                continue;
            }

            if (result.Run is null)
                continue;
            if (!result.Run.Terminal)
                return JsonResponse(200, new { ok = true, restored = true, pending = false, run = result.Run });
            latestTerminal ??= result.Run;
        }

        return JsonResponse(200, new { ok = true, restored = false, pending, run = latestTerminal });
    }

    private async Task<(int, string, byte[])> HandleWorkbenchAgentHandoffAsync(
        InspectorWorkflowServices services,
        string? body)
    {
        if (_testAgentSessions is null || _testAgentTargetStateRefresh is null)
            return JsonResponse(501, new { ok = false, error = "Restricted test-agent handoff is unavailable in this host." });

        WorkbenchAgentHandoffRequest? request;
        try
        {
            request = string.IsNullOrWhiteSpace(body)
                ? null
                : JsonSerializer.Deserialize<WorkbenchAgentHandoffRequest>(body, WorkbenchJsonOptions);
        }
        catch (JsonException)
        {
            return JsonResponse(400, new { ok = false, error = "The agent handoff request is not valid JSON." });
        }

        if (request is null || string.IsNullOrWhiteSpace(request.RunId))
            return JsonResponse(400, new { ok = false, error = "A semantic flow and exact failed run are required." });

        var flow = request.Flow;
        if (!string.IsNullOrWhiteSpace(request.Markdown))
        {
            var parsed = Testing.FlowMarkdown.Parse(request.Markdown);
            if (!parsed.Ok || parsed.Flow is null)
                return JsonResponse(400, new { ok = false, error = parsed.Error ?? "The loaded Markdown test is invalid." });
            flow = parsed.Flow;
        }
        if (flow is null)
            return JsonResponse(400, new { ok = false, error = "A semantic flow and exact failed run are required." });

        var runToken = ResolveWorkbenchRunToken(request.RunId, request.CapabilityToken);
        var access = services.GetRunStatus(request.RunId, runToken);
        if (access.Run is null)
            return JsonResponse(access.StatusCode, new { ok = false, error = access.Error ?? "The failed run is unavailable." });
        if (access.Run.Terminal != true || access.Run.Report?.Failure is null)
            return JsonResponse(409, new { ok = false, error = "Agent diagnosis is available only for a terminal failed local run." });
        if (string.IsNullOrWhiteSpace(runToken))
            return JsonResponse(403, new { ok = false, error = "The retained run read capability is unavailable." });
        if (!string.Equals(access.Run.Target.AgentId, _agentId, StringComparison.Ordinal) ||
            !string.Equals(access.Run.Target.AgentInstanceId, _agentInstanceId, StringComparison.Ordinal))
        {
            return JsonResponse(409, new { ok = false, error = "The failed run belongs to a different app instance." });
        }

        var flowDigest = Testing.MauiFlowRunReportSerializer.ComputeFlowDigest(flow);
        if (!string.Equals(flowDigest, access.Run.FlowDigest, StringComparison.Ordinal))
            return JsonResponse(409, new { ok = false, error = "The loaded test no longer matches the failed run." });
        if (!string.IsNullOrWhiteSpace(request.Plan?.Flow?.Digest) &&
            !string.Equals(request.Plan.Flow.Digest, flowDigest, StringComparison.Ordinal))
        {
            return JsonResponse(409, new { ok = false, error = "The loaded plan no longer matches the failed run." });
        }

        var planDigest = request.Plan is null
            ? "none"
            : TestAgentSessionService.ComputePlanDigest(request.Plan);
        var key = $"{request.RunId}\n{flowDigest}\n{planDigest}";
        WorkbenchAgentHandoff? existing;
        lock (_workbenchRunGate)
            _workbenchAgentHandoffs.TryGetValue(key, out existing);
        if (existing is not null && existing.ExpiresAt > DateTimeOffset.UtcNow)
        {
            var readable = _testAgentSessions.GetSnapshotForRead(existing.SessionId, existing.ReadCapabilityId);
            if (readable.Ok)
                return JsonResponse(200, new { ok = true, context = existing.Context });
            lock (_workbenchRunGate)
                _workbenchAgentHandoffs.Remove(key);
        }

        if (string.IsNullOrWhiteSpace(_agentId) || string.IsNullOrWhiteSpace(_agentInstanceId))
            return JsonResponse(409, new { ok = false, error = "The Inspector does not have an exact test-agent target." });

        var targetState = await _testAgentTargetStateRefresh(new Testing.MauiTestAgentTargetState
        {
            AgentId = _agentId,
            AgentInstanceId = _agentInstanceId,
        }).ConfigureAwait(false);
        if (targetState is null)
            return JsonResponse(409, new { ok = false, error = "The exact app instance is no longer available for agent handoff." });

        var target = new Testing.MauiTestAgentTarget
        {
            AgentId = targetState.AgentId,
            AgentInstanceId = targetState.AgentInstanceId,
            AppBuildFingerprint = targetState.AppBuildFingerprint,
            SeedFingerprint = targetState.SeedFingerprint,
            BackendStateFingerprint = targetState.BackendStateFingerprint,
        };
        var handoffId = Guid.NewGuid().ToString("N");
        var begun = _testAgentSessions.Begin(new Testing.MauiTestAgentSessionBeginRequest
        {
            Envelope = new Testing.MauiTestAgentRequestEnvelope
            {
                RequestId = $"handoff-begin-{handoffId}",
                IdempotencyKey = $"handoff-begin-{handoffId}",
                Target = target,
                Correlation = new Testing.MauiTestAgentCorrelation
                {
                    PlanId = request.Plan?.PlanId,
                    PlanRevision = request.Plan?.Revision,
                    FlowId = request.Plan?.Flow?.FlowId,
                    FlowRevision = request.Plan?.Flow?.Revision,
                    FlowDigest = flowDigest,
                    RunId = request.RunId,
                },
                Provenance = HandoffProvenance(),
                Intent = "Diagnose the exact failed Inspector test run and prepare only an eligible inert selector proposal.",
                DeadlineMs = 30_000,
                PolicyVersion = Testing.MauiTestAgentProtocolVersions.PolicyVersion,
            },
            TargetState = targetState,
            Plan = request.Plan,
            Flow = flow,
            DurationSeconds = 600,
        });
        var snapshot = begun.Snapshot;
        if (!begun.Ok || snapshot is null ||
            string.IsNullOrWhiteSpace(snapshot.SessionId) ||
            string.IsNullOrWhiteSpace(snapshot.ReadCapabilityId))
        {
            return JsonResponse(409, new
            {
                ok = false,
                error = begun.Error?.Message ?? "The restricted diagnostic handoff could not be created.",
            });
        }

        var bound = _testAgentSessions.BindRun(new Testing.MauiTestAgentRunBindingRequest
        {
            SessionId = snapshot.SessionId,
            ReadCapabilityId = snapshot.ReadCapabilityId,
            RunId = request.RunId,
            RunCapabilityToken = runToken,
        });
        if (!bound.Ok)
        {
            return JsonResponse(409, new
            {
                ok = false,
                error = bound.Error?.Message ?? "The failed run could not be bound to the diagnostic handoff.",
            });
        }

        var failureEnvelope = HandoffEnvelope(snapshot, target, request.RunId, "failure", handoffId);
        var improvementsEnvelope = HandoffEnvelope(snapshot, target, request.RunId, "improvements", handoffId);
        var patchEnvelope = HandoffEnvelope(snapshot, target, request.RunId, "patch", handoffId);
        var context = new WorkbenchAgentHandoffContext
        {
            TestName = request.FlowName ?? request.Plan?.Flow?.Path ?? flow.Name,
            RunId = request.RunId,
            ExpiresAt = snapshot.ExpiresAt,
            FailureRequest = new Testing.MauiTestAgentTraceRequest
            {
                Envelope = failureEnvelope,
                RunId = request.RunId,
                RunCapabilityToken = runToken,
            },
            ImprovementsEnvelope = improvementsEnvelope,
            PatchEnvelope = patchEnvelope,
        };
        var handoff = new WorkbenchAgentHandoff(
            snapshot.SessionId,
            snapshot.ReadCapabilityId,
            snapshot.ExpiresAt ?? DateTimeOffset.UtcNow.AddMinutes(10),
            context);
        lock (_workbenchRunGate)
        {
            _workbenchAgentHandoffs[key] = handoff;
            while (_workbenchAgentHandoffs.Count > MaxRetainedWorkbenchAgentHandoffs)
            {
                var oldest = _workbenchAgentHandoffs.OrderBy(static pair => pair.Value.ExpiresAt).First();
                _workbenchAgentHandoffs.Remove(oldest.Key);
            }
        }

        return JsonResponse(200, new { ok = true, context });
    }

    private static Testing.MauiTestAgentRequestEnvelope HandoffEnvelope(
        Testing.MauiTestAgentAuthoringSnapshot snapshot,
        Testing.MauiTestAgentTarget target,
        string runId,
        string operation,
        string handoffId)
        => new()
        {
            RequestId = $"handoff-{operation}-{handoffId}",
            IdempotencyKey = $"handoff-{operation}-{handoffId}",
            Target = target,
            Correlation = new Testing.MauiTestAgentCorrelation
            {
                AuthoringSessionId = snapshot.SessionId,
                PlanId = snapshot.Plan?.PlanId,
                PlanRevision = snapshot.Plan?.Revision,
                PlanDigest = snapshot.PlanDigest,
                FlowId = snapshot.Plan?.Flow?.FlowId,
                FlowRevision = snapshot.FlowRevision,
                FlowDigest = snapshot.FlowDigest,
                RunId = runId,
            },
            Provenance = HandoffProvenance(),
            Intent = "Diagnose the exact failed Inspector test run.",
            ReadCapabilityId = snapshot.ReadCapabilityId,
            DeadlineMs = 30_000,
            PolicyVersion = Testing.MauiTestAgentProtocolVersions.PolicyVersion,
        };

    private static Testing.MauiActorProvenance HandoffProvenance()
        => new()
        {
            ActorKind = "agent",
            ActorId = "user-selected-agent",
            Channel = "mcp",
            Provider = "devflow-inspector-handoff",
            Intent = "diagnose-failure",
            RecordedAt = DateTimeOffset.UtcNow,
        };

    private (int, string, byte[]) HandleWorkbenchEvidenceDownload(
        InspectorWorkflowServices services,
        string runId,
        string? body)
    {
        if (!TryReadWorkbenchAccess(body, out var access, out var error))
            return JsonResponse(400, new { ok = false, error });

        var token = ResolveWorkbenchRunToken(runId, access!.CapabilityToken);
        var accessResult = services.GetRunStatus(runId, token);
        if (accessResult.Run is null)
            return JsonResponse(accessResult.StatusCode, WorkflowRunStatusResponse.Failure(accessResult.Error ?? "Workflow run was not found."));

        lock (_workbenchRunGate)
        {
            if (!_workbenchEvidence.TryGetValue(runId, out var evidence))
            {
                return JsonResponse(404, new
                {
                    ok = false,
                    error = "No cached failure evidence is available for this run."
                });
            }

            return (200, "application/vnd.maui.evidence+zip", evidence.Bytes);
        }
    }

    private (int, string, byte[]) HandleWorkbenchArtifactStatus(
        InspectorWorkflowServices services,
        string artifactId,
        string? body)
    {
        if (!TryReadWorkbenchArtifactAccess(body, out var access, out var error))
            return JsonResponse(400, new { ok = false, error });
        var result = services.GetArtifactStatus(artifactId, access!.CapabilityToken);
        return JsonResponse(
            result.StatusCode,
            result.StatusCode == 200
                ? new ArtifactTrustRouteResponse { Ok = true, Status = result.Status }
                : ArtifactTrustRouteResponse.Failure(result.Error ?? "Imported artifact was not found."));
    }

    private (int, string, byte[]) HandleWorkbenchArtifactProjection(
        InspectorWorkflowServices services,
        string artifactId,
        string? body)
    {
        if (!TryReadWorkbenchArtifactAccess(body, out var access, out var error))
            return JsonResponse(400, new { ok = false, error });
        var result = services.GetArtifactProjection(artifactId, access!.CapabilityToken);
        return JsonResponse(
            result.StatusCode,
            result.StatusCode == 200
                ? new ArtifactTrustRouteResponse { Ok = true, Projection = result.Projection }
                : ArtifactTrustRouteResponse.Failure(result.Error ?? "Imported artifact was not found."));
    }

    private (int, string, byte[]) HandleWorkbenchArtifactLocalReproduction(
        InspectorWorkflowServices services,
        string artifactId,
        string? body)
    {
        if (!TryReadWorkbenchArtifactAccess(body, out var access, out var error) ||
            string.IsNullOrWhiteSpace(access!.LocalRunId) ||
            access.Current is null)
        {
            return JsonResponse(400, new
            {
                ok = false,
                error = error ?? "An artifact capability token, local run ID, and current reproduction facts are required."
            });
        }

        var result = services.BindLocalReproduction(
            artifactId,
            access.CapabilityToken,
            access.Current,
            access.LocalRunId);
        return JsonResponse(
            result.StatusCode,
            result.StatusCode == 200
                ? new ArtifactTrustRouteResponse
                {
                    Ok = true,
                    Status = result.Status,
                    Reproduction = result.Evaluation,
                }
                : ArtifactTrustRouteResponse.Failure(result.Error ?? "The reproduction binding was rejected."));
    }

    private async Task<WorkbenchRunRequestResult> EnrichWorkbenchRunRequestAsync(
        WorkflowRunStartRequest request,
        InspectorWorkflowServices services)
    {
        if (!services.IsTargetCurrent())
            return WorkbenchRunRequestResult.Failure(409, "The selected agent instance is no longer connected.");

        try
        {
            var status = await _client.GetStatusAsync().ConfigureAwait(false);
            if (status is null)
                return WorkbenchRunRequestResult.Failure(503, "The selected agent did not return target status.");

            // Browser/host input may request only a normal human one-shot acknowledgement. Reset,
            // backend, compensator, and oracle evidence must come from a lifecycle host and is never
            // trusted when posted by the Inspector browser.
            var requested = request.Context;
            request.Context = new Testing.MauiFlowRunContext
            {
                Intent = Testing.MauiFlowReplayIntents.OrdinaryReplay,
                ManualOneShotAuthorization = requested?.ManualOneShotAuthorization == true,
                PriorMutationCompletionCertain = requested?.PriorMutationCompletionCertain == false
                    ? false
                    : null,
                Preconditions = new Testing.MauiFlowReplayPreconditions
                {
                    Observed = new Testing.MauiFlowCheckpoint
                    {
                        AgentInstanceId = services.Target.AgentInstanceId,
                        AppBuildFingerprint = SafeWorkbenchText(status.App?.Build),
                        Route = SafeWorkbenchText(status.Route),
                        Window = SafeWorkbenchText(status.Window),
                        Modal = SafeWorkbenchText(status.Modal),
                        Locale = SafeWorkbenchText(status.Locale),
                        Theme = SafeWorkbenchText(status.Theme),
                        Orientation = SafeWorkbenchText(status.Orientation),
                        DisplayProfile = SafeWorkbenchText(status.DisplayProfile),
                    },
                    CheckedAt = DateTimeOffset.UtcNow,
                    EvidenceReference = "inspector-live-target",
                },
            };
            request.AvailableCapabilities = WorkflowRunCoordinator.BuildAvailableCapabilities(status);
            return WorkbenchRunRequestResult.Success(request);
        }
        catch (Exception exception) when (IsAgentUnavailableException(exception))
        {
            return WorkbenchRunRequestResult.Failure(503, "The selected agent is unavailable.");
        }
    }

    private static bool TryReadWorkbenchRunEnvelope(
        string? body,
        out WorkbenchRunEnvelope? envelope,
        out string? error)
    {
        envelope = null;
        error = null;
        if (string.IsNullOrWhiteSpace(body))
        {
            error = "A run request is required.";
            return false;
        }

        try
        {
            envelope = JsonSerializer.Deserialize<WorkbenchRunEnvelope>(body, WorkbenchJsonOptions);
        }
        catch (JsonException)
        {
            error = "The run request is not valid JSON.";
            return false;
        }

        if (envelope?.Run is null)
        {
            error = "A run request is required.";
            return false;
        }
        return true;
    }

    private static bool TryReadWorkbenchAccess(
        string? body,
        out WorkflowRunAccessRequest? access,
        out string? error)
    {
        access = null;
        error = null;
        try
        {
            access = JsonSerializer.Deserialize<WorkflowRunAccessRequest>(
                string.IsNullOrWhiteSpace(body) ? "{}" : body,
                WorkbenchJsonOptions);
            return access is not null;
        }
        catch (JsonException)
        {
            error = "The run capability request is not valid JSON.";
            return false;
        }
    }

    private static bool TryReadWorkbenchArtifactAccess(
        string? body,
        out WorkbenchArtifactAccess? access,
        out string? error)
    {
        access = null;
        error = null;
        if (string.IsNullOrWhiteSpace(body))
        {
            error = "An imported-artifact capability request is required.";
            return false;
        }
        try
        {
            access = JsonSerializer.Deserialize<WorkbenchArtifactAccess>(body, WorkbenchJsonOptions);
            if (access is null || string.IsNullOrWhiteSpace(access.CapabilityToken))
            {
                error = "A valid imported-artifact capability token is required.";
                return false;
            }
            return true;
        }
        catch (JsonException)
        {
            error = "The imported-artifact request is not valid JSON.";
            return false;
        }
    }

    private void RememberWorkbenchRun(
        string runId,
        string capabilityToken,
        WorkbenchEvidenceConsent? consent,
        string? idempotencyKey = null)
    {
        if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(capabilityToken))
            return;

        lock (_workbenchRunGate)
        {
            var existing = _workbenchRunCapabilities.TryGetValue(runId, out var known) ? known : null;
            _workbenchRunCapabilities[runId] = new WorkbenchRunCapability(
                runId,
                capabilityToken,
                existing?.CreatedAt ?? DateTimeOffset.UtcNow,
                consent ?? existing?.EvidenceConsent ?? WorkbenchEvidenceConsent.None,
                idempotencyKey ?? existing?.IdempotencyKey);
            while (_workbenchRunCapabilities.Count > MaxRetainedWorkbenchRuns)
            {
                var evicted = _workbenchRunCapabilities.Values
                    .OrderBy(entry => entry.CreatedAt)
                    .First();
                _workbenchRunCapabilities.Remove(evicted.RunId);
                _workbenchEvidence.Remove(evicted.RunId);
            }
        }
    }

    private string RememberWorkbenchRepairClassification(
        Testing.MauiFlowRepairEligibilityDecision decision,
        string? reportRunId,
        string? flowDigest,
        Testing.MauiRepairPriorSelectorResolution? priorActiveSelectorResolution)
    {
        var token = "repairclass_" + Guid.NewGuid().ToString("N");
        lock (_workbenchRunGate)
        {
            _workbenchRepairClassifications[token] = new WorkbenchRepairClassification(
                decision,
                reportRunId,
                flowDigest,
                priorActiveSelectorResolution,
                DateTimeOffset.UtcNow);
            while (_workbenchRepairClassifications.Count > MaxRetainedWorkbenchRepairClassifications)
            {
                var oldest = _workbenchRepairClassifications
                    .OrderBy(static pair => pair.Value.CreatedAt)
                    .First();
                _workbenchRepairClassifications.Remove(oldest.Key);
            }
        }
        return token;
    }

    private WorkbenchRepairClassification? GetWorkbenchRepairClassification(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        lock (_workbenchRunGate)
        {
            return _workbenchRepairClassifications.TryGetValue(token, out var classification) &&
                classification.CreatedAt >= DateTimeOffset.UtcNow.AddMinutes(-10)
                ? classification
                : null;
        }
    }

    private string? ResolveWorkbenchRunToken(string runId, string? supplied)
    {
        if (!string.IsNullOrWhiteSpace(supplied))
            return supplied;
        lock (_workbenchRunGate)
            return _workbenchRunCapabilities.TryGetValue(runId, out var known)
                ? known.CapabilityToken
                : null;
    }

    private bool HasActiveWorkbenchRun()
    {
        var runId = Volatile.Read(ref _activeWorkbenchRunId);
        var services = _workflowServices;
        if (string.IsNullOrWhiteSpace(runId) || services is null)
            return false;

        var token = ResolveWorkbenchRunToken(runId, supplied: null);
        var result = services.GetRunStatus(runId, token);
        if (result.Run is { Terminal: false })
            return true;

        ClearActiveWorkbenchRun(runId);
        return false;
    }

    private void ClearActiveWorkbenchRun(string runId)
        => Interlocked.CompareExchange(ref _activeWorkbenchRunId, null, runId);

    private void ForgetWorkbenchRun(string runId)
    {
        lock (_workbenchRunGate)
        {
            _workbenchRunCapabilities.Remove(runId);
            _workbenchEvidence.Remove(runId);
            foreach (var key in _workbenchAgentHandoffs
                         .Where(pair => string.Equals(pair.Value.Context.RunId, runId, StringComparison.Ordinal))
                         .Select(static pair => pair.Key)
                         .ToArray())
            {
                _workbenchAgentHandoffs.Remove(key);
            }
            foreach (var token in _workbenchRepairClassifications
                         .Where(pair => string.Equals(pair.Value.ReportRunId, runId, StringComparison.Ordinal))
                         .Select(static pair => pair.Key)
                         .ToArray())
            {
                _workbenchRepairClassifications.Remove(token);
            }
        }
    }

    private void StoreWorkbenchEvidence(
        string runId,
        byte[] bytes,
        WorkbenchEvidenceConsent consent)
    {
        if (string.IsNullOrWhiteSpace(runId) || bytes.Length == 0 || bytes.Length > MaxCachedWorkbenchEvidenceBytes)
            return;

        lock (_workbenchRunGate)
        {
            _workbenchEvidence[runId] = new WorkbenchEvidence(bytes, consent, DateTimeOffset.UtcNow);
            while (_workbenchEvidence.Count > MaxRetainedWorkbenchEvidence)
            {
                var evicted = _workbenchEvidence
                    .OrderBy(pair => pair.Value.CapturedAt)
                    .First();
                _workbenchEvidence.Remove(evicted.Key);
            }
        }
    }

    private static string? SafeWorkbenchText(string? value, int maximum = 256)
        => SafeInspectorText(value, maximum);

    internal static string? SafeInspectorText(string? value, int maximum = 256)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var builder = new StringBuilder(Math.Min(value.Length, maximum));
        foreach (var character in value)
        {
            if (!char.IsControl(character) || character is '\t' or '\n')
                builder.Append(character);
            if (builder.Length == maximum)
                break;
        }
        return builder.ToString();
    }

    private sealed class WorkbenchRunEnvelope
    {
        [JsonPropertyName("run")] public WorkflowRunStartRequest? Run { get; set; }
        [JsonPropertyName("evidence")] public WorkbenchEvidenceRequest? Evidence { get; set; }
    }

    private static class WorkbenchApprovalConfirmationActions
    {
        public const string AgentRequestApprove = "agent-request-approve";
    }

    private sealed class WorkbenchApprovalConfirmationIssueRequest
    {
        [JsonPropertyName("action")] public string? Action { get; set; }
        [JsonPropertyName("subjectId")] public string? SubjectId { get; set; }
        [JsonPropertyName("approvedScope")] public Testing.MauiTestAgentMutationScope? ApprovedScope { get; set; }
        [JsonPropertyName("grantDurationSeconds")] public int? GrantDurationSeconds { get; set; }
    }

    private sealed record WorkbenchApprovalConfirmation(
        string Capability,
        string AgentId,
        string AgentInstanceId,
        string Action,
        string SubjectId,
        string MaterialDigest,
        DateTimeOffset ExpiresAt);

    /// <summary>
    /// Optional, bounded provenance labels a host may attach to its decision so the broker audit
    /// records which issuer decided. Never consulted by any authorization check.
    /// </summary>
    private sealed class WorkbenchDecisionProvenance
    {
        [JsonPropertyName("actorId")] public string? ActorId { get; set; }
        [JsonPropertyName("channel")] public string? Channel { get; set; }
        [JsonPropertyName("provider")] public string? Provider { get; set; }
    }

    private sealed class WorkbenchAgentRequestDecision
    {
        [JsonPropertyName("humanConfirmed")] public bool HumanConfirmed { get; set; }
        [JsonPropertyName("confirmationCapability")] public string? ConfirmationCapability { get; set; }
        [JsonPropertyName("decidedBy")] public WorkbenchDecisionProvenance? DecidedBy { get; set; }
        [JsonPropertyName("approvedScope")] public Testing.MauiTestAgentMutationScope? ApprovedScope { get; set; }
        [JsonPropertyName("grantDurationSeconds")] public int? GrantDurationSeconds { get; set; }
        [JsonPropertyName("reasonCode")] public string? ReasonCode { get; set; }
    }

    private sealed class WorkbenchEvidenceRequest
    {
        [JsonPropertyName("includeScreenshot")] public bool? IncludeScreenshot { get; set; }
        [JsonPropertyName("includeWorkflow")] public bool? IncludeWorkflow { get; set; }
    }

    private sealed class WorkbenchArtifactAccess
    {
        [JsonPropertyName("capabilityToken")] public string? CapabilityToken { get; set; }
        [JsonPropertyName("localRunId")] public string? LocalRunId { get; set; }
        [JsonPropertyName("current")] public Testing.MauiLocalReproductionExpectation? Current { get; set; }
    }

    private sealed class WorkbenchRepairClassifyRequest
    {
        [JsonPropertyName("run")] public Testing.MauiFlowRunReport? Run { get; set; }
        [JsonPropertyName("plan")] public Testing.MauiTestPlan? Plan { get; set; }
        [JsonPropertyName("replayEligibility")] public Testing.MauiFlowReplayEligibilityDecision? ReplayEligibility { get; set; }
        [JsonPropertyName("expectedCheckpoint")] public Testing.MauiFlowCheckpoint? ExpectedCheckpoint { get; set; }
        [JsonPropertyName("currentCheckpoint")] public Testing.MauiFlowCheckpoint? CurrentCheckpoint { get; set; }
        [JsonPropertyName("beforeDispatch")] public bool? BeforeDispatch { get; set; }
        [JsonPropertyName("isCurrentLocalRun")] public bool IsCurrentLocalRun { get; set; }
        [JsonPropertyName("artifactTrust")] public string? ArtifactTrust { get; set; }
        [JsonPropertyName("priorActiveSelectorResolution")] public Testing.MauiRepairPriorSelectorResolution? PriorActiveSelectorResolution { get; set; }
        [JsonPropertyName("targetFingerprint")] public Testing.MauiElementFingerprint? TargetFingerprint { get; set; }
        [JsonPropertyName("additionalFailureCodes")] public List<string>? AdditionalFailureCodes { get; set; }
    }

    private sealed class WorkbenchRepairProposeRequest
    {
        [JsonPropertyName("classificationToken")] public string? ClassificationToken { get; set; }
        [JsonPropertyName("input")] public Testing.MauiFlowRepairProposalGenerationInput? Input { get; set; }
        [JsonPropertyName("agentOriginated")] public bool AgentOriginated { get; set; }
    }

    private sealed record WorkbenchRepairClassification(
        Testing.MauiFlowRepairEligibilityDecision Decision,
        string? ReportRunId,
        string? FlowDigest,
        Testing.MauiRepairPriorSelectorResolution? PriorActiveSelectorResolution,
        DateTimeOffset CreatedAt);

    private sealed class WorkbenchRepairGrantRequest
    {
        [JsonPropertyName("proposalId")] public string? ProposalId { get; set; }
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("reviewer")] public string? Reviewer { get; set; }
        [JsonPropertyName("humanConfirmed")] public bool HumanConfirmed { get; set; }
        [JsonPropertyName("expiresAt")] public DateTimeOffset? ExpiresAt { get; set; }
        [JsonPropertyName("policy")] public string? Policy { get; set; }
    }

    private sealed class WorkbenchRepairRejectRequest
    {
        [JsonPropertyName("reviewer")] public string? Reviewer { get; set; }
        [JsonPropertyName("reasonCode")] public string? ReasonCode { get; set; }
    }

    private sealed class WorkbenchRepairValidationRequest
    {
        [JsonPropertyName("validationGrant")] public string? ValidationGrant { get; set; }
        [JsonPropertyName("replaySafety")] public Testing.MauiFlowReplayEligibilityDecision? ReplaySafety { get; set; }
    }

    private sealed class WorkbenchRepairApplyRequest
    {
        [JsonPropertyName("approvalGrant")] public string? ApprovalGrant { get; set; }
        [JsonPropertyName("policy")] public string? Policy { get; set; }
    }

    private sealed class WorkbenchRepairVerificationRequest
    {
        [JsonPropertyName("humanConfirmed")] public bool HumanConfirmed { get; set; }
        [JsonPropertyName("verificationRuns")] public List<WorkflowRepairVerificationAccess>? VerificationRuns { get; set; }
    }

    private sealed class WorkbenchRepairRollbackRequest
    {
        [JsonPropertyName("rollbackGrant")] public string? RollbackGrant { get; set; }
        [JsonPropertyName("policy")] public string? Policy { get; set; }
    }

    private sealed class WorkbenchCSharpSourceProposalRequest
    {
        [JsonPropertyName("elementId")] public string? ElementId { get; set; }
        [JsonPropertyName("proposedAutomationId")] public string? ProposedAutomationId { get; set; }
        [JsonPropertyName("sourceFile")] public string? SourceFile { get; set; }
        [JsonPropertyName("sourceLine")] public int? SourceLine { get; set; }
        [JsonPropertyName("sourceColumn")] public int? SourceColumn { get; set; }
        [JsonPropertyName("sourceHash")] public string? SourceHash { get; set; }
        [JsonPropertyName("sourceConfidence")] public string? SourceConfidence { get; set; }
        [JsonPropertyName("affectedFlows")] public List<Testing.MauiCSharpSourceFlowFollowUp>? AffectedFlows { get; set; }
        [JsonPropertyName("affectedPlatforms")] public List<Testing.MauiCSharpSourcePlatformVerification>? AffectedPlatforms { get; set; }
    }


    private sealed class WorkbenchCSharpSourceRejectRequest
    {
        [JsonPropertyName("reviewer")] public string? Reviewer { get; set; }
        [JsonPropertyName("reasonCode")] public string? ReasonCode { get; set; }
    }

    private sealed class WorkbenchXamlSourceProposalRequest
    {
        [JsonPropertyName("elementId")] public string? ElementId { get; set; }
        [JsonPropertyName("proposedAutomationId")] public string? ProposedAutomationId { get; set; }
        [JsonPropertyName("affectedFlows")] public List<Testing.MauiXamlSourceFlowFollowUp>? AffectedFlows { get; set; }
        [JsonPropertyName("affectedPlatforms")] public List<Testing.MauiXamlSourcePlatformVerification>? AffectedPlatforms { get; set; }
    }


    private sealed class WorkbenchXamlSourceRejectRequest
    {
        [JsonPropertyName("reviewer")] public string? Reviewer { get; set; }
        [JsonPropertyName("reasonCode")] public string? ReasonCode { get; set; }
    }


    private sealed class WorkbenchRunRequestResult
    {
        public int StatusCode { get; private init; }
        public string? Error { get; private init; }
        public WorkflowRunStartRequest? Request { get; private init; }

        public static WorkbenchRunRequestResult Success(WorkflowRunStartRequest request)
            => new() { StatusCode = 200, Request = request };

        public static WorkbenchRunRequestResult Failure(int statusCode, string error)
            => new() { StatusCode = statusCode, Error = error };
    }

    private sealed record WorkbenchEvidenceConsent(bool IncludeScreenshot, bool IncludeWorkflow)
    {
        public static WorkbenchEvidenceConsent None { get; } = new(false, false);

        public static WorkbenchEvidenceConsent From(WorkbenchEvidenceRequest? request)
            => new(request?.IncludeScreenshot == true, request?.IncludeWorkflow == true);
    }

    private sealed record WorkbenchRunCapability(
        string RunId,
        string CapabilityToken,
        DateTimeOffset CreatedAt,
        WorkbenchEvidenceConsent EvidenceConsent,
        string? IdempotencyKey);

    private sealed record WorkbenchAgentHandoff(
        string SessionId,
        string ReadCapabilityId,
        DateTimeOffset ExpiresAt,
        WorkbenchAgentHandoffContext Context);

    private sealed class WorkbenchAgentHandoffContext
    {
        [JsonPropertyName("testName")] public string? TestName { get; init; }
        [JsonPropertyName("runId")] public string? RunId { get; init; }
        [JsonPropertyName("expiresAt")] public DateTimeOffset? ExpiresAt { get; init; }
        [JsonPropertyName("failureRequest")] public Testing.MauiTestAgentTraceRequest? FailureRequest { get; init; }
        [JsonPropertyName("improvementsEnvelope")] public Testing.MauiTestAgentRequestEnvelope? ImprovementsEnvelope { get; init; }
        [JsonPropertyName("patchEnvelope")] public Testing.MauiTestAgentRequestEnvelope? PatchEnvelope { get; init; }
    }

    private sealed class WorkbenchAgentHandoffRequest
    {
        [JsonPropertyName("runId")] public string? RunId { get; set; }
        [JsonPropertyName("capabilityToken")] public string? CapabilityToken { get; set; }
        [JsonPropertyName("flowName")] public string? FlowName { get; set; }
        [JsonPropertyName("markdown")] public string? Markdown { get; set; }
        [JsonPropertyName("flow")] public Testing.MauiFlow? Flow { get; set; }
        [JsonPropertyName("plan")] public Testing.MauiTestPlan? Plan { get; set; }
    }

    private sealed record WorkbenchEvidence(
        byte[] Bytes,
        WorkbenchEvidenceConsent Consent,
        DateTimeOffset CapturedAt);

    private static readonly JsonSerializerOptions WorkbenchJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private async Task<(int, string, byte[])> HandleFlowReplayAsync(
        string? body,
        string leaseId,
        string holderKind,
        string holderLabel)
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

        var parsed = Testing.FlowMarkdown.Parse(markdown);
        if (!parsed.Ok || parsed.Flow is null)
            return (400, "application/json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { ok = false, error = parsed.Error ?? "Could not parse the flow." })));
        if (parsed.Flow.Steps.Count > MaxReplaySteps)
            return (400, "application/json", Encoding.UTF8.GetBytes($"{{\"ok\":false,\"error\":\"Flow too large (max {MaxReplaySteps} steps).\"}}"));

        var validation = Testing.FlowValidator.Validate(parsed.Flow);
        if (!validation.Ok)
        {
            var payload = JsonSerializer.Serialize(new
            {
                ok = false,
                error = "Flow failed validation.",
                errors = validation.Errors,
                warnings = validation.Warnings
            }, CamelCase);
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
            lock (_replayEvidenceGate) _lastReplayEvidence = null;
            var report = _workflowReplay is null
                ? await ReplayStandaloneAsync(parsed.Flow, cts.Token)
                : await _workflowReplay(
                    parsed.Flow,
                    client => new InspectorReplayEvidenceCapture(client, WorkbenchEvidenceConsent.None, (_, bytes, _) =>
                    {
                        lock (_replayEvidenceGate) _lastReplayEvidence = bytes;
                    }),
                    new WorkflowRunLeaseHandoff(leaseId, holderKind, holderLabel),
                    cts.Token);
            lock (_replayEvidenceGate) report.EvidenceAvailable = _lastReplayEvidence is { Length: > 0 };
            return (200, "application/json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(report, CamelCase)));
        }
        catch (WorkflowRunRejectedException ex)
        {
            return JsonResponse(ex.StatusCode, new { ok = false, error = ex.Message });
        }
        catch (OperationCanceledException)
        {
            // Surface a timeout as a normal replay failure, not a generic server error.
            var timeout = JsonSerializer.Serialize(new { ok = false, error = "Replay timed out.", total = parsed.Flow.Steps.Count, passed = 0, failed = parsed.Flow.Steps.Count });
            return (200, "application/json", Encoding.UTF8.GetBytes(timeout));
        }
        finally
        {
            Interlocked.Exchange(ref _replayInProgress, 0);
        }
    }

    private async Task<Testing.FlowReplayReport> ReplayStandaloneAsync(
        Testing.MauiFlow flow,
        CancellationToken cancellationToken)
    {
        var capture = new InspectorReplayEvidenceCapture(_client, WorkbenchEvidenceConsent.None, (_, bytes, _) =>
        {
            lock (_replayEvidenceGate) _lastReplayEvidence = bytes;
        });
        var replayer = new Testing.FlowReplayer(_client, evidenceCapture: capture);
        return await replayer.ReplayAsync(flow, null, cancellationToken);
    }

    private const int MaxReplaySteps = 2000;

    private (int, string, byte[]) HandleReplayEvidenceDownload()
    {
        lock (_replayEvidenceGate)
        {
            return _lastReplayEvidence is { Length: > 0 } bytes
                ? (200, "application/vnd.maui.evidence+zip", bytes)
                : (404, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"No replay failure evidence is available.\"}"));
        }
    }

    private sealed class InspectorReplayEvidenceCapture : Testing.IFlowRunEvidenceCapture
    {
        private readonly AgentClient _client;
        private readonly WorkbenchEvidenceConsent _consent;
        private readonly Action<string, byte[], WorkbenchEvidenceConsent> _capture;
        public Testing.MauiFlowArtifactReference? CapturedArtifact { get; private set; }

        public InspectorReplayEvidenceCapture(
            AgentClient client,
            WorkbenchEvidenceConsent consent,
            Action<string, byte[], WorkbenchEvidenceConsent> capture)
        {
            _client = client;
            _consent = consent;
            _capture = capture;
        }

        public async Task CaptureOnFailureAsync(
            Testing.MauiFlow flow,
            Testing.FlowStep failedStep,
            Testing.FlowStepResult result,
            CancellationToken cancellationToken)
        {
            var bundle = await Evidence.EvidenceCapture.CaptureToBytesAsync(_client, new Evidence.EvidenceRequest
            {
                Source = "inspector",
                IncludeScreenshot = _consent.IncludeScreenshot,
                WorkflowMarkdown = _consent.IncludeWorkflow ? Testing.FlowMarkdown.Serialize(flow) : null,
            }, cancellationToken);
            _capture("legacy-evidence", bundle.Bytes, _consent);
            CapturedArtifact = new Testing.MauiFlowArtifactReference
            {
                ArtifactId = "evidence-replay",
                Kind = "mauitrace",
                Digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bundle.Bytes)).ToLowerInvariant(),
                MediaType = "application/vnd.maui.evidence+zip",
                Redacted = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
        }

        public async Task CaptureOnRunFailureAsync(
            Testing.MauiFlowRunEvidenceContext context,
            CancellationToken cancellationToken)
        {
            var bundle = await Evidence.EvidenceCapture.CaptureToBytesAsync(_client, new Evidence.EvidenceRequest
            {
                Source = "inspector",
                IncludeScreenshot = _consent.IncludeScreenshot,
                WorkflowMarkdown = _consent.IncludeWorkflow ? Testing.FlowMarkdown.Serialize(context.Flow) : null,
                FlowRun = new Evidence.EvidenceFlowRunLink
                {
                    RunId = context.Report.RunId,
                    FailedStepId = context.Report.DivergenceStepId,
                    FailureCode = context.Report.Failure?.Code,
                    ReportDigest = context.ReportDigest,
                    ReportPath = context.ReportPath,
                    ReportReference = context.ReportPath is null
                        ? $"run:{context.Report.RunId}"
                        : $"flow-run:{context.Report.RunId}",
                    CaptureCompleteness = "failure-only-redacted",
                }
            }, cancellationToken);
            _capture(context.Report.RunId ?? "unknown-run", bundle.Bytes, _consent);
            CapturedArtifact = new Testing.MauiFlowArtifactReference
            {
                ArtifactId = $"evidence-{context.Report.RunId}",
                Kind = "mauitrace",
                Digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bundle.Bytes)).ToLowerInvariant(),
                MediaType = "application/vnd.maui.evidence+zip",
                Redacted = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
        }
    }

    // POST paths rejected (409) while a replay is driving the app. Source apply/rollback paths are
    // absent because this layer serves no source-write route at all — see the reviewed-source
    // handlers below, which stop at status/preview/reject.
    internal static bool IsBlockedDuringReplay(string path)
    {
        return path switch
        {
            "/api/tap" or "/api/scroll" or "/api/gesture" or "/api/back" or "/api/fill" or "/api/key"
                or "/api/setProperty" or "/api/persistProperty" or "/api/navigate" or "/api/cdp/eval"
                or "/api/alerts/dismiss" or "/api/flows/record/start" or "/api/flows/record/step"
                or "/api/flows/replay" or "/api/control" or "/api/checkpoint/restore" => true,
            // A profiler session started from the inspector would perturb the run being replayed.
            "/api/performance/start" or "/api/performance/stop" => true,
            _ => false,
        };
    }

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
        var payload = JsonSerializer.Serialize(new { ok = true, route });
        return (200, "application/json", Encoding.UTF8.GetBytes(payload));
    }

    // Resume checkpoints are broker-owned and explicit. Reconnection merely refreshes this status;
    // it never calls restore, which could otherwise mutate a freshly rebuilt app unexpectedly.
    private (int, string, byte[]) HandleResumeCheckpointStatus()
        => JsonResponse(200, _checkpoints.Status(_checkpointRegistration));

    private async Task<(int, string, byte[])> HandleResumeCheckpointAsync(string action)
    {
        var status = action == "save"
            ? await _checkpoints.SaveAsync(_checkpointRegistration, _client, _lifetimeCts.Token)
            : await _checkpoints.RestoreAsync(_checkpointRegistration, _client, _lifetimeCts.Token);
        if (status.Ok && action == "restore")
            InvalidateScreenshotCache();
        return JsonResponse(status.Ok ? 200 : 400, status);
    }

    private (int, string, byte[]) HandleResumeCheckpointClear()
    {
        _checkpoints.Clear(_checkpointRegistration);
        return JsonResponse(200, new RouteCheckpointStatus { Connected = true, HasCheckpoint = false });
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
        var payload = JsonSerializer.Serialize(new
        {
            ok = true,
            file = el.SourceFile,
            line = el.SourceLine,
            column = el.SourceColumn,
            sourceHash = el.SourceHash
        });
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
    internal static bool IsTokenGatedPath(string path)
    {
        if (path.StartsWith("/api/workbench/", StringComparison.OrdinalIgnoreCase))
            return true;

        return path switch
        {
        "/api/checkpoint/status" or "/api/checkpoint/save" or "/api/checkpoint/restore" or "/api/checkpoint/clear"
            or
        "/api/source" or "/api/persistProperty" or "/api/logs" or "/api/network" or "/api/network/detail" or "/api/problems" or "/api/preferences"
            or "/api/device" or "/api/sensors" or "/api/geolocation"
            or "/api/performance/start" or "/api/performance/snapshot" or "/api/performance/stop"
            or "/api/files/roots" or "/api/files/list"
            or "/api/flows/files/list" or "/api/flows/files/load" or "/api/flows/replay/evidence"
            or "/api/plans/list" or "/api/plans/load" or "/api/plans/validate" or "/api/plans/save"
            or "/api/flows/validate" or "/api/flows/diff" or "/api/flows/commit" or "/api/flows/selector/verify" or "/api/flows/assert/verify"
            or "/api/alerts" or "/api/alerts/dismiss"
            or "/api/evidence/preview" or "/api/evidence/capture"
            or "/api/cdp/webviews" or "/api/cdp/source" or "/api/cdp/eval" => true,
        _ => false,
        };
    }

    private async Task<(int, string, byte[])> HandleAlertsAsync()
    {
        var result = await _alertController.DetectAsync();
        return Ok(JsonSerializer.Serialize(result, CamelCase));
    }

    private async Task<(int, string, byte[])> HandleAlertDismissAsync(string? body)
    {
        var buttonLabel = ReadStringField(body, "buttonLabel");
        if (string.IsNullOrWhiteSpace(buttonLabel) || buttonLabel.Length > 256)
            return BadRequest("buttonLabel is required and must be 256 characters or fewer");
        var result = await _alertController.DismissAsync(buttonLabel);
        return Ok(JsonSerializer.Serialize(result, CamelCase));
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
            return Ok(JsonSerializer.Serialize(new { ok = true, requests }, CamelCase));
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
            return Ok(JsonSerializer.Serialize(new { ok = true, request = detail }, CamelCase));
        }
        catch { return Ok("{\"ok\":false,\"error\":\"network unavailable\"}"); }
    }

    private async Task<(int, string, byte[])> HandleProblemsAsync(string? body)
    {
        var limit = ReadIntField(body, "limit", 100, 1, 500);
        var elementId = ReadStringField(body, "elementId");
        try
        {
            var batch = await _client.GetDiagnosticProblemsAsync(
                limit,
                string.IsNullOrWhiteSpace(elementId) ? null : elementId);
            return Ok(JsonSerializer.Serialize(new
            {
                ok = true,
                enabled = batch.Enabled,
                revision = batch.Revision,
                count = batch.Count,
                evicted = batch.Evicted,
                problems = batch.Problems
            }, CamelCase));
        }
        catch
        {
            return Ok("{\"ok\":false,\"error\":\"diagnostic Problems unavailable\"}");
        }
    }

    // ── On-demand diagnostics (layout scan, performance triage) ─────────────────────────────
    // Both are token-gated POST reads that proxy the shared AgentClient/Driver analysis, so the
    // Inspector, the CLI, and the MCP tools present identical results. Neither one refreshes the
    // frame or takes a screenshot: a diagnostic must not change what the user is looking at.

    private async Task<(int, string, byte[])> HandlePerformanceStartAsync(
        string? body,
        string leaseId,
        string holderKind,
        string holderLabel)
    {
        var sampleIntervalMs = ReadIntField(body, "sampleIntervalMs", 0, 0, 60_000);
        try
        {
            var summary = await _client.StartPerformanceSessionAsync(
                sampleIntervalMs >= 50 ? sampleIntervalMs : null);
            var owned = false;
            if (summary.Session.Active && !string.IsNullOrWhiteSpace(summary.Session.SessionId))
            {
                if (string.IsNullOrWhiteSpace(summary.Session.StopToken))
                    throw new InvalidOperationException(
                        "The agent did not return a profiler creator stop token.");
                SetPerformanceOwnership(
                    summary.Session.SessionId,
                    summary.Session.StopToken,
                    leaseId,
                    holderKind,
                    holderLabel);
                summary.Session.StopToken = null;
                owned = true;
            }
            return Ok(JsonSerializer.Serialize(new { ok = true, owned, summary }, CamelCase));
        }
        catch
        {
            return Ok("{\"ok\":false,\"error\":\"performance triage unavailable\"}");
        }
    }

    private async Task<(int, string, byte[])> HandlePerformanceSnapshotAsync()
    {
        var ownership = Volatile.Read(ref _performanceOwnership);
        var sessionId = ownership?.SessionId;
        try
        {
            var summary = await _client.GetPerformanceSummaryAsync(sessionId);
            var owned = ownership is not null &&
                ReferenceEquals(Volatile.Read(ref _performanceOwnership), ownership) &&
                string.Equals(summary.Session.SessionId, sessionId, StringComparison.Ordinal);
            return Ok(JsonSerializer.Serialize(new { ok = true, owned, summary }, CamelCase));
        }
        catch (ProfilerSessionMismatchException) when (ownership is not null)
        {
            TryClearPerformanceOwnership(ownership);
            return Ok("{\"ok\":false,\"error\":\"The recorded performance session was replaced or disconnected. Refresh to inspect the current session.\"}");
        }
        catch
        {
            return Ok("{\"ok\":false,\"error\":\"performance triage unavailable\"}");
        }
    }

    private async Task<(int, string, byte[])> HandlePerformanceStopAsync()
    {
        var ownership = Volatile.Read(ref _performanceOwnership);
        try
        {
            if (ownership is null)
                return Ok("{\"ok\":false,\"error\":\"This Inspector did not start the active performance session and will not stop it.\"}");
            using var leaseScope = _client.UseMutationLease(
                ownership.LeaseId,
                ownership.HolderKind,
                ownership.HolderLabel);
            var summary = await _client.StopPerformanceSessionAsync(
                ownership.SessionId,
                ownership.StopToken);
            if (!summary.Session.Active)
                TryClearPerformanceOwnership(ownership);
            return Ok(JsonSerializer.Serialize(new { ok = true, owned = false, summary }, CamelCase));
        }
        catch (ProfilerSessionMismatchException) when (ownership is not null)
        {
            TryClearPerformanceOwnership(ownership);
            return Ok("{\"ok\":false,\"error\":\"The recorded performance session was replaced or disconnected and was not stopped.\"}");
        }
        catch
        {
            return Ok("{\"ok\":false,\"error\":\"performance triage unavailable\"}");
        }
    }

    // ── Evidence bundle (.mauitrace) ────────────────────────────────────────────────────────
    // Two-step by design: /preview returns the plan the browser must show and the user must
    // confirm; /capture then streams the bundle bytes for download. Both are token-gated reads —
    // the capture is a read of the app, never a mutation, and both go through the same shared
    // EvidenceCapture used by the CLI and MCP tools, so redaction cannot diverge per surface.

    private async Task<(int, string, byte[])> HandleEvidencePreviewAsync(string? body)
    {
        try
        {
            var plan = await Evidence.EvidenceCapture.PreviewAsync(_client, ReadEvidenceRequest(body, includeWorkflow: ReadBoolField(body, "includeWorkflow")));
            return Ok(Evidence.EvidenceJson.Serialize(new Evidence.EvidencePreviewResponse { Ok = true, Plan = plan }));
        }
        catch (Exception ex) when (IsAgentUnavailableException(ex))
        {
            return Ok("{\"ok\":false,\"error\":\"The DevFlow agent is unavailable.\"}");
        }
    }

    private async Task<(int, string, byte[])> HandleEvidenceCaptureAsync(string? body)
    {
        try
        {
            var (_, bytes) = await Evidence.EvidenceCapture.CaptureToBytesAsync(
                _client, ReadEvidenceRequest(body, includeWorkflow: true));
            return (200, "application/zip", bytes);
        }
        catch (Exception ex) when (IsAgentUnavailableException(ex))
        {
            return (503, "application/json", Encoding.UTF8.GetBytes(
                "{\"ok\":false,\"error\":\"The DevFlow agent is unavailable.\"}"));
        }
    }

    private Evidence.EvidenceRequest ReadEvidenceRequest(string? body, bool includeWorkflow)
    {
        // The workflow is user-supplied text: the request-body cap bounds it in transit and the
        // builder re-checks it, excluding it (with a reason in the manifest) when it is too large.
        var workflow = includeWorkflow ? ReadStringField(body, "workflow") : null;

        return new Evidence.EvidenceRequest
        {
            // Screenshots stay opt-in: only an explicit `true` from the confirmed dialog enables it.
            IncludeScreenshot = ReadBoolField(body, "includeScreenshot"),
            SelectedElementId = ReadStringField(body, "elementId"),
            LogLimit = ReadIntField(body, "logLimit", Evidence.EvidenceFormat.DefaultLogLimit, 1, Evidence.EvidenceFormat.MaxLogLimit),
            NetworkLimit = ReadIntField(body, "networkLimit", Evidence.EvidenceFormat.DefaultNetworkLimit, 1, Evidence.EvidenceFormat.MaxNetworkLimit),
            WorkflowMarkdown = workflow,
            Source = "inspector",
            ProjectHint = _project,
        };
    }

    private async Task<(int, string, byte[])> HandlePreferencesAsync(string? body)
    {
        var shared = ReadStringField(body, "sharedName");
        try
        {
            var prefs = await _client.GetPreferencesAsync(string.IsNullOrWhiteSpace(shared) ? null : shared);
            // Unknown shape across platforms — mask obvious secrets in the serialized text; the client
            // additionally masks values by key heuristic and only reveals on demand.
            var masked = MaskSecrets(JsonSerializer.Serialize(prefs, CamelCase));
            return Ok(WrapRaw("preferences", masked));
        }
        catch { return Ok("{\"ok\":false,\"error\":\"preferences unavailable\"}"); }
    }

    // device-info/display are always safe; battery/connectivity are best-effort (may be unsupported).
    private static readonly string[] DeviceEndpoints = { "device-info", "device-display", "battery", "connectivity" };

    private async Task<(int, string, byte[])> HandleDeviceAsync(string? body)
    {
        var result = new Dictionary<string, JsonElement>();
        foreach (var ep in DeviceEndpoints)
        {
            try { result[ep] = await _client.GetPlatformInfoAsync(ep); }
            catch { /* unsupported on this platform — omit */ }
        }
        return Ok(JsonSerializer.Serialize(new { ok = true, device = result }, CamelCase));
    }

    private async Task<(int, string, byte[])> HandleSensorsAsync(string? body)
    {
        try
        {
            var sensors = await _client.GetSensorsAsync();
            return Ok(JsonSerializer.Serialize(new { ok = true, sensors }, CamelCase));
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
            return Ok(JsonSerializer.Serialize(new { ok = true, location = loc }, CamelCase));
        }
        catch { return Ok("{\"ok\":false,\"error\":\"geolocation unavailable\"}"); }
    }

    private async Task<(int, string, byte[])> HandleFilesRootsAsync(string? body)
    {
        try
        {
            var roots = await _client.ListStorageRootsAsync();
            return Ok(JsonSerializer.Serialize(new { ok = true, roots }, CamelCase));
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
            return Ok(JsonSerializer.Serialize(new { ok = true, files }, CamelCase));
        }
        catch { return Ok("{\"ok\":false,\"error\":\"files unavailable\"}"); }
    }

    // ── Blazor WebView CDP tab — list WebViews, view source, and evaluate JavaScript through the
    // existing chobitsu.js CDP bridge.
    private async Task<(int, string, byte[])> HandleCdpWebViewsAsync(string? body)
    {
        try { var wv = await _client.GetCdpWebViewsAsync(); return Ok(JsonSerializer.Serialize(new { ok = true, webviews = wv }, CamelCase)); }
        catch { return Ok("{\"ok\":false,\"error\":\"no WebViews / CDP unavailable\"}"); }
    }

    private async Task<(int, string, byte[])> HandleCdpSourceAsync(string? body)
    {
        var id = ReadStringField(body, "webviewId");
        try { var src = await _client.GetCdpSourceAsync(string.IsNullOrWhiteSpace(id) ? null : id); return Ok(JsonSerializer.Serialize(new { ok = true, source = src }, CamelCase)); }
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
            return Ok(JsonSerializer.Serialize(new { ok = true, result = res }, CamelCase));
        }
        catch { return Ok("{\"ok\":false,\"error\":\"evaluate failed\"}"); }
    }

    // ── Data helpers ──

    private static (int, string, byte[]) Ok(string json) => (200, "application/json", Encoding.UTF8.GetBytes(json));
    private static (int, string, byte[]) BadRequest(string error)
        => (400, "application/json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { error }, CamelCase)));
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

    // The rules live in Evidence/EvidenceRedaction.cs so the inspector data tabs and the evidence
    // bundle share one ruleset and can never drift apart. These stay as the inspector's entry points.
    internal static void RedactHeaders(Dictionary<string, string[]>? headers)
        => Evidence.EvidenceRedaction.RedactHeaders(headers);

    internal static string MaskUrlSecrets(string url)
        => Evidence.EvidenceRedaction.MaskUrlSecrets(url);

    internal static string MaskSecrets(string text)
        => Evidence.EvidenceRedaction.MaskSecrets(text);

    internal static bool IsMutation(string path) => path switch
    {
        "/api/tap" or "/api/scroll" or "/api/gesture" or "/api/back" or "/api/fill" or "/api/key"
            or "/api/setProperty" or "/api/persistProperty" or "/api/navigate" or "/api/cdp/eval"
            or "/api/alerts/dismiss"
            or "/api/checkpoint/restore"
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
        return Ok(JsonSerializer.Serialize(new
        {
            ok = status.Ok,
            youAreWriter = status.YouHold,
            heldByOther = status.HeldByOther,
            holderKind = status.HolderKind,
            label = status.Label,
            expiresInMs = status.ExpiresInMs,
            authority = status.Authority
        }, CamelCase));
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

    internal static string ComputeWebSocketAcceptKey(string webSocketKey)
        => Convert.ToBase64String(
            System.Security.Cryptography.SHA1.HashData(
                Encoding.ASCII.GetBytes(webSocketKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));

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

    /// <summary>
    /// Reads a binary Inspector artifact import without decoding foreign bytes as text. The caller
    /// supplies the format-specific maximum; raw bytes are passed immediately to the broker trust
    /// importer and are never retained by the Inspector.
    /// </summary>
    private static async Task<byte[]?> ReadBoundedBytesAsync(
        Stream input,
        long declaredLength,
        int maximumBytes,
        CancellationToken ct = default)
    {
        if (declaredLength > maximumBytes)
            return null;

        using var output = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        var total = 0;
        while (true)
        {
            using var perReadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            perReadCts.CancelAfter(TimeSpan.FromSeconds(10));
            int read;
            try
            {
                read = await input.ReadAsync(buffer.AsMemory(), perReadCts.Token);
            }
            catch
            {
                return null;
            }

            if (read <= 0)
                break;
            total += read;
            if (total > maximumBytes)
                return null;
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return output.ToArray();
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
        byte[]? rawBody = null;
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
            rawBody = bodyBytes[..totalBodyRead];
            body = Encoding.UTF8.GetString(rawBody);
        }

        return (new HttpRequestInfo
        {
            Method = method,
            Path = path,
            Query = query,
            Headers = headers,
            Body = body,
            BodyBytes = rawBody
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
                     "Cache-Control: no-store\r\n" +
                     "X-Content-Type-Options: nosniff\r\n" +
                     "Connection: close\r\n\r\n";

        await stream.WriteAsync(Encoding.UTF8.GetBytes(header), ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    internal sealed class HttpRequestInfo
    {
        public string Method { get; init; } = "";
        public string Path { get; init; } = "";
        public Dictionary<string, string> Query { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Headers { get; init; } = new();
        public string? Body { get; init; }
        public byte[]? BodyBytes { get; init; }
    }
}
