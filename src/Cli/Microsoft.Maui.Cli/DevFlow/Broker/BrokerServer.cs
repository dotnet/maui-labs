using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>
/// Central broker daemon that manages agent registration and port assignment.
/// Agents connect via WebSocket; CLI queries via HTTP.
/// </summary>
public partial class BrokerServer : IDisposable
{
    public const int DefaultPort = 19223;
    public const int PortRangeStart = 10223;
    public const int PortRangeEnd = 10899;
    private const int MaxRecordingRequestChars = 512 * 1024;

    private readonly int _port;
    private readonly TimeSpan _idleTimeout;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, AgentConnection> _agents = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _agentRouteGates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _agentStateGates = new(StringComparer.Ordinal);
    private readonly MutationLeaseRegistry _mutationLeases;
    /// <summary>
    /// Reads independent business-oracle evidence out of band for a run against an already-running
    /// app. Null in a broker whose host registered no evaluator, in which case such a run stays
    /// unverified and repair-ineligible rather than being certified on weaker evidence.
    /// </summary>
    private readonly Execution.IAttachedRunOracleEvaluator? _attachedRunOracles;
    private readonly BrokerFlowCoordinator _flows;
    private readonly RouteCheckpointCoordinator _checkpoints;
    private readonly WorkflowRunCoordinator _workflowRuns;
    private readonly TestAgentSessionService _testAgentSessions;

    // A workflow run mutates the live app, so the broker verifies the human-issued grant itself
    // instead of trusting the calling client to have done it. Only relaxed by tests that exercise
    // unrelated endpoint mechanics.
    private readonly bool _requireWorkflowRunAuthorization;
    private readonly byte[] _workflowRunDispatchKey = RandomNumberGenerator.GetBytes(32);
    private readonly ArtifactTrustImportService _artifactTrustImports;
    private readonly ArtifactTrustStore _artifactTrustStore;
    private readonly MauiPreviewFeatureFlags _previewFlags;
    // A production broker supplies a per-process, owner-file-only native host verifier. Internal
    // construction remains explicitly unavailable unless a test supplies its own verifier.
    private readonly Func<string?, bool>? _trustedHostApprovalVerifier;
    private readonly string? _nativeApprovalToken;
    // Resolves the component that owns the connected app's lifecycle, when one has registered
    // itself with this broker process. It stays null in an ordinary broker, which is why repair
    // validation reports itself unavailable rather than promising a reset nobody can perform.
    private readonly HashSet<int> _assignedPorts = new();
    private readonly object _portLock = new();
    private DateTime _lastActivity = DateTime.UtcNow;
    private Timer? _idleTimer;
    private bool _disposed;
    private Action<string>? _log;

    public int Port => _port;
    public int AgentCount => _agents.Count;
    public bool IsRunning => _listener?.IsListening ?? false;

    public BrokerServer(
        int port = DefaultPort,
        TimeSpan? idleTimeout = null,
        Action<string>? log = null)
        : this(port, idleTimeout, log, attachedRunOracles: null)
    {
    }

    /// <summary>
    /// Creates the broker a host runs, supplying the evaluator that reads independent business-
    /// oracle evidence out of band for runs against an already-running app, and the resolver that
    /// supplies a lifecycle reset owner for apps which opt into repair validation.
    /// </summary>
    internal BrokerServer(
        int port,
        TimeSpan? idleTimeout,
        Action<string>? log,
        Execution.IAttachedRunOracleEvaluator? attachedRunOracles)
        : this(
            port,
            idleTimeout,
            log,
            checkpointStore: null,
            recordingStorageRoot: null,
            clock: null,
            previewFlags: null,
            trustedHostApprovalVerifier: null,
            nativeApprovalToken: CreateNativeApprovalToken(),
            attachedRunOracles: attachedRunOracles)
    {
    }

    /// <summary>
    /// Test-only overload for suites that exercise workflow-run endpoint mechanics unrelated to the
    /// human-approval boundary. Production callers use the public constructor, which requires it.
    /// </summary>
    internal BrokerServer(
        int port,
        TimeSpan? idleTimeout,
        bool requireWorkflowRunAuthorization)
        : this(
            port,
            idleTimeout,
            log: null,
            checkpointStore: null,
            recordingStorageRoot: null,
            clock: null,
            previewFlags: null,
            trustedHostApprovalVerifier: null,
            nativeApprovalToken: CreateNativeApprovalToken(),
            requireWorkflowRunAuthorization: requireWorkflowRunAuthorization)
    {
    }

    internal BrokerServer(
        int port,
        TimeSpan? idleTimeout,
        MauiPreviewFeatureFlags previewFlags,
        Action<string>? log = null,
        Func<string?, bool>? trustedHostApprovalVerifier = null)
        : this(
            port,
            idleTimeout,
            log,
            checkpointStore: null,
            recordingStorageRoot: null,
            clock: null,
            previewFlags,
            trustedHostApprovalVerifier)
    {
    }

    internal BrokerServer(
        int port,
        TimeSpan? idleTimeout,
        Action<string>? log,
        RouteCheckpointStore? checkpointStore,
        string? recordingStorageRoot,
        TimeProvider? clock,
        MauiPreviewFeatureFlags? previewFlags = null,
        Func<string?, bool>? trustedHostApprovalVerifier = null,
        string? nativeApprovalToken = null,
        bool requireWorkflowRunAuthorization = true,
        Execution.IAttachedRunOracleEvaluator? attachedRunOracles = null)
    {
        _port = port;
        _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(5);
        _log = log;
        _requireWorkflowRunAuthorization = requireWorkflowRunAuthorization;
        _attachedRunOracles = attachedRunOracles;
        _previewFlags = previewFlags ?? MauiPreviewFeatureFlagConfiguration.FromEnvironment();
        _nativeApprovalToken = nativeApprovalToken;
        _trustedHostApprovalVerifier = nativeApprovalToken is null
            ? trustedHostApprovalVerifier
            : supplied => FixedTimeApprovalTokenEquals(nativeApprovalToken, supplied);
        var recordingSpool = new FlowRecordingSpoolStore(
            recordingStorageRoot,
            clock,
            warning => Log("Warning: " + warning));
        _flows = new BrokerFlowCoordinator(
            new FlowRecordingStore(clock),
            recordingSpool);
        _checkpoints = new RouteCheckpointCoordinator(checkpointStore);
        _mutationLeases = new MutationLeaseRegistry();
        _workflowRuns = new WorkflowRunCoordinator(
            _mutationLeases,
            ExecuteWorkflowRunAsync,
            new WorkflowRunCoordinatorOptions
            {
                ArtifactRoot = Path.Combine(recordingSpool.StorageRoot, "workflow-runs"),
            },
            clock: clock,
            controlLedger: ControlWorkflowRunLedgerAsync,
            authorizeDispatch: AuthorizeWorkflowRunDispatch,
            beginOracleSession: BeginWorkflowRunOracleSessionAsync);
        _testAgentSessions = new TestAgentSessionService(clock: clock);
        _artifactTrustImports = new ArtifactTrustImportService(clock);
        _artifactTrustStore = new ArtifactTrustStore(clock: clock);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        // Also accept a literal 127.0.0.1 Host header: HTTP.sys routes by the request's Host,
        // and a bare "localhost" prefix rejects "127.0.0.1" with 400 "Invalid Hostname". Some
        // local clients connect with a 127.0.0.1 Host, so register the loopback IP too.
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException)
        {
            // The 127.0.0.1 prefix can require a URL ACL reservation on some machines, whereas the
            // "localhost" prefix is always permitted without elevation. Retry with localhost only
            // before resorting to the strong wildcard (which itself needs elevation), so adding the
            // loopback-IP prefix can never regress startup on a stock machine.
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{_port}/");
                _listener.Start();
            }
            catch (HttpListenerException)
            {
                // Last-resort fallback for platforms where the loopback prefixes don't bind.
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://+:{_port}/");
                _listener.Start();
            }
        }

        Log($"Broker started on port {_port} (PID {Environment.ProcessId})");

        // Write state file
        WriteBrokerState();

        // Start idle timer
        _idleTimer = new Timer(_ => CheckIdle(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync().WaitAsync(_cts.Token);
                _ = HandleRequestAsync(context);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            Shutdown();
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        TouchActivity();

        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            var method = context.Request.HttpMethod;

            // Defense-in-depth: the broker is designed to be reachable only on
            // loopback, but HttpListener falls back to binding on all interfaces
            // (http://+:port/) when localhost reservation fails — see line 56-60
            // below. In that fallback, non-browser HTTP clients on the LAN (curl,
            // scripts, attacker) can reach this port without sending an Origin
            // header, so the Origin check alone (further down) doesn't help.
            // Reject any caller whose RemoteEndPoint isn't a loopback address.
            // Legitimate uses (CLI tool, inspector UI in a local browser, MAUI
            // agent running on the same machine, Android emulator port-forwarded
            // back to host loopback) all use 127.0.0.1 or ::1.
            var remoteIp = context.Request.RemoteEndPoint?.Address;
            if (remoteIp == null || !IPAddress.IsLoopback(remoteIp))
            {
                context.Response.StatusCode = 403;
                context.Response.ContentType = "text/plain";
                var msg = Encoding.UTF8.GetBytes("Forbidden: loopback required");
                context.Response.ContentLength64 = msg.Length;
                await context.Response.OutputStream.WriteAsync(msg);
                context.Response.Close();
                return;
            }

            // WebSocket upgrade for agents
            if (context.Request.IsWebSocketRequest && path == "/ws/agent")
            {
                await HandleAgentWebSocket(context);
                return;
            }

            // WebSocket upgrade for inspector event relay
            if (context.Request.IsWebSocketRequest && path.StartsWith("/inspector", StringComparison.OrdinalIgnoreCase))
            {
                await HandleInspectorRoute(context, path);
                return;
            }

            // HTTP endpoints for CLI
            // Block state-mutating endpoints from non-loopback origins BEFORE dispatching
            // the handler — otherwise a cross-origin POST to /api/shutdown would still
            // tear down the broker even though we return 403.
            var origin = context.Request.Headers["Origin"];
            if (method == "POST" && !LocalOriginValidator.IsAllowed(origin, _port))
            {
                context.Response.StatusCode = 403;
                context.Response.ContentType = "application/json";
                var forbidden = Encoding.UTF8.GetBytes(CliJson.SerializeUntyped(new JsonObject { ["error"] = "Forbidden origin" }, indented: false));
                context.Response.ContentLength64 = forbidden.Length;
                await context.Response.OutputStream.WriteAsync(forbidden);
                context.Response.Close();
                return;
            }

            if (path.StartsWith("/api/leases/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleMutationLeaseRoute(context, method, path);
                return;
            }
            if (path.StartsWith("/api/recordings/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleRecordingRoute(context, method, path);
                return;
            }
            if (path.StartsWith("/api/checkpoints/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleCheckpointRoute(context, method, path);
                return;
            }
            if (path.StartsWith("/api/workflow-runs", StringComparison.OrdinalIgnoreCase))
            {
                await HandleWorkflowRunRoute(context, method, path);
                return;
            }
            if (path.StartsWith("/api/test-agent", StringComparison.OrdinalIgnoreCase))
            {
                await HandleTestAgentRoute(context, method, path);
                return;
            }
            if (path.StartsWith("/api/artifact-trust", StringComparison.OrdinalIgnoreCase))
            {
                await HandleArtifactTrustRoute(context, method, path);
                return;
            }

            var (statusCode, body) = (method, path) switch
            {
                ("GET", "/api/health") => (200, CliJson.SerializeUntyped(new JsonObject
                {
                    ["status"] = "ok",
                    ["agents"] = _agents.Count
                }, indented: false)),
                ("GET", "/api/agents") => (200, HandleListAgents()),
                ("POST", "/api/shutdown") => HandleShutdown(),
                // Browsers auto-request /favicon.ico; answer 204 so the inspector page doesn't log a 404.
                ("GET", "/favicon.ico") => (204, ""),
                _ => (0, "") // handled below for inspector routes
            };

            // Inspector routes — serve the web inspector for connected agents
            if (statusCode == 0)
            {
                if (path.StartsWith("/inspector", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleInspectorRoute(context, path);
                    return;
                }

                statusCode = 404;
                body = CliJson.SerializeUntyped(new JsonObject { ["error"] = "Not found" }, indented: false);
            }

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            // Mirror Origin only for loopback callers; the previous wildcard let any web
            // page read /api/agents (leaking IDs) and POST /api/shutdown.
            if (LocalOriginValidator.IsAllowed(origin, _port) && !string.IsNullOrEmpty(origin) && origin != "null")
            {
                context.Response.Headers.Add("Access-Control-Allow-Origin", origin);
                context.Response.Headers.Add("Vary", "Origin");
            }

            var responseBytes = Encoding.UTF8.GetBytes(body);
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes);
            context.Response.Close();
        }
        catch (Exception ex)
        {
            Log($"Error handling request: {ex.Message}");
            try { context.Response.Close(); } catch { }
        }
    }

    private async Task HandleAgentWebSocket(HttpListenerContext context)
    {
        // Reject cross-origin WebSocket connections; only the local agent process
        // or CLI tools (no Origin header) may register.
        var origin = context.Request.Headers["Origin"];
        if (!LocalOriginValidator.IsAllowed(origin, _port))
        {
            context.Response.StatusCode = 403;
            context.Response.Close();
            return;
        }

        WebSocketContext wsContext;
        try
        {
            wsContext = await context.AcceptWebSocketAsync(null);
        }
        catch (Exception ex)
        {
            Log($"WebSocket accept failed: {ex.Message}");
            context.Response.StatusCode = 500;
            context.Response.Close();
            return;
        }

        var ws = wsContext.WebSocket;
        var buffer = new byte[4096];
        AgentConnection? publishedConnection = null;

        try
        {
            // Read registration message
            var result = await ws.ReceiveAsync(buffer, _cts?.Token ?? CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) return;

            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var registration = CliJson.Deserialize<RegistrationMessage>(message);
            if (registration == null || registration.Type != "register")
            {
                await ws.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "Expected register message", CancellationToken.None);
                return;
            }

            var id = AgentRegistration.ComputeId(
                registration.PackageId ?? registration.Project,
                registration.Tfm,
                registration.SessionId,
                registration.ProcessId);

            // If the agent already has an HTTP listener (late reconnection), use its current port
            int assignedPort;
            if (registration.CurrentPort is > 0)
            {
                assignedPort = registration.CurrentPort.Value;
            }
            else
            {
                var newPort = AssignPort();
                if (newPort == null)
                {
                    var errorMsg = CliJson.SerializeUntyped(new JsonObject
                    {
                        ["type"] = "error",
                        ["message"] = "No ports available"
                    }, indented: false);
                    await ws.SendAsync(Encoding.UTF8.GetBytes(errorMsg), WebSocketMessageType.Text, true, CancellationToken.None);
                    await ws.CloseAsync(WebSocketCloseStatus.InternalServerError, "No ports available", CancellationToken.None);
                    return;
                }
                assignedPort = newPort.Value;
            }

            var agent = new AgentRegistration
            {
                Id = id,
                Project = registration.Project,
                Tfm = registration.Tfm,
                Platform = registration.Platform,
                AppName = registration.AppName,
                PackageId = registration.PackageId,
                DeviceId = registration.DeviceId,
                Port = assignedPort,
                Version = registration.Version,
                SessionId = registration.SessionId,
                ProcessId = registration.ProcessId,
                InstanceId = AgentRegistration.ComputeInstanceId(
                        registration.PackageId ?? registration.Project,
                        registration.Tfm,
                        registration.SessionId,
                        registration.ProcessId) ??
                    Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
                ConnectedAt = DateTime.UtcNow
            };

            var connection = new AgentConnection(agent, ws);
            var routeGate = AgentRouteGate(id);
            await routeGate.WaitAsync(_cts?.Token ?? CancellationToken.None);
            try
            {
                var replaced = ReplaceConnection(_agents, id, connection);
                if (_inspectors.TryRemove(id, out var staleInspector))
                    staleInspector.Dispose();
                if (replaced is not null)
                {
                    // A dropped socket loses the delivery evidence an in-flight run depends on, so
                    // the run is abandoned whether or not the process behind it is the same one.
                    // Only the wording distinguishes the two, because a same-process reconnect now
                    // keeps its instance identity and "reconnected with a new instance" would be
                    // false.
                    _workflowRuns.MarkAgentInstanceUnavailable(
                        replaced.Registration.Id,
                        replaced.Registration.InstanceId,
                        string.Equals(replaced.Registration.InstanceId, agent.InstanceId, StringComparison.Ordinal)
                            ? "The agent connection was replaced, so in-flight command delivery can no longer be proven."
                            : "The agent reconnected with a new instance.");
                    if (replaced.Registration.Port != assignedPort)
                        ReleasePort(replaced.Registration.Port);
                    try { replaced.WebSocket.Dispose(); } catch { }
                    Log($"Agent replaced: {agent.AppName}|{agent.Tfm} (was port {replaced.Registration.Port})");
                }
            }
            finally
            {
                routeGate.Release();
            }
            publishedConnection = connection;

            Log($"Agent connected: {agent.AppName}|{agent.Tfm} → port {assignedPort} (id: {id})");

            // Send registration response
            var response = CliJson.SerializeUntyped(new JsonObject
            {
                ["type"] = "registered",
                ["id"] = id,
                ["port"] = assignedPort,
                ["instanceId"] = agent.InstanceId
            }, indented: false);
            await ws.SendAsync(Encoding.UTF8.GetBytes(response), WebSocketMessageType.Text, true, CancellationToken.None);

            // Keep connection alive — wait for disconnect
            await MonitorAgentConnection(connection);
        }
        catch (WebSocketException) { }
        catch (OperationCanceledException) { }
        finally
        {
            if (publishedConnection is not null)
                await CleanupAgentConnectionAsync(publishedConnection);
            ws.Dispose();
        }
    }

    internal static TConnection? ReplaceConnection<TConnection>(
        ConcurrentDictionary<string, TConnection> connections,
        string id,
        TConnection connection)
        where TConnection : class
    {
        while (true)
        {
            if (connections.TryGetValue(id, out var existing))
            {
                if (connections.TryUpdate(id, connection, existing))
                    return existing;
                continue;
            }

            if (connections.TryAdd(id, connection))
                return null;
        }
    }

    private async Task MonitorAgentConnection(AgentConnection connection)
    {
        var buffer = new byte[256];
        try
        {
            while (connection.WebSocket.State == WebSocketState.Open && !(_cts?.Token.IsCancellationRequested ?? true))
            {
                var result = await connection.WebSocket.ReceiveAsync(buffer, _cts?.Token ?? CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;
                TouchActivity();
            }
        }
        catch { }
    }

    private async Task CleanupAgentConnectionAsync(AgentConnection connection)
    {
        // Use the KeyValuePair overload so a reconnecting agent that re-registered with the same ID
        // cannot be evicted by stale cleanup from the superseded socket.
        var routeGate = AgentRouteGate(connection.Registration.Id);
        await routeGate.WaitAsync();
        try
        {
            if (_agents.TryRemove(new KeyValuePair<string, AgentConnection>(connection.Registration.Id, connection)))
            {
                var stateGate = AgentStateGate(connection.Registration.Id);
                await stateGate.WaitAsync();
                try
                {
                    ReleasePort(connection.Registration.Port);
                    var leaseKey = LeaseKeyForRegistration(connection.Registration);
                    if (string.Equals(leaseKey, connection.Registration.Id, StringComparison.Ordinal))
                        _mutationLeases.Remove(leaseKey);
                    _flows.RemoveAgent(connection.Registration.Id);
                    _workflowRuns.MarkAgentInstanceUnavailable(
                        connection.Registration.Id,
                        connection.Registration.InstanceId,
                        "The agent disconnected.");
                    if (_inspectors.TryRemove(connection.Registration.Id, out var inspector))
                        inspector.Dispose();
                    Log($"Agent disconnected: {connection.Registration.AppName}|{connection.Registration.Tfm}");
                }
                finally
                {
                    stateGate.Release();
                }
            }
        }
        finally
        {
            routeGate.Release();
        }
    }

    private SemaphoreSlim AgentRouteGate(string agentId)
        => _agentRouteGates.GetOrAdd(agentId, static _ => new SemaphoreSlim(1, 1));

    private SemaphoreSlim AgentStateGate(string agentId)
        => _agentStateGates.GetOrAdd(agentId, static _ => new SemaphoreSlim(1, 1));

    private string HandleListAgents()
    {
        var agents = _agents.Values.Select(c => c.Registration).ToArray();
        return CliJson.SerializeUntyped(agents, indented: true);
    }

    private (int, string) HandleShutdown()
    {
        Log("Shutdown requested via API");
        _ = Task.Run(async () =>
        {
            await Task.Delay(100); // Let response send first
            _cts?.Cancel();
        });
        return (200, CliJson.SerializeUntyped(new JsonObject
        {
            ["status"] = "shutting_down"
        }, indented: false));
    }

    private int? AssignPort()
    {
        lock (_portLock)
        {
            for (int port = PortRangeStart; port <= PortRangeEnd; port++)
            {
                if (_assignedPorts.Contains(port)) continue;
                if (IsPortInUse(port)) continue;
                _assignedPorts.Add(port);
                return port;
            }
        }
        return null;
    }

    private void ReleasePort(int port)
    {
        lock (_portLock)
        {
            _assignedPorts.Remove(port);
        }
    }

    private static bool IsPortInUse(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return false;
        }
        catch
        {
            return true;
        }
    }

    private void TouchActivity() => _lastActivity = DateTime.UtcNow;

    private void CheckIdle()
    {
        if (_agents.Count > 0) return;
        if (DateTime.UtcNow - _lastActivity < _idleTimeout) return;

        Log("Idle timeout reached, shutting down");
        _cts?.Cancel();
    }

    private void Shutdown()
    {
        _idleTimer?.Dispose();

        // Tear down agent WebSockets without the graceful close handshake.
        //
        // HttpListener's server-side WebSocket takes its SessionHandle lock before its
        // internal _thisLock, but the close path re-enters that pair while it is still
        // holding _thisLock. That inverts the order against the receive pump, which holds
        // SessionHandle and then waits for _thisLock in StartOnCloseReceived while handling
        // an inbound close frame. When a peer closes at the same moment the broker shuts
        // down, the two sides deadlock. CloseAsync blocks in Monitor.Enter before its first
        // await, so the CloseAsync(...).Wait(2s) that used to be here could never time out:
        // it hung whichever thread cancelled the broker, which on the RunAsync cancellation
        // path is the caller of Cancel() itself.
        //
        // Abort() takes the same locks in the documented order, so it cannot invert them;
        // dropping the handshake is what removes the deadlock. Each socket is torn down on its
        // own dedicated thread so a single wedged socket cannot starve the others, and off the
        // thread pool so a saturated pool cannot delay teardown past the timeout.
        var sockets = _agents.Values.Select(a => a.WebSocket).ToArray();
        if (sockets.Length > 0)
        {
            var teardowns = new List<Thread>(sockets.Length);
            foreach (var socket in sockets)
            {
                var thread = new Thread(() =>
                {
                    try { socket.Abort(); } catch { }
                    try { socket.Dispose(); } catch { }
                })
                { IsBackground = true, Name = "devflow-broker-socket-teardown" };

                thread.Start();
                teardowns.Add(thread);
            }

            // Stopwatch rather than DateTime.UtcNow: the wall clock is not monotonic, and an NTP
            // step during shutdown would either collapse the budget to zero or extend it.
            var budget = TimeSpan.FromSeconds(2);
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            foreach (var thread in teardowns)
            {
                var remaining = budget - elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    break;

                try { thread.Join(remaining); } catch { }
            }
        }
        _agents.Clear();
        _mutationLeases.Clear();
        _flows.Clear();
        _workflowRuns.Dispose();

        // Dispose inspector instances. Without this, a Shutdown() that doesn't
        // go through Dispose() (e.g. /api/shutdown handler or idle timeout)
        // leaks every InspectorServer's AgentClient (HttpClient) and CTS.
        foreach (var insp in _inspectors.Values)
        {
            try { insp.Dispose(); } catch { }
        }
        _inspectors.Clear();

        // Delete state file
        DeleteBrokerState();

        try { _listener?.Close(); } catch { }

        Log("Broker stopped");
    }

    /// <summary>
    /// True when this broker may publish itself into the machine-wide broker state file.
    /// </summary>
    /// <remarks>
    /// <see cref="BrokerPaths.StateFile"/> is a single well-known path shared by every broker on the
    /// machine, and it carries the owner-only native-host approval token. A short-lived broker on an
    /// ephemeral port must not overwrite the entry belonging to the long-running broker a developer
    /// is actually using: the test suite starts thousands of brokers, and a second IDE MCP server
    /// starts its own. Clobbering the entry silently invalidates the running broker's approval token,
    /// so every later approval fails with no signal beyond a confusing error at approval time.
    /// The slot is taken over only once the broker that claimed it is gone, which keeps recovery
    /// automatic after a crash.
    /// </remarks>
    private bool ShouldPublishBrokerState()
    {
        try
        {
            if (!File.Exists(BrokerPaths.StateFile))
                return true;

            var existing = CliJson.Deserialize<BrokerState>(File.ReadAllText(BrokerPaths.StateFile));
            return MayPublishBrokerState(existing, _port, IsProcessAlive);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Decides whether a broker on <paramref name="ourPort"/> may claim the shared state slot
    /// currently held by <paramref name="existing"/>. Unreadable state cannot yield a usable token,
    /// so replacing it is the recovering move.
    /// </summary>
    internal static bool MayPublishBrokerState(
        BrokerState? existing,
        int ourPort,
        Func<int, bool> isProcessAlive)
    {
        if (existing is null || existing.Port == ourPort)
            return true;

        return !isProcessAlive(existing.Pid);
    }

    /// <summary>
    /// Decides whether a broker may retract the shared state slot. Only an exact self-match
    /// qualifies, so a broker can never delete an entry another broker owns.
    /// </summary>
    internal static bool MayDeleteBrokerState(BrokerState? existing, int ourPort, int ourPid)
        => existing is null || (existing.Port == ourPort && existing.Pid == ourPid);

    private static bool IsProcessAlive(int pid)
    {
        if (pid <= 0)
            return false;

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch
        {
            // Treat an undecidable probe as alive so an unrelated failure never destroys a state
            // file we could not prove is stale.
            return true;
        }
    }

    private void WriteBrokerState()
    {
        if (!ShouldPublishBrokerState())
        {
            Log($"Not publishing broker state: {BrokerPaths.StateFile} belongs to another running broker");
            return;
        }

        var tmpPath = BrokerPaths.StateFile + ".tmp";
        try
        {
            var dir = BrokerPaths.ConfigDir;
            Directory.CreateDirectory(dir);
            BrokerPaths.RestrictConfigDirectoryPermissions(dir);

            var state = new BrokerState
            {
                Pid = Environment.ProcessId,
                Port = _port,
                StartedAt = DateTime.UtcNow,
                EmbedToken = _embedToken,
                NativeApprovalToken = _nativeApprovalToken,
            };

            var json = CliJson.SerializeUntyped(state, indented: true);
            File.WriteAllText(tmpPath, json);
            BrokerPaths.RestrictStateFilePermissions(tmpPath);
            File.Move(tmpPath, BrokerPaths.StateFile, overwrite: true);
        }
        catch (Exception ex)
        {
            try { File.Delete(tmpPath); } catch { }
            Log($"Warning: failed to write broker state: {ex.Message}");
        }
    }

    private static string CreateNativeApprovalToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool FixedTimeApprovalTokenEquals(string expected, string? supplied)
        => supplied is not null &&
           expected.Length == supplied.Length &&
           CryptographicOperations.FixedTimeEquals(
               Encoding.UTF8.GetBytes(expected),
               Encoding.UTF8.GetBytes(supplied));

    private async Task HandleMutationLeaseRoute(HttpListenerContext context, string method, string path)
    {
        if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonResponseAsync(context, 405, new JsonObject { ["error"] = "Method not allowed" });
            return;
        }

        var segments = path.Trim('/').Split('/');
        if (segments.Length != 3 ||
            !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) ||
            !segments[1].Equals("leases", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonResponseAsync(context, 404, new JsonObject { ["error"] = "Not found" });
            return;
        }

        var agentId = Uri.UnescapeDataString(segments[2]);

        if (context.Request.ContentLength64 > 64 * 1024)
        {
            await WriteJsonResponseAsync(context, 413, new JsonObject { ["error"] = "Request body too large" });
            return;
        }

        JsonObject body;
        try
        {
            var text = await ReadBoundedBodyAsync(
                context.Request.InputStream,
                context.Request.ContentEncoding ?? Encoding.UTF8,
                64 * 1024);
            body = string.IsNullOrWhiteSpace(text)
                ? new JsonObject()
                : JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
        }
        catch (RequestBodyTooLargeException)
        {
            await WriteJsonResponseAsync(context, 413, new JsonObject { ["error"] = "Request body too large" });
            return;
        }
        catch
        {
            await WriteJsonResponseAsync(context, 400, new JsonObject { ["error"] = "Invalid JSON body" });
            return;
        }

        var action = body["action"]?.GetValue<string>()?.Trim().ToLowerInvariant() ?? "status";
        var leaseId = body["leaseId"]?.GetValue<string>();
        var holderKind = body["holderKind"]?.GetValue<string>();
        var label = body["label"]?.GetValue<string>();
        var force = body["force"]?.GetValue<bool>() ?? false;
        var transactionId = body["transactionId"]?.GetValue<string>();

        MutationLeaseSnapshot status;
        var stateGate = AgentStateGate(agentId);
        await stateGate.WaitAsync(_cts?.Token ?? CancellationToken.None);
        try
        {
            if (!_agents.TryGetValue(agentId, out var connection))
            {
                await WriteJsonResponseAsync(context, 404, new JsonObject { ["error"] = $"Agent '{agentId}' not found" });
                return;
            }
            status = _mutationLeases.Control(
                LeaseKeyForRegistration(connection.Registration),
                action,
                leaseId,
                holderKind,
                label,
                force,
                transactionId);
        }
        catch (ArgumentException ex)
        {
            await WriteJsonResponseAsync(context, 400, new JsonObject { ["error"] = ex.Message });
            return;
        }
        finally
        {
            stateGate.Release();
        }

        await WriteJsonResponseAsync(context, 200, new JsonObject
        {
            ["ok"] = true,
            ["allowed"] = status.Allowed,
            ["youHold"] = status.YouHold,
            ["heldByOther"] = status.HeldByOther,
            ["leaseId"] = status.LeaseId,
            ["transactionId"] = status.TransactionId,
            ["holderKind"] = status.HolderKind,
            ["label"] = status.Label,
            ["expiresInMs"] = status.ExpiresInMs,
            ["authorityEpoch"] = status.AuthorityEpoch,
            ["authority"] = "broker"
        });
    }

    private static async Task WriteJsonResponseAsync(HttpListenerContext context, int statusCode, JsonObject body)
    {
        var bytes = Encoding.UTF8.GetBytes(CliJson.SerializeUntyped(body, indented: false));
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private async Task HandleRecordingRoute(HttpListenerContext context, string method, string path)
    {
        if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonResponseAsync(context, 405, new JsonObject { ["error"] = "Method not allowed" });
            return;
        }

        var segments = path.Trim('/').Split('/');
        if (segments.Length != 3 ||
            !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) ||
            !segments[1].Equals("recordings", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonResponseAsync(context, 404, new JsonObject { ["error"] = "Not found" });
            return;
        }

        var agentId = Uri.UnescapeDataString(segments[2]);
        if (context.Request.ContentLength64 > MaxRecordingRequestChars)
        {
            await WriteJsonResponseAsync(context, 413, new JsonObject { ["error"] = "Request body too large" });
            return;
        }

        JsonObject body;
        try
        {
            var text = await ReadBoundedBodyAsync(
                context.Request.InputStream,
                context.Request.ContentEncoding ?? Encoding.UTF8,
                MaxRecordingRequestChars);
            body = string.IsNullOrWhiteSpace(text)
                ? new JsonObject()
                : JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
        }
        catch (RequestBodyTooLargeException)
        {
            await WriteJsonResponseAsync(context, 413, new JsonObject { ["error"] = "Request body too large" });
            return;
        }
        catch
        {
            await WriteJsonResponseAsync(context, 400, new JsonObject { ["error"] = "Invalid JSON body" });
            return;
        }

        var action = body["action"]?.GetValue<string>()?.Trim().ToLowerInvariant() ?? "status";
        var leaseId = body["leaseId"]?.GetValue<string>();
        var recordingId = body["recordingId"]?.GetValue<string>();
        if (!action.Equals("status", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(leaseId))
        {
            await WriteJsonResponseAsync(context, 400, new JsonObject { ["error"] = "leaseId is required" });
            return;
        }
        if (!action.Equals("status", StringComparison.Ordinal))
        {
            var leaseKey = _agents.TryGetValue(agentId, out var recordingConnection)
                ? LeaseKeyForRegistration(recordingConnection.Registration)
                : agentId;
            var lease = _mutationLeases.Control(leaseKey, "validate", leaseId, null, null, force: false);
            if (!lease.Allowed)
            {
                await WriteJsonResponseAsync(context, 409, new JsonObject
                {
                    ["ok"] = false,
                    ["reason"] = "lease",
                    ["error"] = "Another session is driving this app.",
                    ["holderKind"] = lease.HolderKind,
                    ["label"] = lease.Label
                });
                return;
            }
        }

        BrokerFlowResult result;
        var stateGate = AgentStateGate(agentId);
        await stateGate.WaitAsync(_cts?.Token ?? CancellationToken.None);
        try
        {
            if (!_agents.TryGetValue(agentId, out var connection))
            {
                await WriteJsonResponseAsync(context, 404, new JsonObject { ["error"] = $"Agent '{agentId}' not found" });
                return;
            }
            var flowAgentId = connection.Registration.Id;
            var stableFlowAgentId = RouteCheckpointCoordinator.StableAgentId(connection.Registration);
            _flows.ConnectAgent(
                flowAgentId,
                stableFlowAgentId,
                connection.Registration.SessionId,
                recordingId);

            switch (action)
            {
                case "start":
                    result = _flows.Start(
                        flowAgentId,
                        body["name"]?.GetValue<string>() ?? "scenario",
                        body["app"]?.GetValue<string>() ?? connection.Registration.AppName,
                        body["platform"]?.GetValue<string>() ?? connection.Registration.Platform,
                        body["preconditions"]?.GetValue<string>(),
                        connection.Registration.SessionId,
                        stableFlowAgentId);
                    break;
                case "status":
                    result = _flows.Status(flowAgentId, recordingId);
                    break;
                case "observe":
                    var observationNode = body["observation"];
                    var observation = observationNode?.Deserialize<FlowObservation>(
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    result = observation is null
                        ? BrokerFlowResult.Failure("observation is required")
                        : _flows.Observe(flowAgentId, observation, recordingId);
                    break;
                case "stop":
                    result = _flows.Stop(flowAgentId, recordingId);
                    break;
                case "cancel":
                    result = _flows.Cancel(flowAgentId, recordingId);
                    break;
                case "cancel-if-empty":
                    result = _flows.CancelIfEmpty(flowAgentId, recordingId);
                    break;
                default:
                    result = BrokerFlowResult.Failure($"Unknown recording action '{action}'.");
                    break;
            }
        }
        finally
        {
            stateGate.Release();
        }

        var resultNode = JsonSerializer.SerializeToNode(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        })?.AsObject() ?? new JsonObject();
        await WriteJsonResponseAsync(context, result.Ok ? 200 : 400, resultNode);
    }

    /// <summary>
    /// Broker-owned local route checkpoint API. Unlike workflow recording this deliberately does
    /// not require a mutation lease: save/restore/clear are explicit user commands, and restore is
    /// never performed by connection lifecycle code.
    /// </summary>
    private async Task HandleCheckpointRoute(HttpListenerContext context, string method, string path)
    {
        var segments = path.Trim('/').Split('/');
        if (segments.Length != 3 ||
            !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) ||
            !segments[1].Equals("checkpoints", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonResponseAsync(context, 404, new JsonObject { ["error"] = "Not found" });
            return;
        }

        var agentId = Uri.UnescapeDataString(segments[2]);
        var action = method.Equals("GET", StringComparison.OrdinalIgnoreCase) ? "status" : null;
        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            if (context.Request.ContentLength64 > 16 * 1024)
            {
                await WriteJsonResponseAsync(context, 413, new JsonObject { ["error"] = "Request body too large" });
                return;
            }
            try
            {
                var text = await ReadBoundedBodyAsync(
                    context.Request.InputStream,
                    context.Request.ContentEncoding ?? Encoding.UTF8,
                    16 * 1024);
                action = string.IsNullOrWhiteSpace(text)
                    ? "status"
                    : JsonNode.Parse(text)?["action"]?.GetValue<string>()?.Trim().ToLowerInvariant();
            }
            catch
            {
                await WriteJsonResponseAsync(context, 400, new JsonObject { ["error"] = "Invalid JSON body" });
                return;
            }
        }
        else if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonResponseAsync(context, 405, new JsonObject { ["error"] = "Method not allowed" });
            return;
        }

        if (!_agents.TryGetValue(agentId, out var connection))
        {
            await WriteJsonResponseAsync(context, 404, new JsonObject
            {
                ["ok"] = false,
                ["error"] = $"Agent '{agentId}' is not connected. Checkpoints are retained until explicitly cleared or expired."
            });
            return;
        }

        RouteCheckpointStatus status;
        try
        {
            status = action switch
            {
                "status" => _checkpoints.Status(connection.Registration),
                "save" => await _checkpoints.SaveAsync(connection.Registration, _cts?.Token ?? CancellationToken.None),
                "restore" => await _checkpoints.RestoreAsync(connection.Registration, _cts?.Token ?? CancellationToken.None),
                "clear" => ClearCheckpoint(connection.Registration),
                _ => new RouteCheckpointStatus
                {
                    Ok = false,
                    Connected = true,
                    Warning = $"Unknown checkpoint action '{action}'."
                }
            };
        }
        catch (Exception ex)
        {
            Log($"Warning: checkpoint {action} failed for {agentId}: {ex.Message}");
            status = new RouteCheckpointStatus
            {
                Ok = false,
                Connected = true,
                Warning = "Checkpoint operation failed."
            };
        }

        var node = JsonSerializer.SerializeToNode(status, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        })?.AsObject() ?? new JsonObject();
        await WriteJsonResponseAsync(context, status.Ok ? 200 : 400, node);
    }

    private RouteCheckpointStatus ClearCheckpoint(AgentRegistration registration)
    {
        _checkpoints.Clear(registration);
        return new RouteCheckpointStatus { Connected = true, HasCheckpoint = false };
    }

    /// <summary>
    /// Imports foreign diagnostic artifacts into a memory-only, capability-gated quarantine.
    /// Import is deliberately read-only: no flow is executed, no workspace path is used, and no
    /// repair history is appended. Attestation facts are not accepted on this browser-facing route;
    /// imports remain untrusted unless a trusted host calls the provider-neutral policy directly.
    /// </summary>
    private async Task HandleArtifactTrustRoute(HttpListenerContext context, string method, string path)
    {
        const string capabilityHeader = "X-Maui-Artifact-Capability";
        var normalizedPath = path.TrimEnd('/');

        if (string.Equals(normalizedPath, "/api/artifact-trust/import", StringComparison.OrdinalIgnoreCase))
        {
            if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTypedJsonResponseAsync(context, 405, ArtifactTrustRouteResponse.Failure("Method not allowed."));
                return;
            }

            var kind = HttpUtility.ParseQueryString(context.Request.Url?.Query ?? string.Empty)["kind"];
            if (!ArtifactTrustImportKinds.IsKnown(kind))
            {
                await WriteTypedJsonResponseAsync(context, 400, ArtifactTrustRouteResponse.Failure(
                    "An explicit supported artifact kind is required."));
                return;
            }

            var maximum = string.Equals(kind, ArtifactTrustImportKinds.FlowRun, StringComparison.Ordinal)
                ? ArtifactTrustImportService.MaxFlowRunBytes
                : (int)Evidence.EvidenceFormat.MaxBundleFileBytes;
            var bytes = await ReadArtifactTrustBytesAsync(context, maximum);
            if (bytes is null)
                return;

            var imported = _artifactTrustImports.Import(
                bytes,
                kind!,
                policy: null,
                verifiedProvenance: null,
                _cts?.Token ?? CancellationToken.None);
            if (!imported.Ok || imported.Artifact is null)
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    400,
                    ArtifactTrustRouteResponse.Failure(imported.Error ?? "The artifact could not be imported."));
                return;
            }

            var stored = _artifactTrustStore.Add(imported.Artifact);
            if (!stored.Ok)
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    409,
                    ArtifactTrustRouteResponse.Failure(stored.Error ?? "The artifact could not be retained."));
                return;
            }

            await WriteTypedJsonResponseAsync(context, 201, new ArtifactTrustRouteResponse
            {
                Ok = true,
                CapabilityToken = stored.CapabilityToken,
                Status = stored.Status,
            });
            return;
        }

        var segments = normalizedPath.Trim('/').Split('/');
        if (segments.Length != 4 ||
            !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) ||
            !segments[1].Equals("artifact-trust", StringComparison.OrdinalIgnoreCase))
        {
            await WriteTypedJsonResponseAsync(context, 404, ArtifactTrustRouteResponse.Failure("Not found."));
            return;
        }

        var artifactId = Uri.UnescapeDataString(segments[2]);
        var action = segments[3];
        var capabilityToken = context.Request.Headers[capabilityHeader];
        if (string.Equals(action, "status", StringComparison.OrdinalIgnoreCase))
        {
            if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTypedJsonResponseAsync(context, 405, ArtifactTrustRouteResponse.Failure("Method not allowed."));
                return;
            }

            var result = _artifactTrustStore.GetStatus(artifactId, capabilityToken);
            await WriteTypedJsonResponseAsync(
                context,
                result.StatusCode,
                result.StatusCode == 200
                    ? new ArtifactTrustRouteResponse { Ok = true, Status = result.Status }
                    : ArtifactTrustRouteResponse.Failure(result.Error ?? "Imported artifact was not found."));
            return;
        }

        if (string.Equals(action, "projection", StringComparison.OrdinalIgnoreCase))
        {
            if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTypedJsonResponseAsync(context, 405, ArtifactTrustRouteResponse.Failure("Method not allowed."));
                return;
            }

            var result = _artifactTrustStore.GetSafeProjection(artifactId, capabilityToken);
            await WriteTypedJsonResponseAsync(
                context,
                result.StatusCode,
                result.StatusCode == 200
                    ? new ArtifactTrustRouteResponse { Ok = true, Projection = result.Projection }
                    : ArtifactTrustRouteResponse.Failure(result.Error ?? "Imported artifact was not found."));
            return;
        }

        if (string.Equals(action, "bind-local-reproduction", StringComparison.OrdinalIgnoreCase))
        {
            if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTypedJsonResponseAsync(context, 405, ArtifactTrustRouteResponse.Failure("Method not allowed."));
                return;
            }

            // Validate imported-artifact authority before revealing whether a local run exists.
            var access = _artifactTrustStore.GetStatus(artifactId, capabilityToken);
            if (access.StatusCode != 200)
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    access.StatusCode,
                    ArtifactTrustRouteResponse.Failure(access.Error ?? "Imported artifact was not found."));
                return;
            }

            var request = await ReadArtifactTrustJsonBodyAsync<ArtifactTrustLocalReproductionRequest>(context, 32 * 1024);
            if (request is null)
                return;
            if (string.IsNullOrWhiteSpace(request.LocalRunId) || request.LocalRunId.Length > 128 || request.Current is null)
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    400,
                    ArtifactTrustRouteResponse.Failure("localRunId and current reproduction facts are required."));
                return;
            }

            var local = _workflowRuns.GetLocalReproductionFacts(request.LocalRunId);
            if (!local.Ok || local.Facts is null)
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    409,
                    ArtifactTrustRouteResponse.Failure(local.Error ?? "The local run cannot establish reproduction facts."));
                return;
            }

            var bound = _artifactTrustStore.BindLocalReproduction(
                artifactId,
                capabilityToken,
                local.Facts,
                request.Current);
            await WriteTypedJsonResponseAsync(
                context,
                bound.StatusCode,
                bound.StatusCode == 200
                    ? new ArtifactTrustRouteResponse
                    {
                        Ok = true,
                        Status = bound.Status,
                        Reproduction = bound.Evaluation,
                    }
                    : ArtifactTrustRouteResponse.Failure(bound.Error ?? "The reproduction binding was rejected."));
            return;
        }

        await WriteTypedJsonResponseAsync(context, 404, ArtifactTrustRouteResponse.Failure("Not found."));
    }

    private async Task HandleTestAgentRoute(HttpListenerContext context, string method, string path)
    {
        const int maxBodyChars = 1_048_576;
        var normalizedPath = path.TrimEnd('/');
        if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            await WriteTypedJsonResponseAsync(
                context,
                405,
                new Microsoft.Maui.DevFlow.Testing.MauiTestAgentToolResult
                {
                    Error = TestAgentRouteError(
                        Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCodes.UnsupportedOperation,
                        Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCategories.Unsupported,
                        "Only POST is supported by the restricted test-agent broker protocol.",
                        retryable: false),
                });
            return;
        }
        if (!DevFlowPreviewPolicy.IsBrokerTestAgentRouteEnabled(_previewFlags, normalizedPath))
        {
            await WriteTypedJsonResponseAsync(
                context,
                404,
                new Microsoft.Maui.DevFlow.Testing.MauiTestAgentToolResult
                {
                    Error = TestAgentRouteError(
                        Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCodes.UnsupportedOperation,
                        Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCategories.Unsupported,
                    "The restricted test-agent preview is disabled by the effective agent-authoring feature policy. " +
                        "Set DEVFLOW_PREVIEW_AGENT_AUTHORING=true in the environment that starts the broker, then restart " +
                        "the broker: setting it only in an agent host or a new shell does not change a broker that is " +
                        "already running. PowerShell: $env:DEVFLOW_PREVIEW_AGENT_AUTHORING = 'true'; maui devflow broker " +
                        "stop; maui devflow broker start.",
                        retryable: false),
                });
            return;
        }

        if (string.Equals(normalizedPath, "/api/test-agent/sessions/begin", StringComparison.OrdinalIgnoreCase))
        {
            var request = await ReadWorkflowRunBodyAsync<Microsoft.Maui.DevFlow.Testing.MauiTestAgentSessionBeginRequest>(context, maxBodyChars);
            if (request is null)
                return;
            request.TargetState = await GetLiveTestAgentTargetStateAsync(request.Envelope?.Target, request.TargetState);
            if (request.TargetState is null)
            {
                await WriteTypedJsonResponseAsync(context, 409, new Microsoft.Maui.DevFlow.Testing.MauiTestAgentSessionResult
                {
                    Error = TestAgentRouteError(
                        Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCodes.TargetStale,
                        Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCategories.Target,
                        "The explicit target agent and instance are not currently connected.",
                        retryable: false),
                });
                return;
            }
            CanonicalizeTestAgentTarget(request.Envelope?.Target, request.TargetState);

            var result = _testAgentSessions.Begin(request);
            await WriteTypedJsonResponseAsync(context, TestAgentStatusCode(result.Error), result);
            return;
        }

        if (string.Equals(normalizedPath, "/api/test-agent/sessions/status", StringComparison.OrdinalIgnoreCase))
        {
            var request = await ReadWorkflowRunBodyAsync<Microsoft.Maui.DevFlow.Testing.MauiTestAgentSessionAccessRequest>(context, maxBodyChars);
            if (request is null)
                return;
            var result = _testAgentSessions.Status(request);
            await WriteTypedJsonResponseAsync(context, TestAgentStatusCode(result.Error), result);
            return;
        }

        if (string.Equals(normalizedPath, "/api/test-agent/sessions/abandon", StringComparison.OrdinalIgnoreCase))
        {
            var request = await ReadWorkflowRunBodyAsync<Microsoft.Maui.DevFlow.Testing.MauiTestAgentSessionAccessRequest>(context, maxBodyChars);
            if (request is null)
                return;
            var result = _testAgentSessions.Abandon(request);
            await WriteTypedJsonResponseAsync(context, TestAgentStatusCode(result.Error), result);
            return;
        }

        if (string.Equals(normalizedPath, "/api/test-agent/approvals/request", StringComparison.OrdinalIgnoreCase))
        {
            if (_trustedHostApprovalVerifier is null)
            {
                await WriteTypedJsonResponseAsync(context, 501, new Microsoft.Maui.DevFlow.Testing.MauiTestAgentApprovalResult
                {
                    Error = TestAgentRouteError(
                        Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCodes.HumanApprovalRequired,
                        Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCategories.Unsupported,
                        "Native host approval is unavailable in this broker build. Approval requests are disabled rather than presented as actionable.",
                        retryable: false),
                });
                return;
            }
            var request = await ReadWorkflowRunBodyAsync<Microsoft.Maui.DevFlow.Testing.MauiTestAgentApprovalSubmitRequest>(context, maxBodyChars);
            if (request is null)
                return;
            var result = _testAgentSessions.SubmitApprovalRequest(request);
            await WriteTypedJsonResponseAsync(context, TestAgentStatusCode(result.Error), result);
            return;
        }

        if (string.Equals(normalizedPath, "/api/test-agent/grants/issue", StringComparison.OrdinalIgnoreCase))
        {
            // Kept as an explicit refusal rather than deleted: the route name appears in earlier
            // protocol drafts, and a 501 that says why is clearer to a caller than a 404 that
            // reads like a missing build. There is no direct grant path behind it in any build —
            // a grant exists only as the outcome of deciding a persisted approval request.
            await WriteTypedJsonResponseAsync(context, 501, new Microsoft.Maui.DevFlow.Testing.MauiTestAgentGrantIssueResult
            {
                Error = TestAgentRouteError(
                    Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCodes.HumanApprovalRequired,
                    Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCategories.Unsupported,
                    "Direct grant issuance does not exist. Submit an approval request, then have it decided by an owner-token approval host such as 'maui devflow approve <approval-request-id>'.",
                    retryable: false),
            });
            return;
        }

        if (string.Equals(normalizedPath, "/api/test-agent/mutations/authorize", StringComparison.OrdinalIgnoreCase))
        {
            var request = await ReadWorkflowRunBodyAsync<Microsoft.Maui.DevFlow.Testing.MauiTestAgentMutationAuthorizationRequest>(context, maxBodyChars);
            if (request is null)
                return;
            request.CurrentTargetState = await GetLiveTestAgentTargetStateAsync(
                request.Envelope?.Target,
                request.CurrentTargetState);
            if (request.CurrentTargetState is null)
            {
                await WriteTypedJsonResponseAsync(context, 409, new Microsoft.Maui.DevFlow.Testing.MauiTestAgentMutationAuthorizationResult
                {
                    Error = TestAgentRouteError(
                        Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCodes.TargetStale,
                        Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCategories.Target,
                        "The explicit target agent and instance are not currently connected.",
                        retryable: false),
                });
                return;
            }

            var result = _testAgentSessions.AuthorizeMutation(request);
            await WriteTypedJsonResponseAsync(context, TestAgentStatusCode(result.Error), result);
            return;
        }

        if (string.Equals(normalizedPath, "/api/test-agent/exploration/authorize", StringComparison.OrdinalIgnoreCase))
        {
            var request = await ReadWorkflowRunBodyAsync<Microsoft.Maui.DevFlow.Testing.MauiTestAgentExplorationRequest>(context, maxBodyChars);
            if (request is null)
                return;
            request.CurrentTargetState = await GetLiveTestAgentTargetStateAsync(
                request.Envelope?.Target,
                request.CurrentTargetState);
            if (request.CurrentTargetState is null)
            {
                await WriteTypedJsonResponseAsync(context, 409, new Microsoft.Maui.DevFlow.Testing.MauiTestAgentExplorationResult
                {
                    Error = TestAgentRouteError(
                        Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCodes.TargetStale,
                        Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCategories.Target,
                        "The explicit target agent and instance are not currently connected.",
                        retryable: false),
                });
                return;
            }

            var result = _testAgentSessions.AuthorizeExploration(request);
            await WriteTypedJsonResponseAsync(context, TestAgentStatusCode(result.Error), result);
            return;
        }

        if (string.Equals(normalizedPath, "/api/test-agent/mutations/complete", StringComparison.OrdinalIgnoreCase))
        {
            var request = await ReadWorkflowRunBodyAsync<Microsoft.Maui.DevFlow.Testing.MauiTestAgentMutationCompletion>(context, maxBodyChars);
            if (request is null)
                return;
            var result = _testAgentSessions.CompleteMutation(request);
            await WriteTypedJsonResponseAsync(context, TestAgentStatusCode(result.Error), result);
            return;
        }

        if (string.Equals(normalizedPath, "/api/test-agent/draft/action", StringComparison.OrdinalIgnoreCase))
        {
            var request = await ReadWorkflowRunBodyAsync<Microsoft.Maui.DevFlow.Testing.MauiTestAgentActionRequest>(context, maxBodyChars);
            if (request is null)
                return;
            var result = _testAgentSessions.AppendAction(
                request.Envelope?.Correlation?.AuthoringSessionId,
                request.AuthorizationId,
                request);
            await WriteTypedJsonResponseAsync(context, TestAgentStatusCode(result.Error), result);
            return;
        }

        if (string.Equals(normalizedPath, "/api/test-agent/draft/assertion", StringComparison.OrdinalIgnoreCase))
        {
            var request = await ReadWorkflowRunBodyAsync<Microsoft.Maui.DevFlow.Testing.MauiTestAgentAssertionRequest>(context, maxBodyChars);
            if (request is null)
                return;
            var result = _testAgentSessions.AddAssertion(
                request.Envelope?.Correlation?.AuthoringSessionId,
                request.AuthorizationId,
                request);
            await WriteTypedJsonResponseAsync(context, TestAgentStatusCode(result.Error), result);
            return;
        }

        if (string.Equals(normalizedPath, "/api/test-agent/draft/commit", StringComparison.OrdinalIgnoreCase))
        {
            var request = await ReadWorkflowRunBodyAsync<Microsoft.Maui.DevFlow.Testing.MauiTestAgentSessionAccessRequest>(context, maxBodyChars);
            if (request is null)
                return;
            var result = _testAgentSessions.Commit(request, request.AuthorizationId);
            await WriteTypedJsonResponseAsync(context, TestAgentStatusCode(result.Error), result);
            return;
        }

        if (string.Equals(normalizedPath, "/api/test-agent/draft/migrate-preview", StringComparison.OrdinalIgnoreCase))
        {
            var request = await ReadWorkflowRunBodyAsync<Microsoft.Maui.DevFlow.Testing.MauiTestAgentSessionAccessRequest>(context, maxBodyChars);
            if (request is null)
                return;
            var result = _testAgentSessions.MigratePreview(request);
            await WriteTypedJsonResponseAsync(context, TestAgentStatusCode(result.Error), result);
            return;
        }

        if (string.Equals(normalizedPath, "/api/test-agent/patch", StringComparison.OrdinalIgnoreCase))
        {
            var request = await ReadWorkflowRunBodyAsync<Microsoft.Maui.DevFlow.Testing.MauiTestAgentPatchRequest>(context, maxBodyChars);
            if (request is null)
                return;
            var result = _testAgentSessions.Patch(request);
            await WriteTypedJsonResponseAsync(context, TestAgentStatusCode(result.Error), result);
            return;
        }

        if (string.Equals(normalizedPath, "/api/test-agent/audit", StringComparison.OrdinalIgnoreCase))
        {
            var request = await ReadWorkflowRunBodyAsync<Microsoft.Maui.DevFlow.Testing.MauiTestAgentSessionAccessRequest>(context, maxBodyChars);
            if (request is null)
                return;
            var result = _testAgentSessions.Audit(request);
            await WriteTypedJsonResponseAsync(context, TestAgentStatusCode(result.Error), result);
            return;
        }

        if (string.Equals(normalizedPath, "/api/test-agent/runs/bind", StringComparison.OrdinalIgnoreCase))
        {
            var request = await ReadWorkflowRunBodyAsync<Microsoft.Maui.DevFlow.Testing.MauiTestAgentRunBindingRequest>(context, maxBodyChars);
            if (request is null)
                return;
            var result = _testAgentSessions.BindRun(request);
            await WriteTypedJsonResponseAsync(context, TestAgentStatusCode(result.Error), result);
            return;
        }

        if (string.Equals(normalizedPath, "/api/test-agent/runs/validate", StringComparison.OrdinalIgnoreCase))
        {
            var request = await ReadWorkflowRunBodyAsync<Microsoft.Maui.DevFlow.Testing.MauiTestAgentRunBindingRequest>(context, maxBodyChars);
            if (request is null)
                return;
            var result = _testAgentSessions.ValidateRunBinding(request);
            await WriteTypedJsonResponseAsync(context, TestAgentStatusCode(result.Error), result);
            return;
        }

        if (string.Equals(normalizedPath, "/api/test-agent/reset-offer", StringComparison.OrdinalIgnoreCase))
        {
            await HandleTestAgentResetOfferAsync(context, maxBodyChars);
            return;
        }

        await WriteTypedJsonResponseAsync(context, 404, new Microsoft.Maui.DevFlow.Testing.MauiTestAgentToolResult
        {
            Error = TestAgentRouteError(
                Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCodes.UnsupportedOperation,
                Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCategories.Unsupported,
                "The requested restricted test-agent broker operation is not supported.",
                retryable: false),
        });
    }

    /// <summary>
    /// Reports what the target's lifecycle reset owner would establish, without establishing it.
    /// </summary>
    /// <remarks>
    /// Admission compares a plan's declared seed fingerprint against the one an owner reports, but
    /// that fingerprint digests owner, strategy, app, device, and build — facts only the broker
    /// holds. Without this an author can only guess a value that fails closed, or spend a one-shot
    /// run grant to discover it. The answer is deliberately an offer: it performs no reset, and the
    /// broker still refuses admission unless an owner later actually attests one.
    /// </remarks>
    private async Task HandleTestAgentResetOfferAsync(HttpListenerContext context, int maxBodyChars)
    {
        var request = await ReadWorkflowRunBodyAsync<Microsoft.Maui.DevFlow.Testing.MauiTestAgentRunBindingRequest>(
            context,
            maxBodyChars);
        if (request is null)
            return;

        var target = request.Envelope?.Target;
        if (target is null ||
            string.IsNullOrWhiteSpace(target.AgentId) ||
            !_agents.TryGetValue(target.AgentId, out var connection) ||
            !string.Equals(connection.Registration.InstanceId, target.AgentInstanceId, StringComparison.Ordinal))
        {
            await WriteJsonResponseAsync(context, 409, new JsonObject
            {
                ["ok"] = false,
                ["reason"] = "target-unavailable",
                ["note"] = "The reset offer requires the exact connected agentId and agentInstanceId.",
            });
            return;
        }

        var owner = CreateAppActionResetOwnerFor(connection.Registration);
        if (owner is null)
        {
            await WriteJsonResponseAsync(context, 200, new JsonObject
            {
                ["ok"] = true,
                ["ownerAvailable"] = false,
                ["note"] =
                    "No lifecycle reset owner is registered for this target, so a plan that sets " +
                    "reset.required=true cannot be admitted. Either add an in-app reset action, or " +
                    "declare reset.required=false and state that repeated runs are not independent.",
            });
            return;
        }

        // Whether the app still advertises the action is a live fact, so it is probed rather than
        // assumed: an app can be rebuilt without it while the broker keeps running.
        var canReset = await owner.CanResetAsync(CancellationToken.None).ConfigureAwait(false);
        var offer = owner.DescribeOffer();

        await WriteJsonResponseAsync(context, 200, new JsonObject
        {
            ["ok"] = true,
            ["ownerAvailable"] = canReset,
            ["ownerId"] = offer.OwnerId,
            ["strategy"] = offer.Strategy,
            ["resetIdentity"] = offer.ResetIdentity,
            ["seedFingerprint"] = offer.SeedFingerprint,
            ["backendStateFingerprint"] = offer.BackendStateFingerprint,
            ["sideEffectPolicy"] = Microsoft.Maui.DevFlow.Testing.MauiFlowSideEffectPolicies.AppStateResettable,
            ["note"] = canReset
                ? "Declare reset.strategy and reset.seedFingerprint exactly as reported, with " +
                  "sideEffectPolicy app-state-resettable. This owner resets app state only and seeds " +
                  "no backend, so test-tenant-resettable can never be admitted for it. This is an " +
                  "offer, not evidence: admission still requires an owner to attest a real reset."
                : "The app does not currently advertise the reset action this owner needs, so a " +
                  "reset-requiring plan would fail admission. Ask the human to add it before " +
                  "declaring reset.required=true.",
        });
    }

    /// <summary>
    /// Builds the app-action reset owner for a registration, or null when the app cannot host one.
    /// </summary>
    /// <summary>
    /// Performs a plan's declared lifecycle reset and records what the owner attested, before
    /// replay admission is evaluated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Admission compares a declared reset contract against evidence an owner actually produced,
    /// and fails closed when that evidence is absent. Describing an offer is not evidence, so
    /// without this step a plan that correctly declares <c>reset.required</c> is denied on every
    /// attempt — and because the denial happens after the grant is bound, each attempt silently
    /// spends a one-shot run approval while executing nothing.
    /// </para>
    /// <para>
    /// The reset runs here rather than inside the coordinator because it is outbound async work
    /// against the app, and because doing it before <c>Start</c> keeps one attested result visible
    /// to both admission and the run report. Every failure path leaves the context untouched: a
    /// missing owner, a refusal, or a thrown client error must all read as "no reset was proven",
    /// never as a reset that did not happen.
    /// </para>
    /// </remarks>
    private async Task<PreRunResetMutationLease?> EstablishPreRunResetAsync(
        AgentConnection connection,
        WorkflowRunStartRequest request)
    {
        var declared = request?.Plan?.Reset;
        if (declared?.Required != true)
            return null;

        // Only supply evidence that is missing. Overwriting a caller's attestation would let this
        // step relabel a reset somebody else owns.
        if (request!.Context?.Reset is not null)
            return null;

        var owner = CreateAppActionResetOwnerFor(connection.Registration);
        if (owner is null)
            return null;

        if (!string.IsNullOrWhiteSpace(declared.Strategy) &&
            !string.Equals(
                declared.Strategy.Trim(),
                Execution.AppActionFlowLifecycleResetOwner.ResetStrategy,
                StringComparison.Ordinal))
        {
            return null;
        }

        if (declared.BackendTestDataSeed is not null ||
            !await owner.CanResetAsync(CancellationToken.None).ConfigureAwait(false))
        {
            return null;
        }

        var resetLease = PreRunResetMutationLease.TryAcquire(
            _mutationLeases,
            LeaseKeyForRegistration(connection.Registration));
        if (resetLease is null)
            return null;

        owner = CreateAppActionResetOwnerFor(connection.Registration, resetLease.LeaseId);
        if (owner is null)
        {
            resetLease.Dispose();
            return null;
        }

        Execution.FlowLifecycleResetOutcome outcome;
        try
        {
            outcome = await owner.ResetAsync(new Execution.FlowLifecycleResetRequest
            {
                Reason = "flow-replay-admission",
                RequiredStrategy = declared.Strategy,
                ExpectedSeedIdentity = declared.AppStateSeed?.SeedId,
                RequiresBackendSeed = declared.BackendTestDataSeed is not null,
            }).ConfigureAwait(false);
        }
        catch (Exception)
        {
            resetLease.Dispose();
            return null;
        }

        if (!outcome.Succeeded || outcome.Applied is null)
        {
            resetLease.Dispose();
            return null;
        }

        var applied = outcome.Applied;
        if (!resetLease.PrepareHandoff())
        {
            resetLease.Dispose();
            return null;
        }

        request.Context ??= new Microsoft.Maui.DevFlow.Testing.MauiFlowRunContext();
        request.Context.Reset = new Microsoft.Maui.DevFlow.Testing.MauiFlowResetResult
        {
            Requested = true,
            Succeeded = true,
            AppStateSucceeded = applied.AppStateSucceeded,
            BackendTestDataSucceeded = applied.BackendTestDataSucceeded,
            Strategy = applied.Strategy,
            ResetIdentity = applied.ResetIdentity,
            SeedFingerprint = applied.SeedFingerprint,
            BackendStateFingerprint = applied.BackendStateFingerprint,
        };

        // A plan may also declare the seed as a checkpoint precondition, which admission compares
        // against an observed value. Live target observation deliberately reports no seed, because
        // the app cannot be trusted to name its own state, so the only thing that can supply it is
        // a reset owner that just established it — which is exactly what happened here. Without
        // this, declaring the same seed the reset offer reports is unsatisfiable: the expected
        // value is present, the observed one never is, and the run is denied for a precondition
        // that was in fact met.
        var seeded = request.Context.Preconditions ??= new Microsoft.Maui.DevFlow.Testing.MauiFlowReplayPreconditions();
        seeded.Observed ??= new Microsoft.Maui.DevFlow.Testing.MauiFlowCheckpoint();
        // Only the seeds are attested here. Route, window, and the rest stay whatever the host
        // observed, because a reset owner establishes state, not where the app is standing.
        seeded.Observed.SeedFingerprint ??= applied.SeedFingerprint;
        seeded.Observed.BackendStateFingerprint ??= applied.BackendStateFingerprint;
        return resetLease;
    }

    /// <remarks>
    /// Shared so the offer a caller is told to declare and the reset a run later performs are
    /// derived from exactly the same identity facts. Computing them apart would let an author
    /// declare a plausible fingerprint that admission then rejects.
    /// </remarks>
    internal static Execution.AppActionFlowLifecycleResetOwner? CreateAppActionResetOwnerFor(
        AgentRegistration registration,
        string? preclaimedMutationLeaseId = null)
    {
        // The package is the app identity a reset re-establishes. Without it the owner cannot build
        // a stable reset identity, so it declines rather than digesting an empty string.
        if (registration is null || registration.Port <= 0 || string.IsNullOrWhiteSpace(registration.PackageId))
            return null;

        return new Execution.AppActionFlowLifecycleResetOwner(
            registration.Port,
            registration.PackageId,
            registration.DeviceId ?? registration.Platform,
            $"{registration.AppName}:{registration.Tfm}:{registration.Version}",
            preclaimedMutationLeaseId);
    }

    private async Task<Microsoft.Maui.DevFlow.Testing.MauiTestAgentTargetState?> GetLiveTestAgentTargetStateAsync(
        Microsoft.Maui.DevFlow.Testing.MauiTestAgentTarget? target,
        Microsoft.Maui.DevFlow.Testing.MauiTestAgentTargetState? supplied = null)
    {
        if (target is null ||
            string.IsNullOrWhiteSpace(target.AgentId) ||
            string.IsNullOrWhiteSpace(target.AgentInstanceId) ||
            !_agents.TryGetValue(target.AgentId, out var connection) ||
            !string.Equals(connection.Registration.InstanceId, target.AgentInstanceId, StringComparison.Ordinal))
        {
            return null;
        }

        return await ReadLiveTestAgentTargetStateAsync(connection.Registration, supplied).ConfigureAwait(false);
    }

    private async Task<Microsoft.Maui.DevFlow.Testing.MauiTestAgentTargetState?> GetLiveTestAgentTargetStateAsync(
        Microsoft.Maui.DevFlow.Testing.MauiTestAgentTargetState? supplied)
    {
        if (supplied is null ||
            string.IsNullOrWhiteSpace(supplied.AgentId) ||
            string.IsNullOrWhiteSpace(supplied.AgentInstanceId) ||
            !_agents.TryGetValue(supplied.AgentId, out var connection) ||
            !string.Equals(connection.Registration.InstanceId, supplied.AgentInstanceId, StringComparison.Ordinal))
        {
            return null;
        }

        return await ReadLiveTestAgentTargetStateAsync(connection.Registration, supplied).ConfigureAwait(false);
    }

    private static async Task<Microsoft.Maui.DevFlow.Testing.MauiTestAgentTargetState?> ReadLiveTestAgentTargetStateAsync(
        AgentRegistration registration,
        Microsoft.Maui.DevFlow.Testing.MauiTestAgentTargetState? supplied)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient("localhost", registration.Port)
            {
                AutoAcquireMutationLease = false,
            };
            var status = await client.GetStatusAsync().ConfigureAwait(false);
            if (status is null)
                return null;

            return new Microsoft.Maui.DevFlow.Testing.MauiTestAgentTargetState
            {
                AgentId = registration.Id,
                AgentInstanceId = registration.InstanceId,
                AppBuildFingerprint = BuildTestAgentAppFingerprint(status),
                // These values remain unavailable until the running agent or a trusted reset host
                // attests them. Never turn caller-echoed values into broker-observed facts.
                SeedFingerprint = null,
                BackendStateFingerprint = null,
                Route = status.Route,
                Window = status.Window,
                ObservedAt = DateTimeOffset.UtcNow,
            };
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return null;
        }
    }

    private static string? BuildTestAgentAppFingerprint(Microsoft.Maui.DevFlow.Driver.AgentStatus status)
    {
        static string? Normalize(string? value)
        {
            var normalized = value?.Trim();
            return string.IsNullOrWhiteSpace(normalized) ||
                   string.Equals(normalized, "unknown", StringComparison.OrdinalIgnoreCase)
                ? null
                : normalized;
        }

        var version = Normalize(status.App?.Version);
        var build = Normalize(status.App?.Build);
        return version is null && build is null
            ? null
            : $"{version ?? "unknown"}:{build ?? "unknown"}";
    }

    private static bool RequiresWorkflowRunCapabilities(MauiTestPlan? plan)
        => plan?.Requirements?.RequiredCapabilities.Count > 0 ||
           plan?.Requirements?.RequiredSemantics.Count > 0;

    private static async Task<MauiFlowCapabilitySet?> ReadWorkflowRunCapabilitiesAsync(AgentRegistration registration)
    {
        try
        {
            using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient("localhost", registration.Port)
            {
                AutoAcquireMutationLease = false,
            };
            var status = await client.GetStatusAsync().ConfigureAwait(false);
            return WorkflowRunCoordinator.BuildAvailableCapabilities(status);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return null;
        }
    }

    private static void CanonicalizeTestAgentTarget(
        Microsoft.Maui.DevFlow.Testing.MauiTestAgentTarget? target,
        Microsoft.Maui.DevFlow.Testing.MauiTestAgentTargetState targetState)
    {
        if (target is null)
            return;
        target.AppBuildFingerprint = targetState.AppBuildFingerprint;
        target.SeedFingerprint = targetState.SeedFingerprint;
        target.BackendStateFingerprint = targetState.BackendStateFingerprint;
    }

    private static int TestAgentStatusCode(Microsoft.Maui.DevFlow.Testing.MauiTestAgentError? error)
    {
        if (error is null)
            return 200;
        return error.Category switch
        {
            Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCategories.Authorization => 403,
            Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCategories.Target or
            Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCategories.State or
            Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCategories.Conflict => 409,
            Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCategories.Capability => 429,
            Microsoft.Maui.DevFlow.Testing.MauiTestAgentErrorCategories.Unsupported => 400,
            _ => 400,
        };
    }

    private static Microsoft.Maui.DevFlow.Testing.MauiTestAgentError TestAgentRouteError(
        string code,
        string category,
        string message,
        bool retryable)
        => new()
        {
            Code = code,
            Category = category,
            Message = message,
            Retryable = retryable,
        };

    private static bool FixedTimeEquals(string expected, string? supplied)
        => !string.IsNullOrEmpty(supplied) &&
           CryptographicOperations.FixedTimeEquals(
               Encoding.UTF8.GetBytes(expected),
               Encoding.UTF8.GetBytes(supplied));

    private async Task HandleWorkflowRunRoute(HttpListenerContext context, string method, string path)
    {
        const int maxBodyChars = 1_048_576;
        var normalizedPath = path.TrimEnd('/');

        if (string.Equals(normalizedPath, "/api/workflow-runs/capabilities", StringComparison.OrdinalIgnoreCase))
        {
            if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    405,
                    WorkflowRunStatusResponse.Failure("Method not allowed."));
                return;
            }

            await WriteTypedJsonResponseAsync(context, 200, _workflowRuns.GetCapabilities());
            return;
        }

        if (string.Equals(normalizedPath, "/api/workflow-runs/start", StringComparison.OrdinalIgnoreCase))
        {
            if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    405,
                    WorkflowRunStatusResponse.Failure("Method not allowed."));
                return;
            }

            var request = await ReadWorkflowRunBodyAsync<WorkflowRunStartRequest>(context, maxBodyChars);
            if (request is null)
                return;

            if (string.IsNullOrWhiteSpace(request.AgentId) || string.IsNullOrWhiteSpace(request.AgentInstanceId))
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    400,
                    WorkflowRunStartResult.Rejected(400, "agentId and agentInstanceId are required.", null, null));
                return;
            }

            var routeGate = AgentRouteGate(request.AgentId);
            await routeGate.WaitAsync(_cts?.Token ?? CancellationToken.None);
            try
            {
                if (!_agents.TryGetValue(request.AgentId, out var connection) ||
                    !string.Equals(
                        connection.Registration.InstanceId,
                        request.AgentInstanceId,
                        StringComparison.Ordinal))
                {
                    await WriteTypedJsonResponseAsync(
                        context,
                        409,
                        WorkflowRunStartResult.Conflict(
                            "The requested agent instance is stale or no longer connected."));
                    return;
                }

                // A cheap, non-consuming filter so an ungranted caller cannot make the broker do
                // outbound work on its behalf. It is not the authorization boundary — the coordinator
                // still takes the single-use decision, so a surface that skips this is still refused.
                if (_requireWorkflowRunAuthorization &&
                    !_testAgentSessions.CanDispatchRunAuthorization(
                        request.AuthorizationId,
                        request.AgentId,
                        request.AgentInstanceId,
                        WorkflowRunCoordinator.DescribeDispatchSteps(request.Flow),
                        WorkflowRunCoordinator.DescribeDispatchFlowDigest(request.Flow),
                        out var runAuthorizationError))
                {
                    await WriteTypedJsonResponseAsync(
                        context,
                        403,
                        WorkflowRunStartResult.Rejected(403, runAuthorizationError!, null, null));
                    return;
                }

                if (RequiresWorkflowRunCapabilities(request.Plan) &&
                    request.AvailableCapabilities is null)
                {
                    request.AvailableCapabilities = await ReadWorkflowRunCapabilitiesAsync(connection.Registration)
                        .ConfigureAwait(false);
                }

                // Admission reads attested reset evidence; nothing else in the run path produces
                // it, so the reset happens here, before admission and before any device work.
                using var resetLease = await EstablishPreRunResetAsync(connection, request).ConfigureAwait(false);

                var target = CreateWorkflowRunTarget(connection.Registration);
                var result = _workflowRuns.Start(
                    request,
                    target,
                    () => IsCurrentAgentConnection(connection),
                    new WorkflowRunExecutionOptions
                    {
                        ReproductionExpectation = request.ReproductionExpectation,
                    },
                    leaseHandoff: resetLease?.Handoff);
                await WriteTypedJsonResponseAsync(context, result.StatusCode, result);
            }
            finally
            {
                routeGate.Release();
            }
            return;
        }

        var segments = normalizedPath.Trim('/').Split('/');
        if (segments.Length != 4 ||
            !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) ||
            !segments[1].Equals("workflow-runs", StringComparison.OrdinalIgnoreCase))
        {
            await WriteTypedJsonResponseAsync(context, 404, WorkflowRunStatusResponse.Failure("Not found."));
            return;
        }

        var runId = Uri.UnescapeDataString(segments[2]);
        var action = segments[3];
        if (string.Equals(action, "status", StringComparison.OrdinalIgnoreCase))
        {
            if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    405,
                    WorkflowRunStatusResponse.Failure("Method not allowed."));
                return;
            }

            var request = await ReadWorkflowRunBodyAsync<WorkflowRunAccessRequest>(context, maxBodyChars);
            if (request is null)
                return;

            var result = _workflowRuns.GetStatus(runId, request.CapabilityToken);
            await WriteTypedJsonResponseAsync(
                context,
                result.StatusCode,
                result.Run is null
                    ? WorkflowRunStatusResponse.Failure(result.Error ?? "Workflow run was not found.")
                    : WorkflowRunStatusResponse.Success(result.Run));
            return;
        }

        if (string.Equals(action, "cancel", StringComparison.OrdinalIgnoreCase))
        {
            if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    405,
                    WorkflowRunStatusResponse.Failure("Method not allowed."));
                return;
            }

            var request = await ReadWorkflowRunBodyAsync<WorkflowRunAccessRequest>(context, maxBodyChars);
            if (request is null)
                return;

            var result = _workflowRuns.Cancel(runId, request.CapabilityToken);
            await WriteTypedJsonResponseAsync(context, result.StatusCode, result);
            return;
        }

        await WriteTypedJsonResponseAsync(context, 404, WorkflowRunStatusResponse.Failure("Not found."));
    }

    private async Task<byte[]?> ReadArtifactTrustBytesAsync(HttpListenerContext context, int maximumBytes)
    {
        if (context.Request.ContentLength64 > maximumBytes)
        {
            await WriteTypedJsonResponseAsync(
                context,
                413,
                ArtifactTrustRouteResponse.Failure("Request body too large."));
            return null;
        }

        try
        {
            using var output = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
            var buffer = new byte[16 * 1024];
            var total = 0;
            while (true)
            {
                var read = await context.Request.InputStream.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    _cts?.Token ?? CancellationToken.None);
                if (read == 0)
                    break;

                total += read;
                if (total > maximumBytes)
                {
                    await WriteTypedJsonResponseAsync(
                        context,
                        413,
                        ArtifactTrustRouteResponse.Failure("Request body too large."));
                    return null;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), _cts?.Token ?? CancellationToken.None);
            }

            return output.ToArray();
        }
        catch (OperationCanceledException)
        {
            await WriteTypedJsonResponseAsync(
                context,
                408,
                ArtifactTrustRouteResponse.Failure("Artifact import was cancelled."));
            return null;
        }
        catch (IOException)
        {
            await WriteTypedJsonResponseAsync(
                context,
                400,
                ArtifactTrustRouteResponse.Failure("The artifact request could not be read."));
            return null;
        }
    }

    private async Task<T?> ReadArtifactTrustJsonBodyAsync<T>(HttpListenerContext context, int maximumChars)
        where T : class
    {
        if (context.Request.ContentLength64 > maximumChars)
        {
            await WriteTypedJsonResponseAsync(
                context,
                413,
                ArtifactTrustRouteResponse.Failure("Request body too large."));
            return null;
        }

        try
        {
            var text = await ReadBoundedBodyAsync(
                context.Request.InputStream,
                context.Request.ContentEncoding ?? Encoding.UTF8,
                maximumChars,
                _cts?.Token ?? CancellationToken.None);
            if (string.IsNullOrWhiteSpace(text))
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    400,
                    ArtifactTrustRouteResponse.Failure("A JSON request body is required."));
                return null;
            }

            var value = JsonSerializer.Deserialize<T>(text, WorkflowRunJsonOptions);
            if (value is null)
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    400,
                    ArtifactTrustRouteResponse.Failure("Invalid JSON request body."));
            }
            return value;
        }
        catch (RequestBodyTooLargeException)
        {
            await WriteTypedJsonResponseAsync(
                context,
                413,
                ArtifactTrustRouteResponse.Failure("Request body too large."));
            return null;
        }
        catch (JsonException)
        {
            await WriteTypedJsonResponseAsync(
                context,
                400,
                ArtifactTrustRouteResponse.Failure("Invalid JSON request body."));
            return null;
        }
    }

    private async Task<T?> ReadWorkflowRunBodyAsync<T>(HttpListenerContext context, int maxChars)
        where T : class
    {
        if (context.Request.ContentLength64 > maxChars)
        {
            await WriteTypedJsonResponseAsync(
                context,
                413,
                WorkflowRunStatusResponse.Failure("Request body too large."));
            return null;
        }

        try
        {
            var text = await ReadBoundedBodyAsync(
                context.Request.InputStream,
                context.Request.ContentEncoding ?? Encoding.UTF8,
                maxChars,
                _cts?.Token ?? CancellationToken.None);
            if (string.IsNullOrWhiteSpace(text))
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    400,
                    WorkflowRunStatusResponse.Failure("A JSON request body is required."));
                return null;
            }

            var value = JsonSerializer.Deserialize<T>(text, WorkflowRunJsonOptions);
            if (value is null)
            {
                await WriteTypedJsonResponseAsync(
                    context,
                    400,
                    WorkflowRunStatusResponse.Failure("Invalid JSON request body."));
            }
            return value;
        }
        catch (RequestBodyTooLargeException)
        {
            await WriteTypedJsonResponseAsync(
                context,
                413,
                WorkflowRunStatusResponse.Failure("Request body too large."));
            return null;
        }
        catch (JsonException)
        {
            await WriteTypedJsonResponseAsync(
                context,
                400,
                WorkflowRunStatusResponse.Failure("Invalid JSON request body."));
            return null;
        }
    }

    private static readonly JsonSerializerOptions WorkflowRunJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static async Task WriteTypedJsonResponseAsync<T>(
        HttpListenerContext context,
        int statusCode,
        T body)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, WorkflowRunJsonOptions);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private static WorkflowRunTarget CreateWorkflowRunTarget(AgentRegistration registration)
        => new(
            registration.Id,
            registration.InstanceId,
            registration.Port,
            registration.Platform,
            registration.AppName,
            LeaseKeyForRegistration(registration));

    internal static string LeaseKeyForRegistration(AgentRegistration registration) => registration.Id;

    /// <summary>
    /// The broker's one authorization boundary for starting a device-mutating workflow run. The
    /// coordinator refuses to start without a decision from here, so this stays true no matter how
    /// many surfaces dispatch runs: MCP, the Inspector workbench, the replay bridge, or a future one.
    /// </summary>
    internal WorkflowRunDispatchDecision AuthorizeWorkflowRunDispatch(WorkflowRunDispatch dispatch)
    {
        var decision = DecideWorkflowRunDispatch(dispatch);
        if (!decision.Allowed)
        {
            // A refusal has no run record to journal against, so the broker log is the only place a
            // repeated forged or unrecognized dispatch attempt can be noticed at all.
            Log($"Refused a workflow run dispatch from '{dispatch.Origin}' for " +
                $"{dispatch.AgentId}/{dispatch.AgentInstanceId}: {decision.Error}");
        }

        return decision;
    }

    internal WorkflowRunDispatchDecision DecideWorkflowRunDispatch(WorkflowRunDispatch dispatch)
    {
        switch (dispatch.Origin)
        {
            // The Inspector is a human at a local, read-token-gated UI rather than an agent acting
            // for an absent human, so it has no MCP grant to present and deliberately proves
            // something else: the broker itself created this adapter for this exact agent instance
            // (a ticket no browser or third route can compute), and it currently holds the app's
            // single-writer mutation lease, which the coordinator then transfers atomically.
            case WorkflowRunDispatchOrigin.InspectorWorkbench:
            case WorkflowRunDispatchOrigin.InspectorReplayBridge:
                if (!IsBrokerIssuedWorkflowRunDispatchTicket(dispatch))
                    return WorkflowRunDispatchDecision.Deny(UnticketedDispatchError);
                return dispatch.LeaseHandoff is null
                    ? WorkflowRunDispatchDecision.Deny(
                        "The Inspector must already hold this app's mutation lease to start a workflow run.")
                    : WorkflowRunDispatchDecision.Allow(
                        dispatch.Origin == WorkflowRunDispatchOrigin.InspectorWorkbench
                            ? "inspector-workbench-lease"
                            : "inspector-replay-bridge-lease");

            // Repair validation replays a reviewer-approved proposal from inside the broker, and the
            // reviewer's repair grant — not an ordinary run authorization — is what permits it.
            case WorkflowRunDispatchOrigin.RepairValidation:
                if (dispatch.HasDeviceExtensions)
                {
                    return WorkflowRunDispatchDecision.Deny(
                        "Repair validation cannot execute devicePreconditions or deviceSteps because the repair grant does not authorize those mutations.");
                }
                return IsBrokerIssuedWorkflowRunDispatchTicket(dispatch)
                    ? WorkflowRunDispatchDecision.Allow("broker-repair-validation")
                    : WorkflowRunDispatchDecision.Deny(UnticketedDispatchError);

            case WorkflowRunDispatchOrigin.TestAgentGrant:
                if (!_requireWorkflowRunAuthorization)
                    return WorkflowRunDispatchDecision.Allow("test-only-authorization-disabled");
                return _testAgentSessions.TryConsumeRunDispatchAuthorization(
                        dispatch.AuthorizationId,
                        dispatch.AgentId,
                        dispatch.AgentInstanceId,
                        dispatch.Steps,
                        dispatch.FlowDigest,
                        out var error)
                    ? WorkflowRunDispatchDecision.Allow("test-agent-human-grant")
                    : WorkflowRunDispatchDecision.Deny(error!);

            default:
                return WorkflowRunDispatchDecision.Deny(
                    "The workflow run dispatch origin is not one this broker authorizes.");
        }
    }

    private const string UnticketedDispatchError =
        "A broker-issued dispatch ticket for this exact agent instance is required to start a workflow run.";

    /// <summary>
    /// Mints the proof a broker-owned dispatch surface presents back to the coordinator. It is
    /// derived from a per-broker key so nothing outside this process can forge one, and it is bound
    /// to the exact agent instance and origin it was issued for, so it can be replayed neither
    /// against another app nor against a different origin's weaker rule. The value is an in-process
    /// capability with no expiry: never return it to a client, log it, or write it to disk.
    /// </summary>
    private string IssueWorkflowRunDispatchTicket(
        AgentRegistration registration,
        WorkflowRunDispatchOrigin origin)
        => ComputeWorkflowRunDispatchTicket(registration.Id, registration.InstanceId, origin);

    internal string ComputeWorkflowRunDispatchTicket(
        string agentId,
        string agentInstanceId,
        WorkflowRunDispatchOrigin origin)
        => Convert.ToHexString(HMACSHA256.HashData(
            _workflowRunDispatchKey,
            Encoding.UTF8.GetBytes($"workflow-run-dispatch\n{origin}\n{agentId}\n{agentInstanceId}")));

    private bool IsBrokerIssuedWorkflowRunDispatchTicket(WorkflowRunDispatch dispatch)
        => !string.IsNullOrEmpty(dispatch.DispatchTicket) &&
           CryptographicOperations.FixedTimeEquals(
               Encoding.UTF8.GetBytes(dispatch.DispatchTicket),
               Encoding.UTF8.GetBytes(ComputeWorkflowRunDispatchTicket(
                   dispatch.AgentId,
                   dispatch.AgentInstanceId,
                   dispatch.Origin)));

    private bool IsCurrentAgentConnection(AgentConnection expected)
        => _agents.TryGetValue(expected.Registration.Id, out var current) &&
           ReferenceEquals(current, expected) &&
           string.Equals(
               current.Registration.InstanceId,
               expected.Registration.InstanceId,
               StringComparison.Ordinal);

    private async Task<FlowReplayReport> ExecuteWorkflowRunAsync(
        WorkflowRunExecution execution,
        CancellationToken cancellationToken)
    {
        using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient("localhost", execution.Target.AgentPort)
        {
            AutoAcquireMutationLease = false,
            RetryMutatingRequests = false,
            TransientFailureRetryCount = 0,
            MutationLeaseHolderKind = "workflow-run",
            MutationLeaseLabel = execution.RunId
        };
        using var leaseScope = client.UseMutationLease(
            execution.LeaseId,
            "workflow-run",
            execution.RunId);
        using var workflowScope = client.UseWorkflowRun(new Microsoft.Maui.DevFlow.Driver.WorkflowRunContext
        {
            RunId = execution.RunId,
            AgentInstanceId = execution.Target.AgentInstanceId,
            AuthorityEpoch = execution.AuthorityEpoch
        });
        Microsoft.Maui.DevFlow.Driver.AgentStatus? liveStatus = null;
        try
        {
            // Target facts are observed before replay and are report metadata only. They do not
            // grant reset or source authority, and unavailable facts remain absent rather than
            // being inferred from a package name or host path.
            liveStatus = await client.GetStatusAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            // The canonical runner will report the actual transport failure. Keep target facts
            // capability-honest when the app cannot answer this optional read.
        }
        var evidenceCapture = execution.Options.EvidenceCaptureFactory?.Invoke(client);
        AgentRegistration? registration = null;
        if (_agents.TryGetValue(execution.Target.AgentId, out var currentConnection) &&
            string.Equals(
                currentConnection.Registration.InstanceId,
                execution.Target.AgentInstanceId,
                StringComparison.Ordinal))
        {
            registration = currentConnection.Registration;
        }

        var runner = new MauiFlowRunner(
            client,
            new MauiFlowRunnerOptions
            {
                RunId = execution.RunId,
                Target = new MauiFlowRunTarget
                {
                    TargetId = execution.Target.AgentId,
                    AgentId = execution.Target.AgentId,
                    AgentInstanceId = execution.Target.AgentInstanceId,
                    Platform = liveStatus?.Device?.Platform ?? execution.Target.Platform,
                    AppId = liveStatus?.App?.PackageId ?? execution.Target.AppName,
                    AppBuildFingerprint = liveStatus?.App?.Build,
                    DeviceProfile = liveStatus?.Device is null ||
                        string.IsNullOrWhiteSpace(liveStatus.Device.DeviceType) &&
                        string.IsNullOrWhiteSpace(liveStatus.Device.Idiom)
                        ? null
                        : string.Join(
                            "|",
                            liveStatus.Device.DeviceType ?? string.Empty,
                            liveStatus.Device.Idiom ?? string.Empty),
                },
                Plan = execution.SafetyRequest.Plan,
                RunContext = execution.SafetyRequest.Context,
                ThrowOnCancellation = false,
                Progress = execution.Options.Progress,
                StepObservationDelayMs = 900,
                ExecutionExtension = null,
            },
            evidenceCapture);
        return (await runner.RunWithLegacyAsync(execution.Flow, file: null, cancellationToken)
            .ConfigureAwait(false)).LegacyReport;
    }

    /// <summary>
    /// Opens an independent business-oracle evaluation for a run against an app the broker attached
    /// to rather than installed.
    /// </summary>
    /// <remarks>
    /// The evaluator reaches the device out of band, through the same transport the CLI uses, so
    /// the evidence is produced outside the agent channel the flow itself drove. Returning null
    /// leaves the run unverified and therefore repair-ineligible, which is the correct outcome
    /// whenever the plan declares no evaluable oracle or the device cannot be reached.
    /// </remarks>
    private async Task<IWorkflowRunOracleSession?> BeginWorkflowRunOracleSessionAsync(
        RunOracleSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (_attachedRunOracles is null)
            return null;

        var agent = _agents.Values
            .Select(static connection => connection.Registration)
            .FirstOrDefault(registration =>
                string.Equals(registration.Id, request.Target.AgentId, StringComparison.Ordinal) &&
                string.Equals(registration.InstanceId, request.Target.AgentInstanceId, StringComparison.Ordinal));
        if (agent?.PackageId is not { Length: > 0 } packageId)
            return null;
        if (!_attachedRunOracles.SupportsAttachedRun(request.Plan, agent.Platform))
            return null;

        var target = new Execution.AttachedRunOracleTarget
        {
            Plan = request.Plan,
            Platform = agent.Platform ?? string.Empty,
            PackageId = packageId,
            DeviceIdentity = agent.DeviceId,
            Deadline = DateTimeOffset.UtcNow.AddSeconds(30),
        };
        var baseline = await _attachedRunOracles
            .ObserveAttachedBaselineAsync(target, cancellationToken)
            .ConfigureAwait(false);
        if (!baseline.Observed)
        {
            Log($"Workflow run oracle baseline unavailable: {baseline.UnavailableCode}");
            return null;
        }

        return new AttachedRunOracleSession(_attachedRunOracles, target, baseline);
    }

    /// <summary>Evaluates one run's declared oracles against the baseline taken before it ran.</summary>
    private sealed class AttachedRunOracleSession(
        Execution.IAttachedRunOracleEvaluator evaluator,
        Execution.AttachedRunOracleTarget target,
        Execution.AttachedRunOracleBaseline baseline) : IWorkflowRunOracleSession
    {
        public Task<IReadOnlyList<Microsoft.Maui.DevFlow.Testing.MauiIndependentBusinessOracleResult>> EvaluateAsync(
            CancellationToken cancellationToken)
            => evaluator.EvaluateAttachedAsync(
                target with { Deadline = DateTimeOffset.UtcNow.AddSeconds(30) },
                baseline,
                cancellationToken);
    }

    private async Task<WorkflowRunLedgerControlResult> ControlWorkflowRunLedgerAsync(        WorkflowRunLedgerControl control,
        CancellationToken cancellationToken)
    {
        using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient("localhost", control.Target.AgentPort)
        {
            AutoAcquireMutationLease = false,
            RetryMutatingRequests = false,
            TransientFailureRetryCount = 0,
            MutationLeaseHolderKind = "workflow-run",
            MutationLeaseLabel = control.RunId
        };
        using var leaseScope = client.UseMutationLease(
            control.LeaseId,
            "workflow-run",
            control.RunId);
        var result = await client.ControlWorkflowRunAsync(
            control.Action,
            new Microsoft.Maui.DevFlow.Driver.WorkflowRunContext
            {
                RunId = control.RunId,
                AgentInstanceId = control.Target.AgentInstanceId,
                AuthorityEpoch = control.AuthorityEpoch,
                ApprovalDigest = control.ApprovalDigest
            },
            control.Reason).WaitAsync(cancellationToken).ConfigureAwait(false);

        if (result.Ok && !string.Equals(result.State, "unknown-completion", StringComparison.Ordinal))
            return WorkflowRunLedgerControlResult.Success();

        return WorkflowRunLedgerControlResult.Failure(
            result.Reason ?? (result.State == "unknown-completion" ? "workflow-unknown-completion" : null),
            result.Error ?? (result.State == "unknown-completion"
                ? "The agent reported a workflow command with unknown completion."
                : null));
    }

    internal static async Task<string> ReadBoundedBodyAsync(
        Stream stream,
        Encoding encoding,
        int maxChars,
        CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(
            stream,
            encoding,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true);
        var buffer = new char[Math.Min(4096, maxChars + 1)];
        var builder = new StringBuilder(Math.Min(maxChars, 4096));
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) return builder.ToString();
            if (builder.Length + read > maxChars) throw new RequestBodyTooLargeException();
            builder.Append(buffer, 0, read);
        }
    }

    private sealed class RequestBodyTooLargeException : Exception;

    /// <summary>
    /// Retracts this broker's registration, and only this broker's: the state file is shared
    /// machine-wide, so deleting an entry we do not own would destroy another broker's native-host
    /// approval token. See <see cref="ShouldPublishBrokerState"/>.
    /// </summary>
    private void DeleteBrokerState()
    {
        try
        {
            if (!File.Exists(BrokerPaths.StateFile))
                return;

            var existing = CliJson.Deserialize<BrokerState>(File.ReadAllText(BrokerPaths.StateFile));
            if (!MayDeleteBrokerState(existing, _port, Environment.ProcessId))
                return;

            File.Delete(BrokerPaths.StateFile);
        }
        catch { }
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {message}";
        try { _log?.Invoke(line); } catch { }

        try
        {
            var logFile = BrokerPaths.LogFile;
            Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);

            // Truncate if > 1MB
            if (File.Exists(logFile) && new FileInfo(logFile).Length > 1_000_000)
                File.WriteAllText(logFile, "");

            File.AppendAllText(logFile, line + Environment.NewLine);
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _idleTimer?.Dispose();
        try { _listener?.Close(); } catch { }
        foreach (var inspector in _inspectors.Values)
        {
            try { inspector.Dispose(); } catch { }
        }
        _inspectors.Clear();
        _mutationLeases.Clear();
        _flows.Clear();
        _workflowRuns.Dispose();
        _cts?.Dispose();
    }
    private record AgentConnection(AgentRegistration Registration, WebSocket WebSocket);

    // ── Inspector integration ──

    private readonly ConcurrentDictionary<string, InspectorServer> _inspectors = new();

    // Unguessable per-broker token that lets local host shells (canvas, VS Code) embed the inspector
    // in an iframe. Written to broker.json (local-only) and honored by the inspector via ?embed=.
    private readonly string _embedToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    private async Task HandleInspectorRoute(HttpListenerContext context, string path)
    {
        // Routes:
        //   /inspector          → list agents with inspector links
        //   /inspector/{id}     → 301 redirect to /inspector/{id}/ so that
        //                         relative asset URLs in inspector.html
        //                         (devflow.css, devflow.js) resolve against
        //                         the per-agent base rather than the broker root.
        //   /inspector/{id}/    → serve inspector HTML for that agent
        //   /inspector/{id}/... → proxy sub-routes to the per-agent InspectorServer

        var segments = path.TrimStart('/').Split('/', 3);

        if (segments.Length == 1 || (segments.Length == 2 && string.IsNullOrEmpty(segments[1])))
        {
            // List agents with inspector links
            await ServeAgentListPage(context);
            return;
        }

        // /inspector/{id} (no trailing slash, no sub-path) → redirect to
        // /inspector/{id}/ so that <link href="devflow.css"> on the inspector
        // page resolves to /inspector/{id}/devflow.css instead of
        // /inspector/devflow.css (which the broker would otherwise try to
        // route as agent id "devflow.css"). Without the trailing slash, the
        // browser treats {id} as a filename and resolves relatives against
        // /inspector/ instead of /inspector/{id}/.
        if (segments.Length == 2 && !string.IsNullOrEmpty(segments[1]) && !path.EndsWith('/'))
        {
            // Preserve the query string on the redirect and URL-encode the
            // agent id so the Location header is a valid URI-reference even
            // when the agent id contains characters that need percent-encoding
            // (spaces, unicode, etc.).
            var query = context.Request.Url?.Query ?? string.Empty;
            context.Response.StatusCode = 301;
            context.Response.RedirectLocation = $"/inspector/{Uri.EscapeDataString(segments[1])}/{query}";
            context.Response.Close();
            return;
        }

        var agentId = segments[1];

        var subPath = segments.Length > 2 ? "/" + segments[2] : "/";

        // Inspector URLs are agent-scoped. Only the explicit "default" convenience route may use
        // the sole connected agent; a stale or mistyped real ID must never drive another app.
        var connection = ResolveInspectorAgent(_agents, agentId);

        if (connection == null)
        {
            context.Response.StatusCode = 404;
            context.Response.ContentType = "text/plain";
            var msg = Encoding.UTF8.GetBytes($"Agent '{agentId}' not found. Connected agents: {_agents.Count}");
            await context.Response.OutputStream.WriteAsync(msg);
            context.Response.Close();
            return;
        }

        // Ordinary HTTP requests hold the generation gate through dispatch, so a same-ID reconnect
        // cannot replace the agent after validation but before a mutation reaches it. Event sockets
        // are long-lived and read-only; replacement disposes their InspectorServer instead.
        var routeGate = context.Request.IsWebSocketRequest ? null : AgentRouteGate(connection.Registration.Id);
        if (routeGate is not null)
            await routeGate.WaitAsync(_cts?.Token ?? CancellationToken.None);
        try
        {

        // Get or create inspector server for this agent.
        // Three lifecycle hazards we have to defend against:
        //   1. Race with disconnect — the agent may disconnect between our
        //      TryGetValue above and creating the inspector. MonitorAgentConnection
        //      runs its own _inspectors.TryRemove() but only sees inspectors that
        //      already exist; one created after its cleanup would be orphaned.
        //   2. Stale port on reconnect — if the agent restarts on a different port,
        //      the cached inspector still holds an AgentClient pointing at the old
        //      (dead) port. Replace it.
        //   3. GetOrAdd factory race — the ConcurrentDictionary factory overload
        //      may invoke the factory concurrently and silently discard the loser,
        //      leaking its AgentClient (HttpClient) and CTS. Construct first, then
        //      GetOrAdd(value), and dispose the loser.
        var agentPort = connection.Registration.Port;
        if (_inspectors.TryGetValue(connection.Registration.Id, out var inspector))
        {
            // Stale-port detection: the agent reconnected on a different port.
            if (inspector.AgentPort != agentPort ||
                !string.Equals(inspector.AgentInstanceId, connection.Registration.InstanceId, StringComparison.Ordinal))
            {
                if (_inspectors.TryRemove(new KeyValuePair<string, InspectorServer>(connection.Registration.Id, inspector)))
                {
                    try { inspector.Dispose(); } catch { }
                }
                inspector = null;
            }
        }

        if (inspector == null)
        {
            var registration = connection.Registration;
            var created = new InspectorServer(
                0,
                "localhost",
                agentPort,
                _embedToken,
                registration.Id,
                registration.AppName,
                registration.Platform,
                registration.Project,
                registration.SessionId,
                checkpoints: _checkpoints,
                checkpointRegistration: registration,
                workflowReplay: (flow, evidenceFactory, leaseHandoff, cancellationToken) =>
                    ReplayInspectorWorkflowAsync(connection, flow, evidenceFactory, leaseHandoff, cancellationToken),
                agentInstanceId: registration.InstanceId,
                workflowServices: new InspectorWorkflowServices(
                    _workflowRuns,
                    _artifactTrustImports,
                    _artifactTrustStore,
                    CreateWorkflowRunTarget(registration),
                    IssueWorkflowRunDispatchTicket(
                        registration,
                        WorkflowRunDispatchOrigin.InspectorWorkbench),
                    () => IsCurrentAgentConnection(connection),
                    _cts?.Token ?? CancellationToken.None),
                testAgentSessions: _testAgentSessions,
                testAgentTargetStateRefresh: supplied => GetLiveTestAgentTargetStateAsync(supplied),
                previewFlags: _previewFlags,
                trustedHostApprovalVerifier: _trustedHostApprovalVerifier);
            inspector = _inspectors.GetOrAdd(connection.Registration.Id, created);
            if (!ReferenceEquals(inspector, created))
            {
                created.Dispose();
            }
            else
            {
                Log($"Inspector created for agent: {connection.Registration.AppName} (port {agentPort})");
            }

            // Disconnect-race fix: MonitorAgentConnection may have already removed
            // this agent ID from _agents (and tried to remove the not-yet-existing
            // inspector). If so, our newly-stored inspector would leak — clean up
            // and return 503 instead of routing into a dead AgentClient.
            if (!_agents.TryGetValue(connection.Registration.Id, out var currentConnection) ||
                !ReferenceEquals(currentConnection, connection))
            {
                if (_inspectors.TryRemove(new KeyValuePair<string, InspectorServer>(connection.Registration.Id, inspector)))
                {
                    try { inspector.Dispose(); } catch { }
                }
                context.Response.StatusCode = 503;
                context.Response.ContentType = "text/plain";
                var msg = Encoding.UTF8.GetBytes("Agent disconnected");
                await context.Response.OutputStream.WriteAsync(msg);
                context.Response.Close();
                return;
            }
        }

        if (!_agents.TryGetValue(connection.Registration.Id, out var routeConnection) ||
            !ReferenceEquals(routeConnection, connection))
        {
            context.Response.StatusCode = 503;
            context.Response.ContentType = "text/plain";
            var msg = Encoding.UTF8.GetBytes("Agent reconnected; reload Inspector state");
            await context.Response.OutputStream.WriteAsync(msg);
            context.Response.Close();
            return;
        }

        // Proxy the request through the inspector's route handler
        await inspector.HandleBrokerRequestAsync(context, subPath);
        }
        finally
        {
            routeGate?.Release();
        }
    }

    internal static TConnection? ResolveInspectorAgent<TConnection>(
        IReadOnlyDictionary<string, TConnection> agents,
        string agentId)
        where TConnection : class
    {
        if (agents.TryGetValue(agentId, out var connection))
            return connection;
        if (!string.Equals(agentId, "default", StringComparison.OrdinalIgnoreCase))
            return null;

        var candidates = agents.Values.Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static MauiTestPlan? RebindPlanToTransientFlow(MauiTestPlan? plan, MauiFlow flow)
    {
        if (plan?.Flow is null)
            return plan;

        var digest = MauiFlowRunReportSerializer.ComputeFlowDigest(flow);
        if (string.Equals(plan.Flow.Digest, digest, StringComparison.Ordinal))
            return plan;

        var node = System.Text.Json.JsonSerializer
            .SerializeToNode(plan, MauiTestingJsonContext.Default.MauiTestPlan)?
            .AsObject();
        if (node?["flow"]?.AsObject() is not { } flowNode)
            return plan;

        flowNode["digest"] = digest;
        return node.Deserialize(MauiTestingJsonContext.Default.MauiTestPlan) ?? plan;
    }

    /// <summary>Normalizes an observed status value the same way the Inspector classifies one.</summary>
    private static string? SafeCheckpointText(string? value, int maximum = 256)
        => InspectorServer.SafeInspectorText(value, maximum);

    private async Task<FlowReplayReport> ReplayInspectorWorkflowAsync(
        AgentConnection connection,
        MauiFlow flow,
        Func<Microsoft.Maui.DevFlow.Driver.AgentClient, IFlowReplayEvidenceCapture?> evidenceCaptureFactory,
        WorkflowRunLeaseHandoff leaseHandoff,
        CancellationToken _)
    {
        if (!IsCurrentAgentConnection(connection))
        {
            throw new WorkflowRunRejectedException(
                409,
                "The Inspector target was replaced by a newer agent instance. Reload the Inspector.");
        }

        var registration = connection.Registration;
        var started = _workflowRuns.Start(
            new WorkflowRunStartRequest
            {
                AgentId = registration.Id,
                AgentInstanceId = registration.InstanceId,
                IdempotencyKey = $"inspector-{Guid.NewGuid():N}",
                Flow = flow,
                TimeoutMs = 120_000
            },
            CreateWorkflowRunTarget(registration),
            () => IsCurrentAgentConnection(connection),
            new WorkflowRunExecutionOptions
            {
                EvidenceCaptureFactory = evidenceCaptureFactory
            },
            leaseHandoff,
            dispatchOrigin: WorkflowRunDispatchOrigin.InspectorReplayBridge,
            dispatchTicket: IssueWorkflowRunDispatchTicket(
                registration,
                WorkflowRunDispatchOrigin.InspectorReplayBridge));
        if (!started.Ok || started.Run is null || string.IsNullOrWhiteSpace(started.CapabilityToken))
        {
            throw new WorkflowRunRejectedException(
                started.StatusCode == 0 ? 500 : started.StatusCode,
                started.Error ?? "The broker could not start the workflow run.");
        }

        var snapshot = await _workflowRuns.WaitForTerminalAsync(
            started.Run.RunId,
            started.CapabilityToken,
            CancellationToken.None).ConfigureAwait(false);
        if (snapshot.CompatibilityReport is not null)
            return snapshot.CompatibilityReport;
        if (snapshot.Report is not null)
        {
            var compatibility = FlowReplayReportAdapter.ToLegacy(snapshot.Report, flow.Name);
            compatibility.Total = Math.Max(compatibility.Total, flow.Steps.Count);
            return compatibility;
        }

        return new FlowReplayReport
        {
            Ok = false,
            Name = flow.Name,
            Total = flow.Steps.Count,
            Failed = flow.Steps.Count,
            DivergencePoint = snapshot.FirstDivergence,
            StoppedEarly = true,
            Results =
            [
                new FlowStepResult
                {
                    Seq = snapshot.FirstDivergence ?? 0,
                    Action = "run",
                    Label = "Prepare run",
                    Ok = false,
                    FailureKind = FlowFailureKinds.Drive,
                    Error = snapshot.Message ?? "The workflow run failed before the first step."
                }
            ]
        };
    }

    private async Task ServeAgentListPage(HttpListenerContext context)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'><title>DevFlow Inspector</title>");
        sb.AppendLine("<style>body{font-family:system-ui;background:#1e1e1e;color:#fff;padding:20px}");
        sb.AppendLine("a{color:#4ec9b0;text-decoration:none}a:hover{text-decoration:underline}");
        sb.AppendLine(".agent{padding:12px;margin:8px 0;background:#2d2d2d;border-radius:6px}</style></head><body>");
        sb.AppendLine("<h1>DevFlow Inspector</h1>");

        if (_agents.IsEmpty)
        {
            sb.AppendLine("<p>No agents connected. Start a MAUI app with DevFlow enabled.</p>");
        }
        else
        {
            foreach (var agent in _agents.Values)
            {
                var reg = agent.Registration;
                sb.AppendLine($"<div class='agent'>");
                sb.AppendLine($"<a href='/inspector/{HttpUtility.UrlEncode(reg.Id)}/'><strong>{HttpUtility.HtmlEncode(reg.AppName)}</strong></a>");
                sb.AppendLine($" — {HttpUtility.HtmlEncode(reg.Platform)} ({HttpUtility.HtmlEncode(reg.Tfm)}) on port {reg.Port}");
                sb.AppendLine($"</div>");
            }
        }

        sb.AppendLine("</body></html>");
        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/html; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }
}
